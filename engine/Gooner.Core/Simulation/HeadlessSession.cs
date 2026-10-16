using System.Globalization;
using System.Text.Json.Nodes;
using Gooner.Core.Achievements;
using Gooner.Core.Beat;
using Gooner.Core.Callouts;
using Gooner.Core.Climax;
using Gooner.Core.Randomness;
using Gooner.Core.Scoring;
using Gooner.Core.Sessions;
using Gooner.Core.Settings;
using Gooner.Core.Timing;

namespace Gooner.Core.Simulation;

// A clock that stands wherever it is put, in seconds since the Unix epoch - for everything that
// reads the time, the session clock and the TimeProviders alike.
public sealed class SimulatedClock(double start) : TimeProvider, ISessionClock
{
    public double Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddSeconds(Now);
}

public sealed record HeadlessOptions
{
    public const double DefaultLingerAfterClimax = 15.0;

    // One stream of dice for the whole session, so a seed replays it exactly.
    public int Seed { get; init; }

    // Where the simulated clock starts; the wall clock by default.
    public double? StartAt { get; init; }

    // res/callouts, or null for a session nobody talks in (forced lines are then simply unsaid).
    public string? CalloutDir { get; init; }

    // The user's settings, or null for the defaults. The overrides below win over both.
    public ISettingsStore? Settings { get; init; }

    public bool? ClimaxActive { get; init; }
    public double? MinClimaxAfter { get; init; }
    public double? MaxClimaxAfter { get; init; }
    public bool? ClimaxOnlyAfterRamp { get; init; }

    // Ambient callouts are off by default in the app; on here, so a run shows what is said.
    public bool ActiveCallouts { get; init; } = true;

    public double FrameSeconds { get; init; } = 1 / 60.0;

    // How long after the climax the session is stopped, as a user would.
    public double LingerAfterClimax { get; init; } = DefaultLingerAfterClimax;
}

// What happened, and when, in seconds from the start of the session.
public sealed record SessionEvent(double At, string What, string Detail);

// A whole session without a screen: the beat, the climax, the callouts, the score and the recorder
// wired the way GoonerApp wires them, on a simulated clock driven frame by frame as fast as the
// machine runs. What the console harness plays, and the reference for the engine's own wiring in
// phase 2. Touches no data directory: nothing here is given a store.
public sealed class HeadlessSession
{
    private readonly HeadlessOptions _options;
    private readonly List<SessionEvent> _events = [];
    private double _startedAt;
    private double? _outcomeAt;

    public HeadlessSession(HeadlessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        Clock = new SimulatedClock(options.StartAt ?? (DateTimeOffset.UtcNow - DateTimeOffset.UnixEpoch).TotalSeconds);
        Scheduler = new ClockScheduler(Clock);
        var random = new SystemRandomSource(new Random(options.Seed));

        Beat = new BeatHandler(options.Settings, random: random, scheduler: Scheduler);
        Callouts = options.CalloutDir is null
            ? null
            : new CalloutHandler(options.CalloutDir, options.Settings, random: random, time: Clock) { ActiveCallout = options.ActiveCallouts };
        Climax = new ClimaxHandler(Beat, (ICalloutVoice?)Callouts ?? new Silence(), options.Settings, random, Scheduler);
        if (options.ClimaxActive is { } active) Climax.ClimaxActive = active;
        if (options.MinClimaxAfter is { } min) Climax.MinClimaxAfter = min;
        if (options.MaxClimaxAfter is { } max) Climax.MaxClimaxAfter = max;
        if (options.ClimaxOnlyAfterRamp is { } afterRamp) Climax.ClimaxOnlyAfterRamp = afterRamp;
        Score = new ScoreTracker(time: Clock);
        Recorder = new SessionRecorder(Clock);
        Achievements = new AchievementTracker(dataStore: null, time: Clock);
        Wire();
    }

    public SimulatedClock Clock { get; }
    public ClockScheduler Scheduler { get; }
    public BeatHandler Beat { get; }
    public CalloutHandler? Callouts { get; }
    public ClimaxHandler Climax { get; }
    public ScoreTracker Score { get; }
    public SessionRecorder Recorder { get; }
    public AchievementTracker Achievements { get; }
    public IReadOnlyList<SessionEvent> Events => _events;
    public bool Ended { get; private set; }
    public IReadOnlyList<Achievement>? Unlocked { get; private set; }

    private double Elapsed => Clock.Now - _startedAt;

    private void Note(string what, string detail = "") => _events.Add(new SessionEvent(Elapsed, what, detail));

