using System.Text.Json.Nodes;
using Gooner.Core.Achievements;
using Gooner.Core.Beat;
using Gooner.Core.Callouts;
using Gooner.Core.Characters;
using Gooner.Core.Climax;
using Gooner.Core.Data;
using Gooner.Core.Devices;
using Gooner.Core.Formatting;
using Gooner.Core.Localization;
using Gooner.Core.Logging;
using Gooner.Core.Media;
using Gooner.Core.Migration;
using Gooner.Core.Randomness;
using Gooner.Core.Scoring;
using Gooner.Core.Sessions;
using Gooner.Core.Settings;
using Gooner.Core.Timing;

namespace Gooner.Core.App;

public sealed record GoonerAppOptions
{
    // The user's settings; null for the defaults, written nowhere.
    public ISettingsStore? Settings { get; init; }

    // The data directory; null keeps everything in memory - history, achievements, saved sessions.
    public UserDataStore? DataStore { get; init; }

    // res/callouts; null for a session nobody talks in.
    public string? CalloutDir { get; init; }

    // res/locales, the translations of the app's own words; null for English alone.
    public string? LocaleDir { get; init; }

    // res/characters: the .vrm figures on offer for the start screen.
    public string? CharacterDir { get; init; }

    // One stream of dice for every handler, so a seed replays a whole session.
    public IRandomSource? Random { get; init; }

    // What a saved session says it was saved with, and what What's New compares against.
    public string Version { get; init; } = "unknown";

    // The entries What's New shows; null for the shipped ones.
    public IReadOnlyList<ChangelogEntry>? Changelog { get; init; }

    // Device output over Intiface - optional, as the plugin folder was on the Python side. Off, the
    // app has no Intiface at all.
    public bool DeviceOutput { get; init; }

    // How what the device connection reports reaches the window's thread; inline when not given.
    public Action<Action>? Post { get; init; }

    // What the import of the Python app's profile did before the stores were built. It ran before
    // there was a log - the switch for one is among the settings it imports - so it is written
    // down here, once the log is configured.
    public MigrationReport? Migration { get; init; }
}

// The session's conductor: src/GoonerApp.py without its window. The lifecycle (Start, Stop, the
// ways a session ends, replaying and saving one), the wiring - who hears what from whom, all of it
// in Wire() - and the state the window draws: whether a session runs, the edge button, the question
// after a climax, the banner and the captions. The engine sets the clock once a frame and calls
// Frame(); everything here runs off that.
//
// One thing works differently because a window here has no modal dialogs: Stop asks how the session
// ended by raising HowItEndedAsked, and the session is recorded once AnswerHowItEnded comes back.
// Everything has gone quiet by then, as before.
public sealed class GoonerApp : ISettingsOwner
{
    public static string SettingsGroup => "GoonerApp";
    private const string Log = "GoonerApp";

    // How long the "denied" banner stays up before the session is ended for the user.
    public const int DeniedStopDelayMs = 5000;

    // How long a denied session waits for the user to say what they actually did before ending on
    // its own. Long, because the answer is the point; capped, so it cannot sit there forever.
    public const int DeniedAnswerTimeoutMs = 30000;

    public static string EdgeButtonDefaultText => L10n.T("I reached my Edge");

    // Socials > Join Discord.
    public const string DiscordInviteUrl = "https://discord.gg/qqkcxvq37Z";

    public static IReadOnlyDictionary<string, object> Defaults { get; } = new Dictionary<string, object>
    {
        ["show_startup_splash"] = true,
        ["ask_for_outcome"] = true,
        ["diagnostic_log"] = false,
        ["diagnostic_log_level"] = AppLog.DefaultLevel,
    };

    private readonly ISettingsStore? _settings;
    private readonly UserDataStore? _data;
    private readonly IRandomSource _random;
    private readonly string _version;

