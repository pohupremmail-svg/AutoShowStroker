using System.Text.Json;
using System.Text.Json.Nodes;
using Gooner.Core.Climax;
using Gooner.Core.Data;
using Gooner.Core.Localization;
using Gooner.Core.Logging;
using Gooner.Core.Randomness;
using Gooner.Core.Sessions;
using Gooner.Core.Settings;
using Gooner.Core.Timing;

namespace Gooner.Core.Beat;

// One note the rhythm is about to play: seconds from now, whether it is heard, and its weight.
public readonly record struct UpcomingBeat(double Offset, bool Audible, int Weight);

// One note the rhythm is about to play, at the session-clock moment it will fire.
public readonly record struct ScheduledNote(double At, bool Audible, int Weight);

// A stretch of the plan as the note track draws it - a rhythm ("beat"/"finale", with its pattern)
// or a pause - from the moment it takes over to the moment the next one does, on the session clock.
public readonly record struct ForecastSpan(string Kind, string? PatternName, double Start, double End);

public sealed record TrackForecast(IReadOnlyList<ScheduledNote> Notes, IReadOnlyList<ForecastSpan> Spans);

// The rhythm engine. Port of src/BeatHandler.py.
//
// The session is *planned*, not improvised: StartBeat builds a rolling buffer of Segments (a
// rhythm, a pause, or the finale run-in) and each segment change reads the next one off the plan
// instead of re-rolling. That is what lets anything make a promise about the future -
// UpcomingBeats predicts across pattern changes and through a running pause, and SetFinaleAt
// guarantees a fast rhythm across a given moment. A segment always starts exactly as it was
// planned; drift is corrected while segments are still queued and nothing of them is on screen.
// BeatHandler knows nothing about climaxes; it is only ever told "be fast here".
//
// No timers fire on their own in here. The two QTimers of the Python side are ClockTimers on a
// ClockScheduler, shared with whatever else runs on the session clock, and the host advances it
// every frame: each note or pause second that has come due fires, in order, at the moment it was
// due - so a late frame never shifts the rhythm. A note
// gap stays the whole milliseconds Python schedules (int(step * 1000)); at 48 kHz that is exact,
// and it keeps the two implementations comparable note for note.
public sealed class BeatHandler : IFinalePlanner, ISettingsOwner
{
    public static string SettingsGroup => "BeatHandler";
    private const string Log = "BeatHandler";
    private const string CustomPatternsName = "custom_patterns";

    // Hard cap on UpcomingBeats - at the top of the frequency range with the shortest steps a long
    // horizon would otherwise build a pointlessly huge list.
    public const int MaxLookaheadNotes = 64;

    // How many segments are held ready beyond the running one: deep enough that the finale is
    // always placed while it is still unstarted, shallow enough that a settings change never
    // invalidates much work.
    public const int PlanBufferSegments = 5;

    public static IReadOnlyDictionary<string, int[]> BeatPatternsMap => BeatPatterns.BuiltIn;

    // Single source of truth: the constructor starts from these, and "Reset to defaults" reads them.
    public static IReadOnlyDictionary<string, object> Defaults { get; } = new Dictionary<string, object>
    {
        ["max_beat_dur"] = 45.0,
        ["min_beat_dur"] = 15.0,
        ["max_beat_freq"] = 5.0,
        ["min_beat_freq"] = 0.5,
        ["min_pause_dur"] = 5,
        ["max_pause_dur"] = 20,
        ["pause_chance"] = 0.05,
        // "I reached my edge": a pause on demand, and a gentler rhythm behind it.
        ["edge_relief_active"] = true,
        ["edge_pause_dur"] = 20,
        // Without a cooldown, holding the key turns the session into a nap.
        ["edge_cooldown_sec"] = 60,
        ["ramping_active"] = true,
        ["min_ramp_duration"] = 600.0,
        ["max_ramp_duration"] = 1800.0,
        ["ramp_window_width"] = 0.4,
        ["beat_loudness"] = 1.0,
    };

    private readonly ISettingsStore? _settings;
    private readonly UserDataStore? _dataStore;
    private readonly IRandomSource _random;
    private readonly ClockScheduler _scheduler;

    private double _pauseTotal;
    private int _nextIndex;
    private double _planEndTime;
    private bool _holding;
    private SessionScript? _script;
    // Rhythms a replayed session brought with it. Kept apart from the custom patterns so borrowing
    // someone else's session never quietly adopts their pattern library.
    private Dictionary<string, int[]> _borrowedPatterns = [];
    // Set by EdgeRelief, consumed by the next segment that is actually *drawn*.
    private bool _reliefNextSegment;
    private Dictionary<string, int[]> _customPatterns;

