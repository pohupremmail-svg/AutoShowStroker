using Gooner.Core.Beat;
using Gooner.Core.Randomness;
using Gooner.Core.Sessions;
using Gooner.Core.Settings;
using Gooner.Core.Timing;

namespace Gooner.Core.Climax;

// Decides when the session peaks, and how, before it gets there. Port of src/ClimaxHandler.py.
//
// Nothing here is rolled at the moment it happens. The climax is placed onto the session clock the
// moment the session is planned, and its outcome is resolved in the same breath - which is what
// lets the planner shape a fast run-in towards it. Fake climaxes are pinned to planned segment
// boundaries a few segments ahead. The planner is told "be fast across this moment", never "the
// orgasm is here": this class is the only one that knows what a climax is.
//
// Its timers are ClockTimers on the scheduler it shares with the beat, so a climax and the notes
// around it fire in the order they were due.
public sealed class ClimaxHandler
{
    public const string SettingsGroup = "ClimaxHandler";

    // Single source of truth: the constructor starts from these, and "Reset to defaults" reads them.
    public static readonly IReadOnlyDictionary<string, object> Defaults = new Dictionary<string, object>
    {
        ["climax_active"] = true,
        // Seconds into the session. Measured from the start, deliberately not from the end of the
        // difficulty ramp: the ramp is a curve the user can switch off, and hanging the climax off
        // it meant the Ramp duration sliders silently decided when the orgasm came even with
        // ramping unticked. A range, so the session is not the same length twice.
        ["min_climax_after"] = 720.0,
        ["max_climax_after"] = 2100.0,
        // Hold the climax until the ramp has topped out, however early the draw came out - the
        // classic arc. Only ever later, never earlier, and nothing at all when ramping is off.
        ["climax_only_after_ramp"] = true,
        ["ruined_orgasm_active"] = false,
        ["ruined_orgasm_chance"] = 0.5,
        ["denied_orgasm_active"] = false,
        ["denied_orgasm_chance"] = 0.5,
        ["fake_climax_active"] = true,
        ["fake_climax_chance"] = 0.05,
        ["min_fake_climax_delay"] = 3.0,
        ["max_fake_climax_delay"] = 8.0,
    };

    private static readonly string[] Outcomes = ["real", "ruined", "denied"];

    private readonly IFinalePlanner _beat;
    private readonly ICalloutVoice _voice;
    private readonly IRandomSource _random;
    private readonly ClockScheduler _scheduler;
    private readonly ClockTimer _fakeClimaxTimer;
    // The climax runs off its own clock rather than off a segment boundary: segments end at the
    // first note past their planned end, so hanging the climax on one would let it drift later and
    // later over a long session.
    private readonly ClockTimer _climaxTimer;
    private readonly HashSet<int> _plannedFakes = [];
    // The moment the draw produced, before ClimaxOnlyAfterRamp clamps it. Kept so unticking the
    // box mid-session gives that moment back instead of re-rolling.
    private double? _drawnFinaleAt;
    // Set while replaying a saved session: the climax time, its outcome and the fake-outs come
    // from the file instead of from the dice.
    private SessionScript? _script;