    public GoonerApp(IMediaStage stage, FrameClock clock, GoonerAppOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(clock);
        options ??= new GoonerAppOptions();
        _settings = options.Settings;
        _data = options.DataStore;
        _random = options.Random ?? new SystemRandomSource();
        _version = options.Version;
        ChangelogEntries = options.Changelog ?? App.Changelog.Entries;
        Clock = clock;
        Scheduler = new ClockScheduler(clock);

        // Storage and logging first, so everything after it can already log.
        DiagnosticLog = ReadBool("diagnostic_log");
        DiagnosticLogLevel = _settings?.GetString(Key("diagnostic_log_level"), (string)Defaults["diagnostic_log_level"])
            ?? (string)Defaults["diagnostic_log_level"];
        AppLog.Configure(DiagnosticLog, _data?.BaseDir, DiagnosticLogLevel);
        AppLog.Info(Log, "GoonerApp {0} starting", _version);
        options.Migration?.WriteToLog();
        // Before anything is built that words something.
        UiLanguage = new UiLanguage(_settings, options.LocaleDir);
        Character = new CharacterChoice(_settings, options.CharacterDir);
        UiLanguage.Apply();
        ShowStartupSplash = ReadBool("show_startup_splash");
        AskForOutcome = ReadBool("ask_for_outcome");

        Beat = new BeatHandler(_settings, _data, _random, scheduler: Scheduler);
        Callouts = options.CalloutDir is null ? null : new CalloutHandler(options.CalloutDir, _settings, _data, _random, clock);
        Climax = new ClimaxHandler(Beat, (ICalloutVoice?)Callouts ?? new Silence(), _settings, _random, Scheduler);
        Score = new ScoreTracker(_data, clock);
        Achievements = new AchievementTracker(_data, time: clock);
        // In memory only - it holds media paths, which never go near the data directory.
        Recorder = new SessionRecorder(clock);
        Player = new PlaylistPlayer(stage, _settings, _random, Scheduler);
        Hud = new SessionHud(Score, _settings, Scheduler);
        Banner = new ClimaxBanner(Scheduler);
        Look = new Look(_settings);
        Window = new WindowState(_settings);

        if (options.DeviceOutput)
            Intiface = new IntifacePlugin(this, new IntifaceController(_settings, options.Post));

        DeniedStopTimer = Scheduler.CreateTimer(Stop, singleShot: true);
        // Ticks once a second while the edge button cools down, so it can count down.
        EdgeCooldownTimer = Scheduler.CreateTimer(TickEdgeCooldown);
        Wire();
    }

    private static string Key(string name) => $"{SettingsGroup}/{name}";

    private bool ReadBool(string name) =>
        _settings?.GetBool(Key(name), (bool)Defaults[name]) ?? (bool)Defaults[name];

    // --- what it is made of ---

    public FrameClock Clock { get; }
    public ClockScheduler Scheduler { get; }
    // Where everything is kept - null for a run that keeps nothing. Privacy & Data shows and
    // deletes it.
    public ISettingsStore? Settings => _settings;
    public UserDataStore? DataStore => _data;
    public BeatHandler Beat { get; }
    public CalloutHandler? Callouts { get; }
    public ClimaxHandler Climax { get; }
    public ScoreTracker Score { get; }
    public AchievementTracker Achievements { get; }
    public SessionRecorder Recorder { get; }
    public PlaylistPlayer Player { get; }
    public SessionHud Hud { get; }
    public ClimaxBanner Banner { get; }
    public Look Look { get; }

    public CharacterChoice Character { get; }
    public WindowState Window { get; }
    public UiLanguage UiLanguage { get; }
    public IntifacePlugin? Intiface { get; }
    public ClockTimer DeniedStopTimer { get; }
    public ClockTimer EdgeCooldownTimer { get; }

    // --- settings ---

    public bool ShowStartupSplash { get; set; }
    public bool AskForOutcome { get; set; }
    public bool DiagnosticLog { get; private set; }
    public string DiagnosticLogLevel { get; private set; }

    // --- what the window draws ---

    public bool IsRunning { get; private set; }
    public bool IsMuted { get; private set; }

    public string LoadButtonText => IsRunning ? L10n.T("Change Gooning Folder.") : L10n.T("Set Gooning Folder and Start.");