    public BeatHandler(
        ISettingsStore? settings = null,
        UserDataStore? dataStore = null,
        IRandomSource? random = null,
        ISessionClock? clock = null,
        ClockScheduler? scheduler = null)
    {
        _settings = settings;
        _dataStore = dataStore;
        _random = random ?? new SystemRandomSource();
        _scheduler = scheduler ?? new ClockScheduler(clock ?? new WallClock());
        // Created in the order Python creates them, which is the order a tie fires in.
        PauseTimer = _scheduler.CreateTimer(PauseLoop);
        NoteTimer = _scheduler.CreateTimer(Beat);

        MaxBeatDur = (double)Defaults["max_beat_dur"];
        MinBeatDur = (double)Defaults["min_beat_dur"];
        MaxBeatFreq = (double)Defaults["max_beat_freq"];
        MinBeatFreq = (double)Defaults["min_beat_freq"];
        MinPauseDur = (int)Defaults["min_pause_dur"];
        MaxPauseDur = (int)Defaults["max_pause_dur"];
        PauseChance = (double)Defaults["pause_chance"];
        EdgeReliefActive = (bool)Defaults["edge_relief_active"];
        EdgePauseDur = (int)Defaults["edge_pause_dur"];
        EdgeCooldownSec = (int)Defaults["edge_cooldown_sec"];
        RampingActive = (bool)Defaults["ramping_active"];
        MinRampDuration = (double)Defaults["min_ramp_duration"];
        MaxRampDuration = (double)Defaults["max_ramp_duration"];
        RampWindowWidth = (double)Defaults["ramp_window_width"];
        BeatLoudness = (double)Defaults["beat_loudness"];

        // Whatever the user has saved wins over the defaults above.
        if (_settings is not null)
        {
            MaxBeatDur = _settings.GetDouble(Key("max_beat_dur"), MaxBeatDur);
            MinBeatDur = _settings.GetDouble(Key("min_beat_dur"), MinBeatDur);
            MaxBeatFreq = _settings.GetDouble(Key("max_beat_freq"), MaxBeatFreq);
            MinBeatFreq = _settings.GetDouble(Key("min_beat_freq"), MinBeatFreq);
            MinPauseDur = _settings.GetInt(Key("min_pause_dur"), MinPauseDur);
            MaxPauseDur = _settings.GetInt(Key("max_pause_dur"), MaxPauseDur);
            PauseChance = _settings.GetDouble(Key("pause_chance"), PauseChance);
            EdgeReliefActive = _settings.GetBool(Key("edge_relief_active"), EdgeReliefActive);
            EdgePauseDur = _settings.GetInt(Key("edge_pause_dur"), EdgePauseDur);
            EdgeCooldownSec = _settings.GetInt(Key("edge_cooldown_sec"), EdgeCooldownSec);
            RampingActive = _settings.GetBool(Key("ramping_active"), RampingActive);
            MinRampDuration = _settings.GetDouble(Key("min_ramp_duration"), MinRampDuration);
            MaxRampDuration = _settings.GetDouble(Key("max_ramp_duration"), MaxRampDuration);
            RampWindowWidth = _settings.GetDouble(Key("ramp_window_width"), RampWindowWidth);
            BeatLoudness = _settings.GetDouble(Key("beat_loudness"), BeatLoudness);
            // Nothing saved yet: every built-in rhythm starts active.
            SelectedBeatPatterns = _settings.GetStringList(Key("selected_beat_patterns")) is { Count: > 0 } loaded
                ? [.. loaded]
                : [.. BeatPatternsMap.Keys];
        }
        else
        {
            SelectedBeatPatterns = [.. BeatPatternsMap.Keys];
        }

        // Pattern *definitions* are user-authored data, so they live in a data file rather than
        // the settings. Which patterns are *selected* is config, and stays in the settings.
        _customPatterns = LoadCustomPatterns();
        RefreshAvailablePatterns();
    }

    // --- events ---

    public event Action? BeatPaused;
    public event Action? BeatResumed;
    public event Action<double, string>? BeatChanged;
    // A heard note - the host plays the click on it.
    public event Action? BeatHit;
    // What the meter is doing: "idle", "new_beat" or "pause".
    public event Action<string>? MeterUpdated;
    // The session's start time and the script it replays (null when it is drawn). Raised before
    // the first segment is planned, so a listener can still place a finale into it - and the
    // script travels with it because the climax learns a session is a replay from here alone.
    public event Action<double, SessionScript?>? SessionPlanned;
    public event Action<IReadOnlyList<Segment>>? PlanExtended;
    public event Action<Segment>? SegmentStarted;
    // The rhythm has just committed to its next note, so any prediction taken off UpcomingBeats is
    // now stale. Deliberately carries nothing - what the note is can be read.
    public event Action? NoteScheduled;

    // --- settings ---

    public double MaxBeatDur { get; set; }
    public double MinBeatDur { get; set; }
    public double MaxBeatFreq { get; set; }
    public double MinBeatFreq { get; set; }
    public int MinPauseDur { get; set; }
    public int MaxPauseDur { get; set; }
    public double PauseChance { get; set; }
    public bool EdgeReliefActive { get; set; }
    public int EdgePauseDur { get; set; }
    public int EdgeCooldownSec { get; set; }
    public bool RampingActive { get; set; }
    public double MinRampDuration { get; set; }
    public double MaxRampDuration { get; set; }
    public double RampWindowWidth { get; set; }
    public double BeatLoudness { get; set; }
    public List<string> SelectedBeatPatterns { get; set; }
    public bool IsMuted { get; private set; }

    // --- state ---

    public double SessionStartTime { get; private set; }
    public double RampTargetDuration { get; private set; }
    public double CurFreq { get; internal set; }
    public double TargetBeatDur { get; private set; }
    public double CurBeatStartTime { get; private set; }
    public int[]? CurrentBeatPattern { get; internal set; }
    public string? CurrentBeatPatternName { get; internal set; }
    public int CurrentBeatPosition { get; internal set; }
    public int? CurPauseDur { get; internal set; }
    public double? FinaleAt { get; private set; }
    public Segment? CurrentSegment { get; internal set; }
    public IReadOnlyDictionary<string, int[]> CustomBeatPatterns => _customPatterns;
    public IReadOnlyDictionary<string, int[]> AvailableBeatPatterns { get; private set; } = new Dictionary<string, int[]>();

    // The segments queued behind the running one, oldest first.
    public IReadOnlyList<Segment> PlannedSegments => [.. Plan];