    public ClimaxHandler(
        IFinalePlanner beat,
        ICalloutVoice voice,
        ISettingsStore? settings = null,
        IRandomSource? random = null,
        ClockScheduler? scheduler = null)
    {
        _beat = beat;
        _voice = voice;
        _random = random ?? new SystemRandomSource();
        _scheduler = scheduler ?? new ClockScheduler(new WallClock());

        ClimaxActive = (bool)Defaults["climax_active"];
        MinClimaxAfter = (double)Defaults["min_climax_after"];
        MaxClimaxAfter = (double)Defaults["max_climax_after"];
        ClimaxOnlyAfterRamp = (bool)Defaults["climax_only_after_ramp"];
        RuinedOrgasmActive = (bool)Defaults["ruined_orgasm_active"];
        RuinedOrgasmChance = (double)Defaults["ruined_orgasm_chance"];
        DeniedOrgasmActive = (bool)Defaults["denied_orgasm_active"];
        DeniedOrgasmChance = (double)Defaults["denied_orgasm_chance"];
        FakeClimaxActive = (bool)Defaults["fake_climax_active"];
        FakeClimaxChance = (double)Defaults["fake_climax_chance"];
        MinFakeClimaxDelay = (double)Defaults["min_fake_climax_delay"];
        MaxFakeClimaxDelay = (double)Defaults["max_fake_climax_delay"];

        if (settings is not null)
        {
            ClimaxActive = settings.GetBool(Key("climax_active"), ClimaxActive);
            MinClimaxAfter = settings.GetDouble(Key("min_climax_after"), MinClimaxAfter);
            MaxClimaxAfter = settings.GetDouble(Key("max_climax_after"), MaxClimaxAfter);
            ClimaxOnlyAfterRamp = settings.GetBool(Key("climax_only_after_ramp"), ClimaxOnlyAfterRamp);
            RuinedOrgasmActive = settings.GetBool(Key("ruined_orgasm_active"), RuinedOrgasmActive);
            RuinedOrgasmChance = settings.GetDouble(Key("ruined_orgasm_chance"), RuinedOrgasmChance);
            DeniedOrgasmActive = settings.GetBool(Key("denied_orgasm_active"), DeniedOrgasmActive);
            DeniedOrgasmChance = settings.GetDouble(Key("denied_orgasm_chance"), DeniedOrgasmChance);
            FakeClimaxActive = settings.GetBool(Key("fake_climax_active"), FakeClimaxActive);
            FakeClimaxChance = settings.GetDouble(Key("fake_climax_chance"), FakeClimaxChance);
            MinFakeClimaxDelay = settings.GetDouble(Key("min_fake_climax_delay"), MinFakeClimaxDelay);
            MaxFakeClimaxDelay = settings.GetDouble(Key("max_fake_climax_delay"), MaxFakeClimaxDelay);
        }

        // Created in the order Python creates them, which is the order a tie fires in.
        _fakeClimaxTimer = _scheduler.CreateTimer(RevealFakeClimax, singleShot: true);
        _climaxTimer = _scheduler.CreateTimer(OnClimaxDue, singleShot: true);
    }

    // "real" | "ruined" | "denied"
    public event Action<string>? OutcomeDecided;
    // "cum" | "ruined" | "denied" | "neutral" - for display.
    public event Action<string>? StatusChanged;
    public event Action? FakeClimaxTriggered;
    // The moment the joke is admitted. Anything that reacted to the fake as if it were real - the
    // outcome buttons above all - has to stand down again here.
    public event Action? FakeClimaxRevealed;

    public bool ClimaxActive { get; set; }
    public double MinClimaxAfter { get; set; }
    public double MaxClimaxAfter { get; set; }
    public bool ClimaxOnlyAfterRamp { get; set; }
    public bool RuinedOrgasmActive { get; set; }
    public double RuinedOrgasmChance { get; set; }
    public bool DeniedOrgasmActive { get; set; }
    public double DeniedOrgasmChance { get; set; }
    public bool FakeClimaxActive { get; set; }
    public double FakeClimaxChance { get; set; }
    public double MinFakeClimaxDelay { get; set; }
    public double MaxFakeClimaxDelay { get; set; }

    public bool ClimaxTriggered { get; internal set; }
    public bool FakeClimaxPending { get; internal set; }
    // When the climax is due and what it will be - both decided ahead of the moment they matter.
    public double? FinaleAt { get; internal set; }
    public string? Outcome { get; internal set; }

    // Segment indices still carrying an unfired fake climax. Deliberately not read by anything
    // that paints: a fake only works while it is indistinguishable from the real thing.
    public IReadOnlySet<int> PlannedFakeBoundaries => _plannedFakes.ToHashSet();

    public int ScriptedFakeCount => ScriptedFakeTimers.Count;

    internal List<ClockTimer> ScriptedFakeTimers { get; } = [];

    private double Now => _scheduler.Now;

    private static string Key(string name) => $"{SettingsGroup}/{name}";

    public void SessionStarted()
    {
        ClimaxTriggered = false;
        FakeClimaxPending = false;
        FinaleAt = null;
        _drawnFinaleAt = null;
        Outcome = null;
        _plannedFakes.Clear();
        _script = null;
        CancelScriptedFakes();
        _fakeClimaxTimer.Stop();
        _climaxTimer.Stop();
        // The one piece of the state above the user can see - a real climax emits its verdict and
        // nothing takes it back, so it used to stay on screen into the next session.
        StatusChanged?.Invoke("neutral");
    }

    // Places this session's climax the moment the planner starts. With a script the whole thing
    // is read off the saved session instead: the recorded time, the recorded outcome, and one timer
    // per recorded fake-out.
    public void OnSessionPlanned(double sessionStartTime, SessionScript? script = null)
    {
        _script = script;
        CancelScriptedFakes();
        if (script is not null)
        {
            ReplaySessionPlanned(sessionStartTime, script);
            return;
        }
        DrawClimax(sessionStartTime);
    }

