using System.Globalization;
using System.Text.Json.Nodes;
using Gooner.Core.Achievements;
using Gooner.Core.App;
using Gooner.Core.Beat;
using Gooner.Core.Callouts;
using Gooner.Core.Climax;
using Gooner.Core.Media;
using Gooner.Core.Randomness;
using Gooner.Core.Scoring;
using Gooner.Core.Sessions;
using Gooner.Core.Settings;
using Gooner.Core.Timing;

namespace Gooner.Core.Simulation;

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

// A whole session without a screen: the app's own conductor (App.GoonerApp, wired exactly as the
// engine runs it) on a clock driven frame by frame as fast as the machine runs. What the console
// harness plays. Touches no data directory: the app is given no store, so the history,
// achievements and anything saved stay in memory.
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
        Clock = new FrameClock(options.StartAt ?? (DateTimeOffset.UtcNow - DateTimeOffset.UnixEpoch).TotalSeconds);
        App = new GoonerApp(new NoStage(), Clock, new GoonerAppOptions
        {
            Settings = options.Settings,
            CalloutDir = options.CalloutDir,
            Random = new SystemRandomSource(new Random(options.Seed)),
            Version = "headless",
        });
        if (App.Callouts is { } callouts) callouts.ActiveCallout = options.ActiveCallouts;
        if (options.ClimaxActive is { } active) Climax.ClimaxActive = active;
        if (options.MinClimaxAfter is { } min) Climax.MinClimaxAfter = min;
        if (options.MaxClimaxAfter is { } max) Climax.MaxClimaxAfter = max;
        if (options.ClimaxOnlyAfterRamp is { } afterRamp) Climax.ClimaxOnlyAfterRamp = afterRamp;
        Listen();
    }

    public FrameClock Clock { get; }
    public GoonerApp App { get; }
    public ClockScheduler Scheduler => App.Scheduler;
    public BeatHandler Beat => App.Beat;
    public CalloutHandler? Callouts => App.Callouts;
    public ClimaxHandler Climax => App.Climax;
    public ScoreTracker Score => App.Score;
    public SessionRecorder Recorder => App.Recorder;
    public AchievementTracker Achievements => App.Achievements;
    public IReadOnlyList<SessionEvent> Events => _events;
    public bool Ended { get; private set; }
    public IReadOnlyList<Achievement>? Unlocked => App.NewAchievements;

    private double Elapsed => Clock.Now - _startedAt;

    private void Note(string what, string detail = "") => _events.Add(new SessionEvent(Elapsed, what, detail));

    // What the harness prints. Listens alongside the app's own wiring, never instead of it.
    private void Listen()
    {
        Beat.BeatPaused += () => Note("pause");
        Beat.BeatResumed += () => Note("resume");
        Beat.SegmentStarted += s => Note("segment", s.Kind == "pause"
            ? string.Create(CultureInfo.InvariantCulture, $"pause {s.DurationSec:0.#}s")
            : string.Create(CultureInfo.InvariantCulture, $"{s.Kind} {s.PatternName} at {s.Freq:0.00}/s for {s.DurationSec:0.#}s"));
        Climax.OutcomeDecided += outcome =>
        {
            _outcomeAt = Clock.Now;
            Note("outcome", outcome);
        };
        Climax.FakeClimaxTriggered += () => Note("fake");
        Climax.FakeClimaxRevealed += () => Note("revealed");
        if (Callouts is not null)
            Callouts.TeaseShown += text => Note("callout", text);
    }

    // A session from the top: the app starts it, and a replay says so in the stats as
    // GoonerApp.replay_session does.
    public void Start(SessionScript? script = null)
    {
        _startedAt = Clock.Now;
        _outcomeAt = null;
        Ended = false;
        App.Start(script);
        if (script is not null)
            // After the start, which resets the tracker for the new session.
            Score.ReplayStarted();
    }

    // "I reached my edge": the seconds of pause it bought, or 0 when it was refused - cooling down,
    // mid-pause, or after the climax.
    public double EdgeRelief()
    {
        var seconds = App.EdgeReached();
        if (seconds > 0)
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
        App.Frame();
    }

    // Stopped the way closing the window stops it: recorded, judged, never asked about.
    public void End()
    {
        if (Ended) return;
        App.Close();
        Ended = true;
        Note("end");
    }

    // The session as Save would put it on the shelf - paths and all, though a headless one has none.
    public JsonObject Save() =>
        SessionFiles.ToSavedSession(Recorder.Timeline(), Beat.CustomBeatPatterns, "headless", Clock.GetLocalNow().DateTime);

    // Nothing to show anything on.
    private sealed class NoStage : IMediaStage
    {
        public void ShowImage(string path) { }

        public void ShowGif(string path) { }

        public void PlayVideo(string path, double volume) { }

        public void ReplayVideo() { }

        public void ShowMessage(string text) { }

        public void StopAll() { }

        public void SetMuted(bool muted) { }
    }
}