    internal List<Segment> Plan { get; } = [];
    internal double CurrentSegmentEnd { get; set; }
    internal int PatternAudibleCount { get; set; } = 1;
    internal double PatternInvSum { get; set; } = 1.0;
    internal ClockTimer NoteTimer { get; }
    internal ClockTimer PauseTimer { get; }

    // The session clock: the moment a timer was due while it fires, the clock itself otherwise.
    private double Now => _scheduler.Now;

    // The same clock, for the parity run to stamp events with.
    internal double ClockNow => Now;

    private static string Key(string name) => $"{SettingsGroup}/{name}";

    // --- the clock ---

    // Fires every timer on the session clock that has come due by now - this handler's notes and
    // pause seconds, and anything else sharing its scheduler. The host calls this every frame.
    public void Advance() => _scheduler.Advance();

    // --- the plan ---

    // Stops planning: the segment on the air now runs until the session ends. Called once the
    // climax has actually been announced - a new rhythm, a pause or a beat-change callout would
    // otherwise talk over the moment the session was built towards, and nothing further is
    // planned, so no fake climax can be rolled onto a boundary that will never arrive.
    public void HoldFinalSegment()
    {
        _holding = true;
        Plan.Clear();
    }

    // Asks the planner to guarantee a fast rhythm across the given moment - "be fast here", not
    // "the orgasm is here". Null withdraws it. Replans what is queued, so it works mid-session.
    public void SetFinaleAt(double? moment)
    {
        FinaleAt = moment;
        if (CurrentSegment is not null)
            ReplanFromNextSegment();
    }

    // Rebuilds the queued plan from the current settings. The running segment plays out
    // untouched, and the finale stays where it was drawn - re-rolling it would turn "open Settings
    // and save" into a lever for a different climax.
    public void ReplanFromNextSegment()
    {
        if (CurrentSegment is null || _holding) return;
        // Never rebuild a replay's queue: refilling it *draws* the replacements and the script's
        // cursor cannot rewind. A replay follows the recording, not changed settings.
        if (QueueIsRecorded()) return;
        Plan.Clear();
        _planEndTime = CurrentSegmentEnd;
        ExtendPlan();
    }

    internal void ExtendPlan()
    {
        if (_holding) return;
        var added = new List<Segment>();
        while (Plan.Count < PlanBufferSegments)
        {
            var segment = PlanOneSegment(_planEndTime);
            Plan.Add(segment);
            _planEndTime += segment.DurationSec;
            _nextIndex++;
            added.Add(segment);
        }
        if (added.Count > 0)
            PlanExtended?.Invoke(added);
    }

    private string? LastPlannedKind() => Plan.Count > 0 ? Plan[^1].Kind : CurrentSegment?.Kind;

    // Draws the segment that begins at `start`. A session never opens on a pause, and two pauses
    // never follow each other.
    private Segment PlanOneSegment(double start)
    {
        var index = _nextIndex;
        // Consumed whatever happens: a replay hands out its recorded segment instead, and a flag
        // left standing would slow down some unrelated segment much later.
        var relief = _reliefNextSegment;
        _reliefNextSegment = false;
        if (_script?.NextSegment(index) is { } scripted)
            // No finale rule: a replayed session already has its run-in recorded.
            return scripted;

        Segment candidate;
        if (LastPlannedKind() is not (null or "pause") && _random.Uniform(0, 1) < PauseChance)
        {
            // Sorted, because an inverted range written by an older build must not kill a session.
            var (low, high) = MinPauseDur <= MaxPauseDur ? (MinPauseDur, MaxPauseDur) : (MaxPauseDur, MinPauseDur);
            candidate = new Segment("pause", _random.RandInt(low, high), null, null, index);
        }
        else
        {
            var (windowMin, windowMax) = CurrentFreqRange(atTime: start);
            var duration = _random.Uniform(MinBeatDur, MaxBeatDur);
            // Straight to the bottom of the window after an edge: what comes back has to be
            // gentler than whatever drove the user to ask for it.
            var freq = relief ? windowMin : _random.Uniform(windowMin, windowMax);
            candidate = new Segment("beat", duration, freq, _random.Choice(UsablePatternNames()), index);
        }
        return ApplyFinaleRule(candidate, start);
    }

    // A pause on demand, with a gentler rhythm behind it. Returns the seconds given, or 0 when
    // there is nothing to relieve: while a pause is already running (it would hand out a free
    // second pause) and after the climax, where nothing further is planned. Says nothing about
    // the climax - the caller pushes it back by the returned amount.
    public double EdgeRelief()
    {
        if (CurrentSegment is null || _holding || IsPaused()) return 0.0;

        double seconds = EdgePauseDur;
        // The pause displaces everything behind it, so "be fast here" travels with it.
        if (FinaleAt is not null)
            FinaleAt += seconds;
        if (QueueIsRecorded())
        {
            // A replay's queue is the recording itself, already taken off the script, and the
            // cursor does not rewind - the break goes in front of it instead.
            Plan.Insert(0, new Segment("pause", seconds, null, null, _nextIndex));
        }
        else
        {
            _reliefNextSegment = true;
            Plan.Clear();
            Plan.Add(new Segment("pause", seconds, null, null, _nextIndex));
        }
        _nextIndex++;
        // Cuts the running segment short; BeginNextSegment re-anchors the plan clock to the real
        // start, so the rest of the session simply moves along with it.
        BeginNextSegment();
        return seconds;
    }