    // Places a climax the ordinary way: drawn into the window, outcome resolved now. notBefore
    // holds it back past the end of a recording that had no climax of its own.
    private void DrawClimax(double sessionStartTime, double? notBefore = null)
    {
        if (!ClimaxActive)
        {
            Disarm();
            return;
        }
        var (low, high) = MinClimaxAfter <= MaxClimaxAfter ? (MinClimaxAfter, MaxClimaxAfter) : (MaxClimaxAfter, MinClimaxAfter);
        var drawn = sessionStartTime + _random.Uniform(low, high);
        _drawnFinaleAt = notBefore is { } floor ? Math.Max(drawn, floor) : drawn;
        FinaleAt = ClampedFinaleAt();
        Outcome = ResolveOutcome();
        ArmClimaxTimer();
        _beat.SetFinaleAt(FinaleAt);
    }

    // No climax this session - nothing armed, and the planner told not to build a run-in for one.
    private void Disarm()
    {
        FinaleAt = null;
        _drawnFinaleAt = null;
        Outcome = null;
        _climaxTimer.Stop();
        _beat.SetFinaleAt(null);
    }

    // The drawn moment, held back to the end of the ramp if the user asked for that. Only ever
    // later, never earlier - and never at all when ramping is off.
    private double? ClampedFinaleAt()
    {
        if (!ClimaxOnlyAfterRamp || _beat.RampCompleteAt is not { } rampCompleteAt)
            return _drawnFinaleAt;
        return Math.Max(_drawnFinaleAt!.Value, rampCompleteAt);
    }

    // Everything from the file. No draw, and deliberately no ramp clamp: the time was recorded, not
    // negotiated, and holding it back would replay a different session than the one saved.
    private void ReplaySessionPlanned(double sessionStartTime, SessionScript script)
    {
        foreach (var offset in script.FakeOffsets)
        {
            var timer = _scheduler.CreateTimer(OnScriptedFakeDue, singleShot: true);
            timer.Start(Math.Max(0, (int)((sessionStartTime + offset - Now) * 1000)));
            ScriptedFakeTimers.Add(timer);
        }

        if (script.ClimaxOffset is null)
        {
            // Stopped before any climax - almost always because the user did not last that long,
            // which is exactly the session someone saves to try again. So it is not replayed as a
            // session that can never finish: the recorded stretch plays as recorded, and from its
            // end on this is an ordinary session, climax and all - held past that end, or the
            // retry could finish sooner than the run it is retrying.
            DrawClimax(sessionStartTime, notBefore: sessionStartTime + script.Duration);
            return;
        }
        FinaleAt = sessionStartTime + script.ClimaxOffset.Value;
        _drawnFinaleAt = FinaleAt;
        Outcome = script.ClimaxOutcome;
        ArmClimaxTimer();
        _beat.SetFinaleAt(FinaleAt);
    }

    // Pushes the climax, and a replay's recorded fake-outs, back by `seconds` - called when the
    // user takes an edge break. The climax sits on an absolute clock, so without this a pause would
    // not buy them time; it would quietly spend the rhythm that was leading up to the climax.
    public void Postpone(double seconds)
    {
        if (seconds <= 0 || ClimaxTriggered || FinaleAt is null) return;
        FinaleAt += seconds;
        // The pre-clamp moment moves with it, or the next settings save would snap the climax
        // back to before the break.
        if (_drawnFinaleAt is not null)
            _drawnFinaleAt += seconds;
        ArmClimaxTimer();
        _beat.SetFinaleAt(FinaleAt);
        // Live fake-outs are pinned to segment indices and move with the plan by themselves. A
        // replay's are on absolute timers, like the climax.
        foreach (var timer in ScriptedFakeTimers)
            if (timer.Active)
                timer.Start(timer.RemainingMs + (int)(seconds * 1000));
    }

    private void OnScriptedFakeDue()
    {
        if (ClimaxTriggered || FakeClimaxPending) return;
        TriggerFakeClimax();
    }

    private void CancelScriptedFakes()
    {
        foreach (var timer in ScriptedFakeTimers)
            timer.Release();
        ScriptedFakeTimers.Clear();
    }

    private void ArmClimaxTimer() => _climaxTimer.Start(Math.Max(0, (int)((FinaleAt!.Value - Now) * 1000)));