    public bool EdgeButtonVisible { get; private set; } = true;
    public int EdgeCooldownLeft { get; private set; }
    public bool EdgeButtonEnabled => IsRunning && EdgeCooldownLeft <= 0;
    public string EdgeButtonText => EdgeCooldownLeft > 0 ? L10n.F("Edge ({0}s)", EdgeCooldownLeft) : EdgeButtonDefaultText;

    // The row of answers under the banner: "climax" or "fake" while it is up, null otherwise. The
    // same three answers at both, identically - a fake only works while it is indistinguishable.
    public string? OutcomeQuestion { get; private set; }

    // Stop has asked how the session ended and is waiting for AnswerHowItEnded.
    public bool HowItEndedPending { get; private set; }

    // What the session just ended earned, held for the recap.
    public IReadOnlyList<Achievement>? NewAchievements { get; private set; }

    // --- events ---

    public event Action? SessionStarted;
    public event Action? SessionEnded;
    // The window shows the recap: Score, NewAchievements, Recorder.Timeline() and SaveCurrentSession.
    public event Action? StatisticsRequested;
    // The window asks "Before the recap - what actually happened?" and answers AnswerHowItEnded.
    public event Action? HowItEndedAsked;
    public event Action? MediaSkipped;
    public event Action? MediaRepeated;
    // The panic key: the window minimises itself; anything else that has to become harmless right
    // now hangs off here.
    public event Action? Panicked;

    // --- the clock ---

    // Everything that came due by now fires. The host sets the clock first.
    public void Frame()
    {
        Scheduler.Advance();
        Callouts?.Tick();
    }

    // --- who hears what from whom ---

    // GoonerApp._setup_signal_handler. The climax's events go through one method each, which does
    // everything the slots connected to them did - and is what a test calls in place of emitting.
    private void Wire()
    {
        Beat.BeatPaused += Score.BeatPaused;
        Beat.BeatResumed += Score.BeatResumed;
        if (Callouts is not null)
        {
            Beat.BeatPaused += Callouts.PauseStarted;
            Beat.BeatResumed += Callouts.PauseEnded;
        }

        Beat.BeatHit += Score.Beat;
        Beat.BeatHit += Hud.RefreshRecordChase;

        Beat.BeatChanged += Score.BeatChanged;
        if (Callouts is not null)
            Beat.BeatChanged += Callouts.BeatChangeGeneral;

        // The climax reads the session plan instead of rolling dice per beat change.
        Beat.SessionPlanned += Climax.OnSessionPlanned;
        Beat.PlanExtended += Climax.OnPlanExtended;
        Beat.SegmentStarted += Climax.OnSegmentStarted;

        // What actually played, in order, for the Session Explorer.
        Beat.SegmentStarted += segment => Recorder.SegmentStarted(segment);
        Player.MediaShown += path => Recorder.MediaShown(path);

        Climax.OutcomeDecided += OnOutcomeDecided;
        Climax.StatusChanged += Banner.Update;
        Climax.FakeClimaxTriggered += OnFakeClimaxTriggered;
        Climax.FakeClimaxRevealed += OnFakeClimaxRevealed;

        if (Callouts is not null)
        {
            Callouts.TeaseShown += Hud.ShowTease;
            Callouts.TeaseHidden += Hud.HideTease;
        }
    }

    internal void OnOutcomeDecided(string outcome)
    {
        Recorder.ClimaxRecorded(outcome);
        Score.ClimaxDecided(outcome);
        AppLog.Info(Log, "Climax outcome: {0}", outcome);
        // Nothing left to be relieved of, and the planner would refuse it anyway.
        EdgeButtonVisible = false;
        EdgeCooldownTimer.Stop();
        ShowOutcomeButtons("climax");
        if (outcome == "denied")
            // Waits for the answer when there is one to wait for, or the buttons would be taken off
            // screen five seconds after being put there.
            DeniedStopTimer.Start(OutcomeQuestion is not null ? DeniedAnswerTimeoutMs : DeniedStopDelayMs);
    }

    internal void OnFakeClimaxTriggered()
    {
        Recorder.FakeClimaxRecorded();
        Score.FakeClimaxTriggered();
        ShowOutcomeButtons("fake");
    }