    // Whether what is queued came off a replayed session rather than being drawn - told by index,
    // since anything numbered below the script's length was replayed.
    private bool QueueIsRecorded() => _script is not null && Plan.Count > 0 && Plan[0].Index < _script.SegmentCount;

    // Whether a segment of `duration` starting at `start` is the run-in to the climax: less than
    // MinBeatDur would be left between its end and the finale moment.
    private bool CoversFinale(double start, double duration)
    {
        if (FinaleAt is not { } finaleAt || start >= finaleAt) return false;
        return finaleAt - (start + duration) < MinBeatDur;
    }

    // Fixes a queued segment that drift has moved into or out of the finale window. Segments end
    // at the first note past their planned end, so the plan runs a little late; corrected here,
    // while it is still queued and nothing of it is on screen.
    private void RevalidateFinale()
    {
        if (FinaleAt is null) return;
        // A replay's run-in is recorded where it belongs, and rebuilding would *draw*.
        if (QueueIsRecorded()) return;
        var start = CurrentSegmentEnd;
        for (var i = 0; i < Plan.Count; i++)
        {
            var segment = Plan[i];
            if (CoversFinale(start, segment.DurationSec) != (segment.Kind == "finale"))
            {
                Plan.RemoveRange(i, Plan.Count - i);
                _planEndTime = start;
                return;  // ExtendPlan refills from here with the rule applied
            }
            start += segment.DurationSec;
        }
    }

    // Turns the segment leading into the finale moment into the finale itself, stretched to run
    // MinBeatDur past the moment, so the fast rhythm is still going when the climax is announced.
    // Extending rather than clipping: a clipped stub would be too short to be a rhythm at all.
    private Segment ApplyFinaleRule(Segment candidate, double start)
    {
        if (!CoversFinale(start, candidate.DurationSec)) return candidate;
        var (_, windowMax) = CurrentFreqRange(atTime: start);
        return new Segment(
            "finale",
            Math.Max(candidate.DurationSec, FinaleAt!.Value + MinBeatDur - start),
            windowMax,
            _random.Choice(UsablePatternNames()),
            candidate.Index);
    }

    // Puts the next planned segment on the air and tops the plan back up - exactly as planned:
    // re-deriving it here re-drew a pattern the beat track had already painted.
    internal void BeginNextSegment()
    {
        if (Plan.Count == 0) ExtendPlan();
        var segment = Plan[0];
        Plan.RemoveAt(0);
        var now = Now;
        CurrentSegment = segment;
        CurBeatStartTime = now;
        CurrentSegmentEnd = now + segment.DurationSec;
        // Re-anchored to the real start, or the drift would accumulate across the session.
        _planEndTime = CurrentSegmentEnd + Plan.Sum(s => s.DurationSec);
        RevalidateFinale();
        ExtendPlan();

        if (segment.Kind == "pause")
            StartPause();
        else
            ApplyBeatSegment(segment);
        SegmentStarted?.Invoke(segment);
    }

    // --- running the beat ---

    // Starts a session. With a script the planner replays the recorded segments instead of
    // drawing its own, until the script runs out.
    public void StartBeat(SessionScript? script = null)
    {
        _script = script;
        _borrowedPatterns = script is null ? [] : script.CustomPatterns.ToDictionary(kv => kv.Key, kv => kv.Value);
        RefreshAvailablePatterns();
        SessionStartTime = Now;
        RampTargetDuration = _random.Uniform(MinRampDuration, MaxRampDuration);
        Plan.Clear();
        _nextIndex = 0;
        FinaleAt = null;
        CurrentSegment = null;
        _holding = false;
        _planEndTime = SessionStartTime;
        // Before the plan is built, so a listener still gets to place a finale into it.
        SessionPlanned?.Invoke(SessionStartTime, script);
        ExtendPlan();
        BeginNextSegment();
    }

    // Schedules the next note, moving on to the next segment when this one is spent.
    public void ResetBeatTimer()
    {
        if (CurrentSegment is null)
        {
            BeginNextSegment();
            return;
        }
        if (!_holding && Now >= CurrentSegmentEnd)
        {
            BeginNextSegment();
            return;
        }
        ScheduleNextNote();
    }

    private void ScheduleNextNote()
    {
        var baseStepSec = BaseStepSec();
        var beatTimeMs = (int)(baseStepSec * 1000 / Math.Abs(CurrentBeatPattern![CurrentBeatPosition]));
        CurrentBeatPosition = (CurrentBeatPosition + 1) % CurrentBeatPattern.Length;
        NoteTimer.Start(beatTimeMs);
        NoteScheduled?.Invoke();
    }

    // Seconds a weight-1 step lasts at the current frequency and pattern, normalised so CurFreq
    // means audible beats per second whatever the pattern's shape. The scheduler and the
    // prediction both use it, so they can never drift apart.
    private double BaseStepSec()
    {
        if (PatternAudibleCount > 0)
            return PatternAudibleCount / NonZero(CurFreq * PatternInvSum);
        return 1 / NonZero(CurFreq);  // defensive, no pattern lacks a beat
    }

    private static double BaseStepSecFor(int[] pattern, double freq)
    {
        var audible = pattern.Count(v => v > 0);
        var invSum = InvSum(pattern);
        if (audible > 0 && invSum > 0)
            return audible / NonZero(freq * invSum);
        return 1 / NonZero(freq);
    }

    // Python sums 1/|v| step by step from 0; so does this, to the last bit.
    private static double InvSum(int[] pattern)
    {
        var sum = 0.0;
        foreach (var v in pattern)
            sum += 1.0 / Math.Abs(v);
        return sum;
    }