    // Rolls the fake climaxes for boundaries that have just been planned.
    public void OnPlanExtended(IReadOnlyList<Segment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (!FakeClimaxActive) return;
        foreach (var segment in segments)
        {
            // Never on the finale: the "only joking" would land on top of the real announcement.
            if (segment.Kind == "finale") continue;
            // A recorded segment's fake-outs are recorded too and have their own timers. Only what
            // the planner drew *past* the end of the recording is rolled for.
            if (_script is not null && segment.Index < _script.SegmentCount) continue;
            if (_random.Uniform(0, 1) < FakeClimaxChance)
                _plannedFakes.Add(segment.Index);
        }
    }

    public void OnSegmentStarted(Segment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        if (!_plannedFakes.Remove(segment.Index)) return;
        if (ClimaxTriggered || FakeClimaxPending) return;
        TriggerFakeClimax();
    }

    internal void OnClimaxDue()
    {
        if (ClimaxTriggered) return;
        // A fake still waiting to be revealed would otherwise say "only joking" seconds after the
        // real thing.
        _fakeClimaxTimer.Stop();
        FakeClimaxPending = false;
        TriggerRealClimax();
    }

    // Re-derives what is still undecided after a mid-session settings save. The climax time is
    // kept - re-drawing it would make saving a lever for a different climax - but the outcome is
    // re-resolved, because switching ruined/denied on mid-session has to do something.
    public void SettingsChanged()
    {
        if (ClimaxTriggered) return;
        if (!ClimaxActive)
        {
            FinaleAt = null;
            Outcome = null;
            _climaxTimer.Stop();
            _beat.SetFinaleAt(null);
            return;
        }
        if (FinaleAt is null)
        {
            // Switched on mid-session: placed against the session already running, so it can land
            // in the past, which the timer treats as "due now".
            OnSessionPlanned(_beat.SessionStartTime);
            return;
        }
        Outcome = ResolveOutcome();
        // The drawn moment stands, but the ramp clamp is re-applied, so the box takes effect.
        var movedTo = ClampedFinaleAt();
        if (movedTo != FinaleAt)
        {
            FinaleAt = movedTo;
            ArmClimaxTimer();
            _beat.SetFinaleAt(FinaleAt);
        }
    }

    private void TriggerFakeClimax()
    {
        FakeClimaxPending = true;
        FakeClimaxTriggered?.Invoke();
        // The real-climax phrasing on purpose - the fake only works while it is indistinguishable
        // from the real thing until the reveal.
        _voice.ForceOutputSentence("climax_real");
        StatusChanged?.Invoke("cum");
        var delayMs = (int)(_random.Uniform(MinFakeClimaxDelay, MaxFakeClimaxDelay) * 1000);
        _fakeClimaxTimer.Start(delayMs);
    }

    internal void RevealFakeClimax()
    {
        FakeClimaxPending = false;
        FakeClimaxRevealed?.Invoke();
        _voice.ForceOutputSentence("fake_climax_reveal");
        StatusChanged?.Invoke("neutral");
    }

    internal void TriggerRealClimax()
    {
        ClimaxTriggered = true;
        var outcome = Outcome ?? ResolveOutcome();
        if (outcome == "denied")
            // No rhythm to ride out a denial: whatever happens next is the user's own doing
            // rather than the app still driving them through it.
            _beat.EndRhythm();
        else
            // The rhythm she said it over is the one that stays.
            _beat.HoldFinalSegment();
        var category = outcome switch { "ruined" => "climax_ruined", "denied" => "climax_denied", _ => "climax_real" };
        var status = outcome switch { "ruined" => "ruined", "denied" => "denied", _ => "cum" };
        _voice.ForceOutputSentence(category);
        StatusChanged?.Invoke(status);
        OutcomeDecided?.Invoke(outcome);
    }

    // The chances of real, ruined and denied, summing to one. Ruined and denied take priority over
    // real when they alone exceed 100%: then they are scaled down so all three still sum to 1.
    internal (double Real, double Ruined, double Denied) OutcomeWeights()
    {
        var ruined = RuinedOrgasmActive ? RuinedOrgasmChance : 0.0;
        var denied = DeniedOrgasmActive ? DeniedOrgasmChance : 0.0;
        var totalExtra = ruined + denied;
        if (totalExtra > 1.0)
        {
            ruined /= totalExtra;
            denied /= totalExtra;
        }
        return (Math.Max(0.0, 1.0 - ruined - denied), ruined, denied);
    }

    internal string ResolveOutcome()
    {
        if (!RuinedOrgasmActive && !DeniedOrgasmActive) return "real";
        var (real, ruined, denied) = OutcomeWeights();
        return _random.Choices(Outcomes, [real, ruined, denied]);
    }
}