    // An unanswered fake takes its buttons back at the reveal - left up, they would hand the user a
    // second, stale set when the real climax arrives.
    internal void OnFakeClimaxRevealed() => HideOutcomeButtons();

    // --- the session ---

    // Starts a session. With a script the beats, the climax and the media pacing are replayed from a
    // saved session instead of drawn.
    public void Start(SessionScript? script = null)
    {
        if (HowItEndedPending)
        {
            // The question was left open; the session it was about still goes into the history,
            // unanswered, and without a recap nobody asked for.
            HowItEndedPending = false;
            EndSession(showStatistics: false);
        }
        if (!IsRunning)
        {
            Player.Script = script;
            // A denied outcome from the previous session may still have a stop pending, which would
            // kill the fresh session instead.
            DeniedStopTimer.Stop();
            HideOutcomeButtons();
            // Set before the event: anything reacting to "a session started" should see one running.
            IsRunning = true;
            // Called rather than hung off the event: the first medium is loaded a few lines below,
            // and the player has to be live by then.
            Player.SessionStarted();
            AppLog.Info(Log, "Session started");
            Score.SessionStarted();
            Recorder.SessionStarted();
            Callouts?.SessionStarted();
            Climax.SessionStarted();
            Hud.SessionStarted();
            SessionStarted?.Invoke();
            ResetEdgeButton();
            Beat.StartBeat(script);
        }
        // No autoplay reschedule here: LoadMedia schedules the next change for a picture and
        // deliberately not for a clip, which advances when it ends.
        Player.LoadCurrent();
    }

    // Stop means stop: everything goes quiet first, then the question is asked. Never twice: an
    // answer under the banner has ended the session already. (The Python side also keeps an
    // _outcome_answered flag for this; since answering ends the session it is never read while one
    // runs, so it is not carried over - nor is _announced_outcome, which nothing reads.)
    public void Stop()
    {
        if (!IsRunning) return;
        Halt();
        if (AskForOutcome)
        {
            HowItEndedPending = true;
            HowItEndedAsked?.Invoke();
            return;
        }
        EndSession(showStatistics: true);
    }

    // The answer to the question Stop asked: came, ruined, stopped - or null for "rather not say",
    // which is recorded as exactly that rather than guessed at.
    public void AnswerHowItEnded(string? reported)
    {
        if (!HowItEndedPending) return;
        HowItEndedPending = false;
        if (reported is not null)
            Score.OutcomeReported(reported);
        EndSession(showStatistics: true);
    }

    // The window is going away: a session still in progress is recorded first - never asked about,
    // never recapped, which is the last thing someone who just hit the X wants to see.
    //
    // The device is stopped after that, and its connection shut down - Intiface.Shutdown says whether
    // the host has to wait for the stop to go out before it ends the process.
    public void Close()
    {
        if (IsRunning || HowItEndedPending)
        {
            HowItEndedPending = false;
            EndSession(showStatistics: false);
        }
        Intiface?.Shutdown();
    }

    // Everything that has to fall silent, and nothing that writes anything down. Does nothing once
    // the session is already halted.
    private void Halt()
    {
        if (!IsRunning) return;
        Player.SessionEnded();
        Hud.SessionEnded();
        DeniedStopTimer.Stop();
        Beat.Stop();
        EdgeCooldownTimer.Stop();
        Banner.Freeze();
        IsRunning = false;
    }

    private void EndSession(bool showStatistics)
    {
        Halt();
        AppLog.Info(Log, "Session ended after {0} (statistics shown: {1})",
            Formatting.Clock.FormatClock(Score.LiveMetrics().GetValueOrDefault("total_dur_sec", 0)), showStatistics);
        Recorder.SessionEnded();
        Score.SessionEnded();
        SessionEnded?.Invoke();
        // After the history entry, so a rule that counts sessions can count this one.
        NewAchievements = Achievements.Evaluate(Score.DeliverInfos(), Score.GetHistory());
        if (showStatistics)
            StatisticsRequested?.Invoke();
    }

    // --- what plays next ---

    public void Next()
    {
        Player.ShowNext();
        Score.MediaSkipped();
        Callouts?.MediaSkipped();
        MediaSkipped?.Invoke();
    }