    // Python raises on a division by zero where .NET would quietly hand out infinity.
    private static double NonZero(double divisor) =>
        divisor == 0 ? throw new DivideByZeroException("float division by zero") : divisor;

    // Predicted steps landing within the next horizon. Read-only. Predicts across segment
    // boundaries, because the next segments already exist in the plan, and *through* a running
    // pause, so the notes of the segment behind it fly in towards the moment the beat returns. A
    // pause still ends the prediction when it is the segment being predicted into. Mirrors the
    // scheduler's indexing exactly: the note landing at t takes its audibility from
    // pattern[position], and the gap to the next note from that same index.
    public IReadOnlyList<UpcomingBeat> UpcomingBeats(double horizonSec)
    {
        if (LookaheadStart() is not { } start) return [];
        var (offset, pattern, position, baseStepSec, segmentEndsIn, queued) = start;
        var upcoming = new List<UpcomingBeat>();
        while (offset <= horizonSec && upcoming.Count < MaxLookaheadNotes)
        {
            var step = pattern[position];
            upcoming.Add(new UpcomingBeat(offset, step > 0, Math.Abs(step)));
            if (offset < segmentEndsIn)
            {
                offset += baseStepSec / Math.Abs(step);
                position = (position + 1) % pattern.Length;
                continue;
            }
            // This note ends the segment: it is played off the pattern still on the air, and only
            // then does the rhythm move on, so the gap after it belongs to the next segment.
            if (queued.Count == 0) break;  // predicted past the plan
            var segment = queued.Dequeue();
            if (segment.Kind == "pause") break;  // nothing to draw through a pause
            if (segment.PatternName is null || !AvailableBeatPatterns.TryGetValue(segment.PatternName, out var nextPattern) || nextPattern.Length == 0)
                break;  // pattern deleted since it was planned
            pattern = nextPattern;
            segmentEndsIn = offset + segment.DurationSec;
            baseStepSec = BaseStepSecFor(pattern, segment.Freq!.Value);
            // A new segment starts at index 0, whose weight sets the gap to its first note; that
            // note is then read at index 1.
            offset += baseStepSec / Math.Abs(pattern[0]);
            position = 1 % pattern.Length;
        }
        return upcoming;
    }

    // When the coming notes will fire, exactly: session-clock moments worked out the way the
    // scheduler will work them out, gap by whole-millisecond gap, segment end by segment end. What
    // the click is mixed against - it goes into the audio a fraction of a second before its note
    // comes due, so it needs the moment itself, and UpcomingBeats (the Python side's, for the note
    // track) adds up unrounded gaps and drifts by up to a millisecond a note. Predicts the same
    // stretch UpcomingBeats does: through a running pause, up to a pause still to come, never past
    // the plan. Exact while the plan holds; anything that re-plans changes it, and the next frame's
    // prediction says so.
    public IReadOnlyList<ScheduledNote> UpcomingNotes(double horizonSec)
    {
        var until = Now + horizonSec;
        var notes = new List<ScheduledNote>();
        var queued = new Queue<Segment>(Plan);
        Run run;
        if (IsPaused())
        {
            // The pause ends on its last whole-second tick, and the segment behind it starts there.
            var end = PauseTimer.Due;
            for (var tick = 1; tick < (CurPauseDur ?? 0); tick++)
                end += 1000 / 1000.0;
            if (BeginRun(queued, end) is not { } next) return notes;
            run = next;
        }
        else
        {
            if (!NoteTimer.Active || CurFreq <= 0 || CurrentBeatPattern is not { Length: > 0 }) return notes;
            var segmentEnd = CurrentSegment is null || _holding ? double.PositiveInfinity : CurrentSegmentEnd;
            run = new Run(NoteTimer.Due, CurrentBeatPattern, CurrentBeatPosition, BaseStepSec(), segmentEnd);
        }

        while (run.Due <= until && notes.Count < MaxLookaheadNotes)
        {
            var step = run.Pattern[run.Position];
            notes.Add(new ScheduledNote(run.Due, step > 0, Math.Abs(step)));
            // ResetBeatTimer, as it will run when this note fires.
            if (run.Due >= run.SegmentEnd)
            {
                if (BeginRun(queued, run.Due) is not { } next) break;
                run = next;
                continue;
            }
            var gapMs = (int)(run.BaseStepSec * 1000 / Math.Abs(step));
            run = run with { Due = run.Due + gapMs / 1000.0, Position = (run.Position + 1) % run.Pattern.Length };
        }
        return notes;
    }