    // GoonerApp._setup_signal_handler, minus everything that paints.
    private void Wire()
    {
        Beat.BeatPaused += Score.BeatPaused;
        Beat.BeatResumed += Score.BeatResumed;
        if (Callouts is not null)
        {
            Beat.BeatPaused += Callouts.PauseStarted;
            Beat.BeatResumed += Callouts.PauseEnded;
        }
        Beat.BeatPaused += () => Note("pause");
        Beat.BeatResumed += () => Note("resume");

        Beat.BeatHit += Score.Beat;

        Beat.BeatChanged += Score.BeatChanged;
        if (Callouts is not null)
            Beat.BeatChanged += Callouts.BeatChangeGeneral;

        // The climax reads the session plan instead of rolling dice per beat change.
        Beat.SessionPlanned += Climax.OnSessionPlanned;
        Beat.PlanExtended += Climax.OnPlanExtended;
        Beat.SegmentStarted += Climax.OnSegmentStarted;

        // What actually played, in order.
        Beat.SegmentStarted += s => Recorder.SegmentStarted(s);
        Beat.SegmentStarted += s => Note("segment", s.Kind == "pause"
            ? string.Create(CultureInfo.InvariantCulture, $"pause {s.DurationSec:0.#}s")
            : string.Create(CultureInfo.InvariantCulture, $"{s.Kind} {s.PatternName} at {s.Freq:0.00}/s for {s.DurationSec:0.#}s"));

        Climax.OutcomeDecided += outcome => Recorder.ClimaxRecorded(outcome);
        Climax.FakeClimaxTriggered += () => Recorder.FakeClimaxRecorded();
        Climax.OutcomeDecided += Score.ClimaxDecided;
        Climax.OutcomeDecided += outcome =>
        {
            _outcomeAt = Clock.Now;
            Note("outcome", outcome);
        };
        Climax.FakeClimaxTriggered += Score.FakeClimaxTriggered;
        Climax.FakeClimaxTriggered += () => Note("fake");
        Climax.FakeClimaxRevealed += () => Note("revealed");

        if (Callouts is not null)
            Callouts.TeaseShown += text => Note("callout", text);
    }

    // GoonerApp.start: everything hears that a session started, then the beat plans it.
    public void Start(SessionScript? script = null)
    {
        _startedAt = Clock.Now;
        _outcomeAt = null;
        Ended = false;
        Score.SessionStarted();
        Recorder.SessionStarted();
        Callouts?.SessionStarted();
        Climax.SessionStarted();
        Beat.StartBeat(script);
        if (script is not null)
            // After the start, which resets the tracker for the new session.
            Score.ReplayStarted();
    }

    // GoonerApp.edge_reached: the beat grants the pause, the climax waits as long, and only then
    // does it count. 0 when it was refused - mid-pause, or after the climax.
    public double EdgeRelief()
    {
        var seconds = Beat.EdgeRelief();
        if (seconds == 0) return 0;
        Climax.Postpone(seconds);
        Score.EdgeReached();
        Callouts?.ForceOutputSentence("edge_reached");
        Note("edge", string.Create(CultureInfo.InvariantCulture, $"{seconds:0}s"));
        return seconds;
    }

    // Runs frame by frame until the session ends - LingerAfterClimax after its climax - or until
    // maxSeconds of it have passed, which ends it too.
    public void RunFor(double maxSeconds)
    {
        while (!Ended)
        {
            if (Elapsed >= maxSeconds || (_outcomeAt is { } at && Clock.Now - at >= _options.LingerAfterClimax))
            {
                End();
                return;
            }
            Frame();
        }
    }

    // Runs frame by frame up to `seconds` into the session, and leaves it running there.
    public void AdvanceTo(double seconds)
    {
        while (!Ended && Elapsed < seconds)
            Frame();
    }

    private void Frame()
    {
        Clock.Now += _options.FrameSeconds;
        Scheduler.Advance();
        Callouts?.Tick();
    }

    // GoonerApp._end_session: the rhythm halts, the recording closes, the score goes into the
    // history, and the session is judged against a history that holds it.
    public void End()
    {
        if (Ended) return;
        Beat.Stop();
        Recorder.SessionEnded();
        Score.SessionEnded();
        Unlocked = Achievements.Evaluate(Score.DeliverInfos(), Score.GetHistory());
        Ended = true;
        Note("end");
    }

    // The session as Save would put it on the shelf - paths and all, though a headless one has none.
    public JsonObject Save() =>
        SessionFiles.ToSavedSession(Recorder.Timeline(), Beat.CustomBeatPatterns, "headless", Clock.GetLocalNow().DateTime);

    private sealed class Silence : ICalloutVoice
    {
        public void ForceOutputSentence(string category) { }
    }
}