    public void Previous()
    {
        Player.ShowPrev();
        Score.MediaRepeated();
        Callouts?.MediaRepeated();
        MediaRepeated?.Invoke();
    }

    // The files a folder pick came back with: shuffled into the playlist and played. Counts only in
    // the log, never the folders - that would put the location of the collection on disk.
    public void LoadPlaylist(IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        Banner.Update("neutral");
        AppLog.Info(Log, "Playlist loaded: {0} files", files.Count);
        if (files.Count == 0)
        {
            Player.ShowMessage(L10n.T("No supported files found."));
            Stop();
            return;
        }
        var shuffled = files.ToList();
        // random.shuffle: from the back, each swapped with one at or before it.
        for (var i = shuffled.Count - 1; i > 0; i--)
        {
            var j = _random.NextIndex(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }
        Player.SetPlaylist(shuffled);
        Start();
    }

    // --- sound ---

    public void SetMuted(bool muted)
    {
        IsMuted = muted;
        Player.SetMuted(muted);
        Beat.SetMuted(muted);
    }

    public void ToggleMute() => SetMuted(!IsMuted);

    // Instant hide-and-silence. Deliberately not a stop (Ctrl+Space is that), and nothing unmutes on
    // its own afterwards - the user decides when sound comes back.
    public void Panic()
    {
        Panicked?.Invoke();
        SetMuted(true);
    }

    // --- what actually happened ---

    private void ShowOutcomeButtons(string context)
    {
        if (!AskForOutcome || !IsRunning) return;
        OutcomeQuestion = context;
    }

    private void HideOutcomeButtons() => OutcomeQuestion = null;

    // One of the three answers under the banner: came, ruined, stopped.
    public void ReportOutcome(string reported)
    {
        var context = OutcomeQuestion;
        HideOutcomeButtons();
        if (context == "fake")
        {
            // Not the session's outcome - the real climax is still to come. What it does record is
            // that the fake worked.
            if (reported is "came" or "ruined")
            {
                Score.FellForFakeClimax();
                Callouts?.ForceOutputSentence("fake_climax_fell_for");
            }
            return;
        }
        Score.OutcomeReported(reported);
        // Whatever the answer, the session is over: the climax has landed and the user has just said
        // what became of it.
        DeniedStopTimer.Stop();
        EndSession(showStatistics: true);
    }

    // --- reaching your edge ---

    // A pause now, a gentler rhythm behind it, and the climax pushed back to match - three things
    // that have to move together, which is why this lives here. Returns the seconds given, or 0 when
    // it was refused: no session, the button cooling down or not there, mid-pause, after the climax.
    public double EdgeReached()
    {
        if (!IsRunning || EdgeCooldownLeft > 0 || !EdgeButtonVisible) return 0;
        var seconds = Beat.EdgeRelief();
        if (seconds == 0) return 0;
        Climax.Postpone(seconds);
        Score.EdgeReached();
        Callouts?.ForceOutputSentence("edge_reached");
        AppLog.Info(Log, "Edge relief taken: {0:0}s", seconds);
        StartEdgeCooldown();
        return seconds;
    }

    private void StartEdgeCooldown()
    {
        EdgeCooldownLeft = Beat.EdgeCooldownSec;
        if (EdgeCooldownLeft <= 0) return;
        EdgeCooldownTimer.Start(1000);
    }

    private void TickEdgeCooldown()
    {
        EdgeCooldownLeft--;
        if (EdgeCooldownLeft <= 0)
        {
            EdgeCooldownLeft = 0;
            EdgeCooldownTimer.Stop();
        }
    }

    // What saving the settings sets off beyond the values themselves: the edge button follows its
    // switch at once - one that is still there but switched off would do nothing when pressed - the
    // captions follow theirs, and a running session plans what is still queued again while the
    // segment playing now plays out.
    public void SettingsSaved()
    {
        ResetEdgeButton();
        Hud.Refresh();
        if (!IsRunning) return;
        Climax.SettingsChanged();
        Beat.ReplanFromNextSegment();
    }

    // Back to a fresh session: there if the feature is on, live, off cooldown.
    private void ResetEdgeButton()
    {
        EdgeCooldownTimer.Stop();
        EdgeCooldownLeft = 0;
        EdgeButtonVisible = Beat.EdgeReliefActive;
    }

    // --- saved sessions ---

    // Plays a saved session again. With the recorded paths, they *are* the playlist - in the
    // recorded order, because that order is what the saved gaps were measured against. With
    // ignorePaths the user's own loaded playlist is used and only the pacing is replayed, so there
    // has to be one loaded. Returns whether it started.
    public bool ReplaySession(JsonObject saved, bool ignorePaths = false)
    {
        ArgumentNullException.ThrowIfNull(saved);
        if (ignorePaths)
        {
            if (Player.Playlist.Count == 0)
            {
                AppLog.Warning(Log, "Cannot replay against your own library: nothing is loaded.");
                return false;
            }
            // From the top: the index is left over from whatever played last.
            Player.CurrentIndex = 0;
        }
        else
        {
            var paths = SessionFiles.RecordedPaths(saved);
            if (paths.Count == 0)
            {
                AppLog.Warning(Log, "That saved session carries no media paths to replay.");
                return false;
            }
            Player.SetPlaylist(paths);
        }

        if (IsRunning || HowItEndedPending)
        {
            // No recap: the user asked for a replay, not for a recap of what they interrupted.
            HowItEndedPending = false;
            EndSession(showStatistics: false);
        }
        Banner.Update("neutral");
        AppLog.Info(Log, "Replaying a saved session (own library: {0})", ignorePaths);
        Start(new SessionScript(saved, ignorePaths));
        // After Start, which resets the tracker for the new session.
        Score.ReplayStarted();
        return true;
    }

    // Puts the session just played on the shelf. The one place in the app that writes media paths
    // to disk, and only because the user pressed Save. Returns whether it landed.
    public bool SaveCurrentSession()
    {
        if (_data is null) return false;
        var saved = SessionFiles.ToSavedSession(Recorder.Timeline(), Beat.CustomBeatPatterns, _version, Clock.GetLocalNow().DateTime);
        if (!SessionFiles.StoreSession(_data, saved)) return false;
        AppLog.Info(Log, "Session saved for replay: {0} segments", ((JsonArray)saved["segments"]!).Count);
        return true;
    }

    // --- what's new ---

    // The running version, as VERSION gives it.
    public string Version => _version;

    public IReadOnlyList<ChangelogEntry> ChangelogEntries { get; }

    // What has come since the version the user last saw - shown once, at startup - and that version
    // moves up to this one. A version that does not parse shows nothing and moves nothing.
    public IReadOnlyList<ChangelogEntry> WhatsNewOnStartup()
    {
        var lastSeen = _settings?.GetString(Key("last_seen_version"), "") ?? "";
        IReadOnlyList<ChangelogEntry> entries;
        try
        {
            entries = App.Changelog.EntriesSince(lastSeen, _version, ChangelogEntries);
        }
        catch (FormatException)
        {
            AppLog.Warning(Log, "Not checking for what is new: version {0} does not parse", _version);
            return [];
        }
        _settings?.SetValue(Key("last_seen_version"), _version);
        return entries;
    }

    // --- the diagnostic log ---

    // Applies and persists the opt-in log: flipping it has to reconfigure the live log, not just
    // write a key.
    public void SetDiagnosticLog(bool enabled, string? level = null)
    {
        DiagnosticLog = enabled;
        if (level is not null) DiagnosticLogLevel = level;
        _settings?.SetValue(Key("diagnostic_log"), DiagnosticLog);
        _settings?.SetValue(Key("diagnostic_log_level"), DiagnosticLogLevel);
        AppLog.Configure(DiagnosticLog, _data?.BaseDir, DiagnosticLogLevel);
        AppLog.Info(Log, "Diagnostic log {0} at level {1}", DiagnosticLog ? "enabled" : "disabled", DiagnosticLogLevel);
    }

    private sealed class Silence : ICalloutVoice
    {
        public void ForceOutputSentence(string category) { }
    }
}