    // What the note track draws: UpcomingNotes' notes, and the stretches of the plan they fall in -
    // the one on the air from its start, then each one coming, with the moment it takes over. Unlike
    // UpcomingNotes it walks on through a pause still to come, which runs its planned whole seconds
    // exactly as StartPause will count them, so the pause flies in like the notes do and the notes
    // behind it are there too. A stretch's end is where its last note falls once the walk gets that
    // far, its planned end until then, and never while the final segment is held.
    public TrackForecast Forecast(double horizonSec)
    {
        var until = Now + horizonSec;
        var notes = new List<ScheduledNote>();
        var spans = new List<ForecastSpan>();
        if (CurrentSegment is not { } current) return new TrackForecast(notes, spans);
        var queued = new Queue<Segment>(Plan);
        Run? run;
        if (IsPaused())
        {
            var end = PauseTimer.Due;
            for (var tick = 1; tick < (CurPauseDur ?? 0); tick++)
                end += 1000 / 1000.0;
            spans.Add(new ForecastSpan("pause", null, CurBeatStartTime, end));
            run = BeginForecastRun(queued, end, until, spans);
        }
        else
        {
            if (!NoteTimer.Active || CurFreq <= 0 || CurrentBeatPattern is not { Length: > 0 }) return new TrackForecast(notes, spans);
            var segmentEnd = _holding ? double.PositiveInfinity : CurrentSegmentEnd;
            spans.Add(new ForecastSpan(current.Kind, current.PatternName, CurBeatStartTime, segmentEnd));
            run = new Run(NoteTimer.Due, CurrentBeatPattern, CurrentBeatPosition, BaseStepSec(), segmentEnd);
        }

        while (run is { } r && r.Due <= until && notes.Count < MaxLookaheadNotes)
        {
            var step = r.Pattern[r.Position];
            notes.Add(new ScheduledNote(r.Due, step > 0, Math.Abs(step)));
            if (r.Due >= r.SegmentEnd)
            {
                spans[^1] = spans[^1] with { End = r.Due };
                run = BeginForecastRun(queued, r.Due, until, spans);
                continue;
            }
            var gapMs = (int)(r.BaseStepSec * 1000 / Math.Abs(step));
            run = r with { Due = r.Due + gapMs / 1000.0, Position = (r.Position + 1) % r.Pattern.Length };
        }
        return new TrackForecast(notes, spans);
    }

    // BeginRun for the forecast: pauses are walked through, each a stretch of its own, until a
    // rhythm begins - or the horizon, the end of the plan or a deleted pattern ends the walk.
    private Run? BeginForecastRun(Queue<Segment> queued, double start, double until, List<ForecastSpan> spans)
    {
        while (queued.Count > 0 && start <= until)
        {
            var segment = queued.Dequeue();
            if (segment.Kind == "pause")
            {
                var seconds = Math.Max(1, (int)Math.Round(segment.DurationSec, MidpointRounding.ToEven));
                spans.Add(new ForecastSpan("pause", null, start, start + seconds));
                start += seconds;
                continue;
            }
            if (segment.Freq is not { } freq || freq == 0 || segment.PatternName is null
                || !AvailableBeatPatterns.TryGetValue(segment.PatternName, out var pattern) || pattern.Length == 0)
                return null;
            spans.Add(new ForecastSpan(segment.Kind, segment.PatternName, start, start + segment.DurationSec));
            var baseStepSec = BaseStepSecFor(pattern, freq);
            var gapMs = (int)(baseStepSec * 1000 / Math.Abs(pattern[0]));
            return new Run(start + gapMs / 1000.0, pattern, 1 % pattern.Length, baseStepSec, start + segment.DurationSec);
        }
        return null;
    }

    // A rhythm as UpcomingNotes walks it: the next note's moment, and what sets the ones after.
    private readonly record struct Run(double Due, int[] Pattern, int Position, double BaseStepSec, double SegmentEnd);

    // The next queued segment, begun at `start` the way BeginNextSegment and ApplyBeatSegment will
    // begin it - or null where the prediction stops: a pause, the end of the plan, a pattern gone.
    private Run? BeginRun(Queue<Segment> queued, double start)
    {
        if (queued.Count == 0) return null;
        var segment = queued.Dequeue();
        if (segment.Kind == "pause" || segment.Freq is not { } freq || freq == 0) return null;
        if (segment.PatternName is null || !AvailableBeatPatterns.TryGetValue(segment.PatternName, out var pattern) || pattern.Length == 0)
            return null;
        var baseStepSec = BaseStepSecFor(pattern, freq);
        var gapMs = (int)(baseStepSec * 1000 / Math.Abs(pattern[0]));
        return new Run(start + gapMs / 1000.0, pattern, 1 % pattern.Length, baseStepSec, start + segment.DurationSec);
    }

    // How much of the running pause is still to go, 1.0 down to 0.0; 0.0 when nothing is paused.
    // A fraction of the whole pause, not of the track's lead time.
    public double PauseProgress()
    {
        if (!IsPaused() || _pauseTotal <= 0) return 0.0;
        return Math.Max(0.0, Math.Min(1.0, PauseRemaining() / _pauseTotal));
    }

    // Seconds until the beat comes back: whole seconds counted down, plus the running one.
    private double PauseRemaining()
    {
        if (!IsPaused()) return 0.0;
        var wholeSecondsLeft = Math.Max(0, (CurPauseDur ?? 0) - 1);
        return wholeSecondsLeft + Math.Max(0, PauseTimer.RemainingMs) / 1000;
    }

    // Where UpcomingBeats begins, or null when there is nothing to predict. Mid-beat it carries on
    // from the note already scheduled; mid-pause it starts from the moment the beat returns, with
    // the segment already planned behind the pause.
    private (double Offset, int[] Pattern, int Position, double BaseStepSec, double SegmentEndsIn, Queue<Segment> Queued)? LookaheadStart()
    {
        var queued = new Queue<Segment>(Plan);
        if (IsPaused())
        {
            if (queued.Count == 0) return null;
            var segment = queued.Dequeue();
            if (segment.Kind == "pause") return null;
            if (segment.PatternName is null || !AvailableBeatPatterns.TryGetValue(segment.PatternName, out var pattern)
                || pattern.Length == 0 || segment.Freq is not { } freq || freq == 0)
                return null;
            var baseStepSec = BaseStepSecFor(pattern, freq);
            var resumesIn = PauseRemaining();
            return (resumesIn + baseStepSec / Math.Abs(pattern[0]), pattern, 1 % pattern.Length, baseStepSec,
                resumesIn + segment.DurationSec, queued);
        }

        var remainingMs = NoteTimer.RemainingMs;
        if (remainingMs < 0 || !NoteTimer.Active) return null;  // stopped
        if (CurFreq <= 0 || CurrentBeatPattern is not { Length: > 0 }) return null;
        // A held segment runs past its planned end with nothing queued behind it - treating that
        // end as a boundary would cut the prediction off at the first note.
        var segmentEndsIn = CurrentSegment is null || _holding ? double.PositiveInfinity : CurrentSegmentEnd - Now;
        return (remainingMs / 1000, CurrentBeatPattern, CurrentBeatPosition, BaseStepSec(), segmentEndsIn, queued);
    }

    // When the difficulty ramp tops out, or null when there is no ramp to wait for - before the
    // session or with ramping switched off. The latter is the point: it stops the Ramp duration
    // sliders from influencing anything while their own checkbox is unticked.
    public double? RampCompleteAt =>
        !RampingActive || RampTargetDuration <= 0 ? null : SessionStartTime + RampTargetDuration;

    // Ramp progress at `atTime` (default: now). The planner passes a segment's *planned start*,
    // which turns ramping into a curve the plan walks along.
    private double? RampProgress(double? atTime)
    {
        if (RampTargetDuration <= 0) return null;  // StartBeat has not run yet
        var elapsed = (atTime ?? Now) - SessionStartTime;
        return Math.Min(1.0, Math.Max(0.0, elapsed / RampTargetDuration));
    }

    internal (double Min, double Max) CurrentFreqRange(double? atTime = null)
    {
        var corridor = MaxBeatFreq - MinBeatFreq;
        var progress = RampProgress(atTime);
        if (!RampingActive || progress is null || corridor <= 0)
            return (MinBeatFreq, MaxBeatFreq);
        var width = corridor * RampWindowWidth;
        var windowMin = MinBeatFreq + progress.Value * (corridor - width);
        return (windowMin, windowMin + width);
    }

    // What can actually be played: the built-ins, the user's own, and whatever a replayed session
    // brought with it.
    private void RefreshAvailablePatterns()
    {
        var available = new OrderedDictionary<string, int[]>();
        foreach (var source in new IEnumerable<KeyValuePair<string, int[]>>[] { BeatPatternsMap, _customPatterns, _borrowedPatterns })
            foreach (var (name, steps) in source)
                available[name] = steps;
        AvailableBeatPatterns = available;
    }

    // Selected pattern names that actually have a definition - never empty. An empty or stale
    // selection is reachable without touching the code (untick every rhythm, delete the last
    // selected custom pattern), and a draw from nothing used to take the process down.
    private List<string> UsablePatternNames()
    {
        var usable = SelectedBeatPatterns.Where(AvailableBeatPatterns.ContainsKey).ToList();
        return usable.Count > 0 ? usable : [.. BeatPatternsMap.Keys];
    }

    // Puts a planned beat segment on the air. Rolls nothing - unless its pattern was deleted after
    // it was planned, which planning ahead makes possible at all.
    private void ApplyBeatSegment(Segment segment)
    {
        CurFreq = segment.Freq ?? 0;
        TargetBeatDur = segment.DurationSec;
        CurrentBeatPosition = 0;
        CurrentBeatPatternName = segment.PatternName;
        if (CurrentBeatPatternName is null || !AvailableBeatPatterns.TryGetValue(CurrentBeatPatternName, out var pattern))
        {
            CurrentBeatPatternName = _random.Choice(UsablePatternNames());
            pattern = AvailableBeatPatterns[CurrentBeatPatternName];
        }
        CurrentBeatPattern = pattern;
        PatternAudibleCount = pattern.Count(v => v > 0);
        PatternInvSum = InvSum(pattern);

        MeterUpdated?.Invoke("new_beat");
        BeatChanged?.Invoke(CurFreq, CurrentBeatPatternName);
        ScheduleNextNote();
    }

    public void SetMuted(bool muted) => IsMuted = muted;

    // A note comes due: heard or silent, then the next one is scheduled.
    public void Beat()
    {
        var playBeat = CurrentBeatPattern![CurrentBeatPosition] > 0;
        if (playBeat)
            BeatHit?.Invoke();
        ResetBeatTimer();
    }

    // True while a rhythm pause is counting down.
    public bool IsPaused() => PauseTimer.Active;

    public void StartPause()
    {
        NoteTimer.Stop();
        // Rounded half to even like Python's round(), not truncated: a replayed pause carries the
        // length it was measured at, and int(1.9987) shortened every one. At least 1, or a pause
        // of 0 would end at its first tick.
        if (CurrentSegment is { Kind: "pause" } pause)
        {
            CurPauseDur = Math.Max(1, (int)Math.Round(pause.DurationSec, MidpointRounding.ToEven));
        }
        else
        {
            var (low, high) = MinPauseDur <= MaxPauseDur ? (MinPauseDur, MaxPauseDur) : (MaxPauseDur, MinPauseDur);
            CurPauseDur = _random.RandInt(low, high);
        }
        // Kept so the track can draw how much of the pause is left; CurPauseDur counts down.
        _pauseTotal = CurPauseDur.Value;
        PauseTimer.Start(1000);
        MeterUpdated?.Invoke("pause");
        BeatPaused?.Invoke();
    }

    public void PauseLoop()
    {
        CurPauseDur--;
        if (CurPauseDur <= 0)
        {
            BeatResumed?.Invoke();
            PauseTimer.Stop();
            BeginNextSegment();
            return;
        }
        PauseTimer.Start(1000);
        MeterUpdated?.Invoke("pause");
    }

    void IFinalePlanner.EndRhythm() => Stop();

    // Ends the rhythm.
    public void Stop()
    {
        NoteTimer.Stop();
        PauseTimer.Stop();
        Plan.Clear();
        CurrentSegment = null;
        FinaleAt = null;
        _holding = false;
        CurFreq = 0;
        _pauseTotal = 0.0;
        MeterUpdated?.Invoke("idle");
    }

    // --- custom patterns ---

    private static void ValidatePatternSteps(IReadOnlyCollection<int> steps)
    {
        if (steps.Count == 0)
            throw new ArgumentException(L10n.T("A pattern needs at least one step."));
        if (!steps.All(v => Math.Abs(v) is >= 1 and <= 4))
            throw new ArgumentException(L10n.T("Every step's weight must have a magnitude between 1 and 4."));
        if (!steps.Any(v => v > 0))
            throw new ArgumentException(L10n.T("A pattern needs at least one audible step."));
    }

    public void AddOrUpdateCustomPattern(string? name, IReadOnlyList<int> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        name = name?.Trim() ?? "";
        if (name.Length == 0)
            throw new ArgumentException(L10n.T("Pattern name must not be empty."));
        if (BeatPatternsMap.ContainsKey(name))
            throw new ArgumentException(L10n.F("'{0}' collides with a built-in pattern name.", name));
        ValidatePatternSteps(steps);

        AppLog.Info(Log, "Custom pattern {0} saved with {1} steps", PyJson.Quote(name), steps.Count);
        _customPatterns[name] = [.. steps];
        RefreshAvailablePatterns();
        if (!SelectedBeatPatterns.Contains(name))
            SelectedBeatPatterns.Add(name);
        SaveCustomPatterns();
    }

    public void DeleteCustomPattern(string name)
    {
        AppLog.Info(Log, "Custom pattern {0} deleted", PyJson.Quote(name));
        _customPatterns.Remove(name);
        RefreshAvailablePatterns();
        SelectedBeatPatterns.Remove(name);
        SaveCustomPatterns();
        // Queued segments may still name it - drop them rather than let the fallback quietly
        // substitute something else later.
        ReplanFromNextSegment();
    }

    // Forgets every user-authored pattern. The built-ins are untouched - this is a data deletion,
    // not a rhythm reset.
    public void ClearCustomPatterns()
    {
        AppLog.Info(Log, "Clearing {0} custom pattern(s)", _customPatterns.Count);
        _customPatterns = [];
        RefreshAvailablePatterns();
        SelectedBeatPatterns = [.. UsablePatternNames().Where(BeatPatternsMap.ContainsKey)];
        _dataStore?.Delete(CustomPatternsName);
        _settings?.SetValue(Key("selected_beat_patterns"), Names(SelectedBeatPatterns));
        ReplanFromNextSegment();
    }

    private Dictionary<string, int[]> LoadCustomPatterns() =>
        _dataStore is null ? [] : SanitizeCustomPatterns(_dataStore.Load(CustomPatternsName));

    // Keeps only entries AddOrUpdateCustomPattern would have accepted. The data store guarantees
    // the file parses, not what it holds, and these files are advertised as hand-editable. Bad
    // entries are dropped rather than repaired, so the rest of the file stays usable.
    //
    // One difference: Python accepts a fractional weight (1.5) from a hand-edited file. The
    // editor never writes one and the session format refuses one, so here only whole numbers
    // count - 2.0 reads as 2, 1.5 drops the pattern.
    private static Dictionary<string, int[]> SanitizeCustomPatterns(JsonNode? raw)
    {
        if (raw is null) return [];
        if (raw is not JsonObject patterns)
        {
            AppLog.Warning(Log, "Ignoring custom patterns: expected an object");
            return [];
        }

        var clean = new Dictionary<string, int[]>();
        foreach (var (name, node) in patterns)
        {
            if (node is not JsonArray items)
            {
                AppLog.Warning(Log, "Skipping custom pattern {0}: not a list of steps", PyJson.Quote(name));
                continue;
            }
            var steps = new List<int>();
            foreach (var item in items)
            {
                // A bool is not a weight, however JSON spells it.
                if (item is not JsonValue v || v.GetValueKind() != JsonValueKind.Number) break;
                var number = PyJson.Number(v);
                if (number != Math.Floor(number)) break;
                // Anything past the 1..4 range is out either way; kept as 5 so the validation
                // below says so in its own words, without converting a huge number to int.
                steps.Add(Math.Abs(number) > 4 ? 5 : (int)number);
            }
            if (steps.Count != items.Count)
            {
                AppLog.Warning(Log, "Skipping custom pattern {0}: every step must be a whole number", PyJson.Quote(name));
                continue;
            }
            try
            {
                ValidatePatternSteps(steps);
            }
            catch (ArgumentException error)
            {
                AppLog.Warning(Log, "Skipping custom pattern {0}: {1}", PyJson.Quote(name), AppLog.Describe(error));
                continue;
            }
            clean[name] = [.. steps];
        }
        return clean;
    }

    private void SaveCustomPatterns()
    {
        if (_dataStore is not null)
        {
            var file = new JsonObject();
            foreach (var (name, steps) in _customPatterns)
                file[name] = new JsonArray([.. steps.Select(s => (JsonNode)s)]);
            _dataStore.Save(CustomPatternsName, file);
        }
        _settings?.SetValue(Key("selected_beat_patterns"), Names(SelectedBeatPatterns));
    }

    private static JsonArray Names(IEnumerable<string> names) => new([.. names.Select(n => (JsonNode)n)]);
}
