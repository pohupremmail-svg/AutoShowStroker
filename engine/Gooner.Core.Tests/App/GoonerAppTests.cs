using System.Text.Json.Nodes;
using Gooner.Core.App;
using Gooner.Core.Callouts;
using Gooner.Core.Data;
using Gooner.Core.Logging;
using Gooner.Core.Media;
using Gooner.Core.Sessions;
using Gooner.Core.Settings;
using Gooner.Core.Tests.Support;
using Gooner.Core.Timing;

namespace Gooner.Core.Tests.App;

// Port of the session tests in tests/test_gooner_app.py: the conductor without its window. What is
// a widget there is state here - IsRunning for the greyed-out controls, OutcomeQuestion for the row
// of answers, the edge button's three properties - and the engine draws it. Where a Python test
// emits a handler's signal by hand, these call the conductor method that signal reaches, which
// does everything the connected slots did; a few run a session into the real thing to hold the
// wiring itself. Window-only tests (fullscreen, focus, menus, footer heights) belong to the engine.
public sealed class GoonerAppTests : IDisposable
{
    private const double Start = 1_800_000_000.0;
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-app-");
    private readonly FrameClock _clock = new(Start);
    private readonly FakeStage _stage = new();
    private readonly string _calloutDir;
    private readonly UserDataStore _data;
    private readonly JsonSettingsStore _settings;
    private GoonerApp? _app;

    public GoonerAppTests()
    {
        _calloutDir = Path.Combine(_tmp.FullName, "callouts");
        var phrases = new JsonObject();
        foreach (var key in CalloutHandler.TriggerKeys)
            phrases[key] = new JsonArray($"en {key} phrase");
        Directory.CreateDirectory(Path.Combine(_calloutDir, "en"));
        File.WriteAllText(Path.Combine(_calloutDir, "en", "flirty.json"), phrases.ToJsonString());
        _data = new UserDataStore(Path.Combine(_tmp.FullName, "data"));
        _settings = new JsonSettingsStore(new UserDataStore(Path.Combine(_tmp.FullName, "settings")));
        // A fake can fire on the very first segment (see TODO.md, finding 1), which would put the
        // question up in tests that do not expect one. The ones about fakes call them in by hand.
        _settings.SetValue("ClimaxHandler/fake_climax_active", false);
    }

    public void Dispose()
    {
        AppLog.Configure(false, null);
        _tmp.Delete(recursive: true);
    }

    private GoonerApp App => _app ??= new GoonerApp(_stage, _clock, new GoonerAppOptions
    {
        Settings = _settings,
        DataStore = _data,
        CalloutDir = _calloutDir,
        Random = new ScriptedRandom(1),
        Version = "0.14.3",
    });

    private void RunFor(double seconds)
    {
        var end = _clock.Now + seconds;
        while (_clock.Now < end)
        {
            _clock.Now = Math.Min(end, _clock.Now + 1 / 60.0);
            App.Frame();
        }
    }

    private static List<string> Files(params string[] names) => [.. names.Select(n => Path.Combine("C:", "media", n))];

    // What the recorder holds as shown, read a second on: with no time passed a segment is empty.
    private List<string> Shown() =>
        [.. App.Recorder.Timeline(_clock.Now + 1).Segments.SelectMany(s => s.Media).Where(m => !m.CarriedOver).Select(m => m.Path)];

    private List<string> Said()
    {
        var said = new List<string>();
        App.Callouts!.TeaseShown += said.Add;
        return said;
    }

    private void StartWith(params string[] names)
    {
        App.Player.SetPlaylist(Files(names.Length == 0 ? ["a.png"] : names));
        App.Start();
    }

    // Stop with the question answered "rather not say" - what the Python suite's stubbed box
    // answers, since a test cannot click it.
    private void StopSayingNothing()
    {
        App.Stop();
        App.AnswerHowItEnded(null);
    }

    // --- start / stop lifecycle ---

    [Fact]
    public void start_enables_controls_and_emits_session_started()
    {
        var started = 0;
        App.SessionStarted += () => started++;

        StartWith();

        Assert.Equal(1, started);
        Assert.True(App.IsRunning);
        Assert.Equal("Change Gooning Folder.", App.LoadButtonText);
    }

    [Fact]
    public void start_when_already_running_does_not_reemit_session_started()
    {
        StartWith();
        var started = 0;
        App.SessionStarted += () => started++;

        App.Start();

        Assert.Equal(0, started);
    }

    [Fact]
    public void stop_disables_controls_and_emits_session_ended()
    {
        StartWith();
        var ended = 0;
        App.SessionEnded += () => ended++;

        StopSayingNothing();

        Assert.Equal(1, ended);
        Assert.False(App.IsRunning);
        Assert.Equal("Set Gooning Folder and Start.", App.LoadButtonText);
    }

    [Fact]
    public void stop_when_not_running_is_noop()
    {
        var ended = 0;
        App.SessionEnded += () => ended++;

        App.Stop();

        Assert.Equal(0, ended);
        Assert.False(App.HowItEndedPending);
    }

    [Fact]
    public void stop_freezes_climax_banner_without_hiding_it()
    {
        StartWith();
        App.Banner.Update("cum");
        Assert.True(App.Banner.BlinkTimer.Active);

        StopSayingNothing();

        Assert.Equal("CUM", App.Banner.Text);
        Assert.False(App.Banner.BlinkTimer.Active);
        Assert.Equal(ClimaxBanner.StatusColors["cum"].On, App.Banner.Color);
    }

    [Fact]
    public void stop_when_no_climax_outcome_active_is_still_safe()
    {
        App.Climax.ClimaxActive = false;
        StartWith();

        StopSayingNothing();

        Assert.Null(App.Banner.Text);
        Assert.False(App.Banner.BlinkTimer.Active);
    }

    [Fact]
    public void stop_silences_everything_before_asking_how_it_ended()
    {
        StartWith();
        Assert.True(App.Beat.NoteTimer.Active);
        var seen = new Dictionary<string, bool>();
        App.HowItEndedAsked += () =>
        {
            seen["beat_running"] = App.Beat.NoteTimer.Active || App.Beat.PauseTimer.Active;
            seen["autoplay_running"] = App.Player.AutoPlayTimer.Active;
            seen["video_stopped"] = _stage.Calls.Contains("stop");
            seen["session_running"] = App.IsRunning;
        };

        App.Stop();

        Assert.False(seen["beat_running"], "the rhythm was still playing while the question was up");
        Assert.False(seen["autoplay_running"], "the slideshow was still advancing");
        Assert.True(seen["video_stopped"], "the video was still playing behind the question");
        Assert.False(seen["session_running"]);
        Assert.True(App.HowItEndedPending);
    }

    [Fact]
    public void the_reported_outcome_still_reaches_the_history()
    {
        StartWith();
        App.Stop();

        App.AnswerHowItEnded("came");

        Assert.Equal("came", App.Score.ReportedOutcome);
        Assert.Equal("came", (string?)App.Score.GetHistory()[^1]["reported_outcome"]);
    }

    [Fact]
    public void stopping_by_hand_asks_how_it_ended()
    {
        var asked = 0;
        App.HowItEndedAsked += () => asked++;
        StartWith();

        App.Stop();
        App.AnswerHowItEnded("stopped");

        Assert.Equal(1, asked);
        Assert.Equal("stopped", (string?)App.Score.GetHistory()[^1]["reported_outcome"]);
    }

    [Fact]
    public void the_question_can_be_switched_off()
    {
        var asked = 0;
        App.HowItEndedAsked += () => asked++;
        App.AskForOutcome = false;
        StartWith();

        App.OnOutcomeDecided("real");
        Assert.Null(App.OutcomeQuestion);

        App.Stop();

        Assert.Equal(0, asked);
        Assert.False(App.HowItEndedPending);
        Assert.Single(App.Score.GetHistory());
    }

    [Fact]
    public void an_answer_nobody_asked_for_is_ignored()
    {
        StartWith();

        App.AnswerHowItEnded("came");

        Assert.True(App.IsRunning);
        Assert.Null(App.Score.ReportedOutcome);
    }

    // A question left open when the next session starts: the session it was about still goes into
    // the history, unanswered, and without a recap nobody asked for.
    [Fact]
    public void starting_again_while_the_question_is_open_records_the_session_unanswered()
    {
        StartWith();
        App.Stop();
        var recaps = 0;
        App.StatisticsRequested += () => recaps++;

        App.Start();

        Assert.False(App.HowItEndedPending);
        Assert.Single(App.Score.GetHistory());
        Assert.Equal(0, recaps);
        Assert.True(App.IsRunning);
    }

    [Fact]
    public void stopping_shows_the_recap()
    {
        StartWith();
        var recaps = 0;
        App.StatisticsRequested += () => recaps++;

        StopSayingNothing();

        Assert.Equal(1, recaps);
    }

    // --- next / previous ---

    [Fact]
    public void btn_next_action_advances_and_emits_skip_event()
    {
        App.Player.SetPlaylist(Files("0.png", "1.png"));
        var skipped = 0;
        App.MediaSkipped += () => skipped++;

        App.Next();

        Assert.Equal(1, App.Player.CurrentIndex);
        Assert.Equal(1, skipped);
        Assert.Equal(1, App.Score.Skips);
    }

    [Fact]
    public void btn_prev_action_goes_back_and_emits_repeat_event()
    {
        App.Player.SetPlaylist(Files("0.png", "1.png"));
        App.Player.CurrentIndex = 1;
        var repeated = 0;
        App.MediaRepeated += () => repeated++;

        App.Previous();

        Assert.Equal(0, App.Player.CurrentIndex);
        Assert.Equal(1, repeated);
    }

    // --- loading a folder ---

    [Fact]
    public void open_folder_no_supported_files_shows_message_and_stays_stopped()
    {
        App.LoadPlaylist([]);

        Assert.Equal("message No supported files found.", _stage.Calls[^1]);
        Assert.False(App.IsRunning);
    }

    [Fact]
    public void open_folder_with_files_starts_session()
    {
        App.LoadPlaylist(Files("a.png", "b.png"));

        Assert.True(App.IsRunning);
        Assert.Equal(2, App.Player.Playlist.Count);
    }

    [Fact]
    public void open_folder_with_files_hides_climax_banner()
    {
        App.Banner.Update("cum");

        App.LoadPlaylist(Files("a.png"));

        Assert.Null(App.Banner.Text);
    }

    [Fact]
    public void open_folder_no_supported_files_hides_climax_banner()
    {
        App.Banner.Update("cum");

        App.LoadPlaylist([]);

        Assert.Null(App.Banner.Text);
    }

    [Fact]
    public void the_playlist_is_shuffled()
    {
        var files = Files([.. Enumerable.Range(0, 30).Select(i => $"{i}.png")]);

        App.LoadPlaylist(files);

        Assert.NotEqual(files, App.Player.Playlist);
        Assert.Equal(files.Order(), App.Player.Playlist.Order());
    }

    [Fact]
    public void media_paths_never_reach_the_log()
    {
        App.SetDiagnosticLog(true);

        App.LoadPlaylist([Path.Combine(_tmp.FullName, "very-private-folder", "clip.mp4")]);
        App.Player.VideoFailed("boom");

        var contents = File.ReadAllText(AppLog.LogFilePath(_data.BaseDir));
        Assert.DoesNotContain("very-private-folder", contents);
        Assert.Contains("Playlist loaded: 1 files", contents);
    }

    // --- mute and panic ---

    [Fact]
    public void set_muted_mutes_video_and_beat_audio()
    {
        App.SetMuted(true);

        Assert.True(App.IsMuted);
        Assert.True(App.Beat.IsMuted);
        Assert.Equal("muted True", _stage.Calls[^1]);
    }

    [Fact]
    public void toggle_mute_flips_state()
    {
        App.ToggleMute();
        Assert.True(App.IsMuted);

        App.ToggleMute();
        Assert.False(App.IsMuted);
    }

    // The window does the minimising; it is told to here.
    [Fact]
    public void panic_mutes_and_minimizes()
    {
        StartWith();
        var panicked = 0;
        App.Panicked += () => panicked++;

        App.Panic();

        Assert.True(App.IsMuted);
        Assert.Equal(1, panicked);
        // Deliberately not a stop: Ctrl+Space is that.
        Assert.True(App.IsRunning);
    }

    // --- the climax ---

    [Fact]
    public void on_climax_outcome_denied_schedules_stop()
    {
        App.OnOutcomeDecided("denied");

        Assert.True(App.DeniedStopTimer.Active);
        Assert.Equal(GoonerApp.DeniedStopDelayMs, App.DeniedStopTimer.IntervalMs);
    }

    [Theory]
    [InlineData("real")]
    [InlineData("ruined")]
    public void on_climax_outcome_non_denied_does_not_schedule_stop(string outcome)
    {
        App.OnOutcomeDecided(outcome);

        Assert.False(App.DeniedStopTimer.Active);
    }

    [Fact]
    public void climax_handler_status_event_wired_to_label()
    {
        _settings.SetValue("ClimaxHandler/min_climax_after", 3.0);
        _settings.SetValue("ClimaxHandler/max_climax_after", 3.0);
        _settings.SetValue("ClimaxHandler/climax_only_after_ramp", false);
        StartWith();

        RunFor(5.0);

        Assert.Equal("CUM", App.Banner.Text);
        Assert.Equal("climax", App.OutcomeQuestion);
        Assert.Equal("real", App.Score.ClimaxOutcome);
        Assert.NotNull(App.Recorder.Timeline().ClimaxAt);
        Assert.False(App.EdgeButtonVisible);
    }

    [Fact]
    public void climax_handler_fake_climax_event_wired_to_score_tracker()
    {
        App.OnFakeClimaxTriggered();

        Assert.Equal(1, App.Score.FakeoutCount);
    }

    [Fact]
    public void starting_a_new_session_cancels_a_pending_denied_stop()
    {
        StartWith();
        App.OnOutcomeDecided("denied");
        Assert.True(App.DeniedStopTimer.Active);

        App.Stop();
        App.AnswerHowItEnded(null);
        App.Start();

        Assert.False(App.DeniedStopTimer.Active);
    }

    [Fact]
    public void a_denied_session_waits_for_the_answer_instead_of_stopping_after_five_seconds()
    {
        StartWith();

        App.OnOutcomeDecided("denied");

        Assert.True(App.IsRunning);
        Assert.True(App.DeniedStopTimer.Active);
        Assert.Equal(GoonerApp.DeniedAnswerTimeoutMs, App.DeniedStopTimer.IntervalMs);
    }

    [Fact]
    public void an_unanswered_denial_stops_the_session_when_the_wait_is_over()
    {
        StartWith();
        App.OnOutcomeDecided("denied");

        RunFor(GoonerApp.DeniedAnswerTimeoutMs / 1000.0 + 0.1);

        Assert.False(App.IsRunning);
    }

    // --- reporting what actually happened ---

    [Fact]
    public void the_outcome_buttons_stay_hidden_until_something_is_announced()
    {
        StartWith();

        Assert.Null(App.OutcomeQuestion);
    }

    [Fact]
    public void a_real_climax_asks_what_happened()
    {
        StartWith();

        App.OnOutcomeDecided("real");

        Assert.Equal("climax", App.OutcomeQuestion);
    }

    [Fact]
    public void a_fake_climax_asks_exactly_the_same_thing()
    {
        StartWith();

        App.OnFakeClimaxTriggered();

        Assert.Equal("fake", App.OutcomeQuestion);
    }

    [Fact]
    public void no_question_outside_a_session()
    {
        App.OnOutcomeDecided("real");

        Assert.Null(App.OutcomeQuestion);
    }

    [Fact]
    public void reporting_at_a_real_climax_is_written_down()
    {
        StartWith();
        App.OnOutcomeDecided("denied");

        App.ReportOutcome("came");

        Assert.Equal("came", App.Score.ReportedOutcome);
        Assert.Equal("denied", App.Score.ClimaxOutcome);
    }

    [Fact]
    public void reporting_hides_the_buttons_again()
    {
        StartWith();
        App.OnOutcomeDecided("real");

        App.ReportOutcome("ruined");

        Assert.Null(App.OutcomeQuestion);
        Assert.Equal("ruined", App.Score.ReportedOutcome);
    }

    [Fact]
    public void coming_at_a_fake_out_counts_as_falling_for_it()
    {
        StartWith();
        App.OnFakeClimaxTriggered();

        App.ReportOutcome("came");

        Assert.Equal(1, App.Score.FakeoutsFallenFor);
        Assert.Null(App.Score.ReportedOutcome);
    }

    [Fact]
    public void holding_out_through_a_fake_out_is_not_counted_as_falling_for_it()
    {
        StartWith();
        App.OnFakeClimaxTriggered();

        App.ReportOutcome("stopped");

        Assert.Equal(0, App.Score.FakeoutsFallenFor);
    }

    [Fact]
    public void an_unanswered_fake_out_takes_its_buttons_away_at_the_reveal()
    {
        StartWith();
        App.OnFakeClimaxTriggered();

        App.OnFakeClimaxRevealed();

        Assert.Null(App.OutcomeQuestion);
        Assert.Equal(0, App.Score.FakeoutsFallenFor);
    }

    [Fact]
    public void falling_for_a_fake_out_gets_its_own_line()
    {
        StartWith();
        App.OnFakeClimaxTriggered();
        var said = Said();

        App.ReportOutcome("came");

        Assert.Equal(["en fake_climax_fell_for phrase"], said);
    }

    [Theory]
    [InlineData("real")]
    [InlineData("ruined")]
    [InlineData("denied")]
    public void answering_after_any_climax_ends_the_session(string announced)
    {
        StartWith();
        App.OnOutcomeDecided(announced);
        var recaps = 0;
        App.StatisticsRequested += () => recaps++;

        App.ReportOutcome("came");

        Assert.False(App.IsRunning);
        Assert.Equal(1, recaps);
        Assert.False(App.DeniedStopTimer.Active);
    }

    [Fact]
    public void reporting_at_a_fake_out_leaves_the_session_running()
    {
        StartWith();
        App.OnFakeClimaxTriggered();

        App.ReportOutcome("came");

        Assert.True(App.IsRunning);
    }

    [Fact]
    public void stopping_does_not_ask_again_when_the_climax_already_did()
    {
        var asked = 0;
        App.HowItEndedAsked += () => asked++;
        StartWith();
        App.OnOutcomeDecided("real");
        App.ReportOutcome("came");

        App.Stop();

        Assert.Equal(0, asked);
    }

    // --- closing the window ---

    [Fact]
    public void closing_the_window_records_the_session()
    {
        StartWith();
        var before = App.Score.GetHistory().Count;

        App.Close();

        Assert.Equal(before + 1, App.Score.GetHistory().Count);
        Assert.False(App.IsRunning);
    }

    [Fact]
    public void closing_the_window_does_not_open_the_statistics_dialog()
    {
        StartWith();
        var recaps = 0;
        App.StatisticsRequested += () => recaps++;

        App.Close();

        Assert.Equal(0, recaps);
    }

    [Fact]
    public void closing_the_window_never_asks()
    {
        var asked = 0;
        App.HowItEndedAsked += () => asked++;
        StartWith();

        App.Close();

        Assert.Equal(0, asked);
    }

    [Fact]
    public void closing_the_window_without_a_session_is_harmless()
    {
        App.Close();

        Assert.Empty(App.Score.GetHistory());
    }

    // The question was up when the window went: the session still counts.
    [Fact]
    public void closing_while_the_question_is_open_still_records_the_session()
    {
        StartWith();
        App.Stop();

        App.Close();

        Assert.Single(App.Score.GetHistory());
        Assert.False(App.HowItEndedPending);
    }

    // --- "I reached my Edge" ---

    [Fact]
    public void the_edge_button_is_only_live_during_a_session()
    {
        Assert.False(App.EdgeButtonEnabled);

        StartWith();

        Assert.True(App.EdgeButtonEnabled);
        Assert.Equal(GoonerApp.EdgeButtonDefaultText, App.EdgeButtonText);
    }

    [Fact]
    public void reaching_your_edge_buys_a_pause_and_pushes_the_climax_back()
    {
        App.Beat.EdgePauseDur = 15;
        App.Climax.ClimaxActive = true;
        App.Climax.MinClimaxAfter = App.Climax.MaxClimaxAfter = 600.0;
        App.Climax.ClimaxOnlyAfterRamp = false;
        StartWith();
        var before = App.Climax.FinaleAt!.Value;

        var given = App.EdgeReached();

        Assert.Equal(15.0, given);
        Assert.Equal("pause", App.Beat.CurrentSegment!.Kind);
        Assert.Equal(before + 15.0, App.Climax.FinaleAt!.Value, 9);
        Assert.Equal(1, App.Score.EdgeCount);
    }

    [Fact]
    public void reaching_your_edge_gets_its_own_line()
    {
        StartWith();
        var said = Said();

        App.EdgeReached();

        Assert.Equal(["en edge_reached phrase"], said);
    }

    [Fact]
    public void a_second_edge_is_refused_until_the_cooldown_is_up()
    {
        App.Beat.EdgeCooldownSec = 60;
        StartWith();
        App.EdgeReached();

        App.EdgeReached();

        Assert.Equal(1, App.Score.EdgeCount);
        Assert.False(App.EdgeButtonEnabled);
        Assert.Equal("Edge (60s)", App.EdgeButtonText);
    }

    // Past the pause it bought, the planner would grant another - the cooldown is what refuses it.
    [Fact]
    public void the_cooldown_refuses_an_edge_the_planner_would_still_take()
    {
        App.Beat.EdgePauseDur = 1;
        App.Beat.EdgeCooldownSec = 60;
        StartWith();
        App.EdgeReached();
        RunFor(3.0);
        Assert.False(App.Beat.IsPaused());

        Assert.Equal(0, App.EdgeReached());

        Assert.Equal(1, App.Score.EdgeCount);
    }

    [Fact]
    public void the_cooldown_counts_down_and_gives_the_button_back()
    {
        App.Beat.EdgeCooldownSec = 2;
        StartWith();
        App.EdgeReached();

        RunFor(1.0);
        Assert.Equal("Edge (1s)", App.EdgeButtonText);
        RunFor(1.0);

        Assert.True(App.EdgeButtonEnabled);
        Assert.Equal(GoonerApp.EdgeButtonDefaultText, App.EdgeButtonText);
    }

    [Fact]
    public void the_edge_button_goes_away_at_the_climax()
    {
        StartWith();

        App.OnOutcomeDecided("real");

        Assert.False(App.EdgeButtonVisible);
    }

    [Fact]
    public void a_new_session_hands_the_edge_button_back()
    {
        StartWith();
        App.OnOutcomeDecided("real");
        App.Close();

        App.Start();

        Assert.True(App.EdgeButtonVisible);
        Assert.True(App.EdgeButtonEnabled);
    }

    [Fact]
    public void the_edge_button_hides_entirely_when_switched_off()
    {
        App.Beat.EdgeReliefActive = false;

        StartWith();

        Assert.False(App.EdgeButtonVisible);
        Assert.Equal(0, App.EdgeReached());
    }

    // Refused mid-pause - it must not burn the cooldown or count.
    [Fact]
    public void an_edge_the_planner_refuses_costs_nothing()
    {
        StartWith();
        App.Beat.StartPause();

        Assert.Equal(0, App.EdgeReached());

        Assert.Equal(0, App.Score.EdgeCount);
        Assert.True(App.EdgeButtonEnabled);
    }

    // --- session recording ---

    [Fact]
    public void showing_a_medium_is_recorded()
    {
        StartWith("a.png");

        Assert.Equal(Files("a.png"), Shown());
    }

    [Fact]
    public void starting_a_segment_is_recorded()
    {
        StartWith();

        Assert.NotEmpty(App.Recorder.Timeline().Segments);
    }

    [Fact]
    public void the_recording_is_reset_for_each_session()
    {
        StartWith("a.png", "b.png");
        App.Player.ShowNext();
        App.Close();

        App.Start();

        Assert.Single(Shown());
    }

    [Fact]
    public void a_fake_climax_is_recorded_for_the_timeline()
    {
        StartWith();

        App.OnFakeClimaxTriggered();

        Assert.NotEmpty(App.Recorder.Timeline().FakeClimaxes);
    }

    // --- saving and replaying ---

    private static JsonObject SavedWithMedia(int count = 3) => new()
    {
        ["format"] = 1,
        ["duration_sec"] = 90.0,
        ["segments"] = new JsonArray(new JsonObject
        {
            ["kind"] = "beat", ["pattern"] = "Standard Beat", ["freq"] = 2.0, ["duration_sec"] = 90.0,
        }),
        ["custom_patterns"] = new JsonObject(),
        ["climax"] = new JsonObject { ["at_sec"] = 80.0, ["outcome"] = "real" },
        ["fake_climaxes"] = new JsonArray(),
        ["media"] = new JsonArray([.. Enumerable.Range(0, count).Select(i =>
            (JsonNode)new JsonObject { ["at_sec"] = i * 30.0, ["path"] = Files($"rec{i}.png")[0] })]),
    };

    [Fact]
    public void saving_the_session_puts_it_on_the_shelf()
    {
        StartWith();
        RunFor(3.0);
        App.Close();

        Assert.True(App.SaveCurrentSession());

        var stored = SessionFiles.LoadSavedSessions(_data);
        Assert.Single(stored);
        Assert.Equal("0.14.3", (string?)stored[0]!["app_version"]);
    }

    [Fact]
    public void a_saved_session_carries_the_custom_rhythms_it_played()
    {
        App.Beat.AddOrUpdateCustomPattern("My Rhythm", [1, 2, -1]);
        App.Beat.AddOrUpdateCustomPattern("Unused", [1]);
        App.Beat.SelectedBeatPatterns = ["My Rhythm"];
        StartWith();
        App.Close();

        App.SaveCurrentSession();

        var stored = SessionFiles.LoadSavedSessions(_data);
        Assert.Equal(["My Rhythm"], stored[0]!["custom_patterns"]!.AsObject().Select(kv => kv.Key));
    }

    [Fact]
    public void replaying_a_session_plays_the_recorded_files_in_the_recorded_order()
    {
        var saved = SavedWithMedia();

        Assert.True(App.ReplaySession(saved));

        Assert.Equal(SessionFiles.RecordedPaths(saved), App.Player.Playlist);
        Assert.Equal(0, App.Player.CurrentIndex);
        Assert.True(App.IsRunning);
    }

    [Fact]
    public void replaying_a_session_replays_its_segments_and_its_climax()
    {
        App.ReplaySession(SavedWithMedia());

        Assert.Equal(90.0, App.Beat.CurrentSegment!.DurationSec, 9);
        Assert.Equal("real", App.Climax.Outcome);
    }

    [Fact]
    public void a_replay_reaches_the_climax_handler_with_the_recorded_times()
    {
        App.ReplaySession(SavedWithMedia());

        Assert.Equal(80.0, App.Climax.FinaleAt!.Value - App.Beat.SessionStartTime, 9);
    }

    [Fact]
    public void replaying_against_your_own_library_leaves_the_playlist_alone()
    {
        var mine = Files("mine1.png", "mine2.png");
        App.Player.SetPlaylist(mine);

        Assert.True(App.ReplaySession(SavedWithMedia(), ignorePaths: true));

        Assert.Equal(mine, App.Player.Playlist);
    }

    [Fact]
    public void replaying_against_your_own_library_needs_one_to_be_loaded()
    {
        Assert.False(App.ReplaySession(SavedWithMedia(), ignorePaths: true));
        Assert.False(App.IsRunning);
    }

    [Fact]
    public void a_saved_session_without_paths_cannot_replay_its_own_files()
    {
        Assert.False(App.ReplaySession(SessionFiles.StripPaths(SavedWithMedia())));
        Assert.False(App.IsRunning);
    }

    [Fact]
    public void a_replay_ends_the_session_that_is_already_running()
    {
        StartWith();
        var ended = 0;
        App.SessionEnded += () => ended++;

        App.ReplaySession(SavedWithMedia());

        Assert.Equal(1, ended);
        Assert.True(App.IsRunning);
    }

    [Fact]
    public void a_replay_starts_its_recording_fresh()
    {
        StartWith("a.png", "b.png");
        App.Player.ShowNext();

        App.ReplaySession(SavedWithMedia());

        Assert.Single(Shown());
    }

    [Fact]
    public void replaying_against_your_own_library_starts_it_at_the_beginning()
    {
        App.Player.SetPlaylist(Files("mine1.png", "mine2.png"));
        App.Player.CurrentIndex = 1;

        Assert.True(App.ReplaySession(SavedWithMedia(), ignorePaths: true));

        Assert.Equal(0, App.Player.CurrentIndex);
    }

    [Fact]
    public void replaying_a_session_says_so_in_the_stats()
    {
        App.ReplaySession(SavedWithMedia());

        Assert.True(App.Score.WasReplay);
    }

    // --- achievements ---

    // Judged after the session is already in the history, so a rule that counts sessions can count
    // this one: four played before, and this fifth one earns "Back For More".
    [Fact]
    public void finishing_a_session_judges_it_for_achievements()
    {
        _data.Save("session_history", new JsonArray([.. Enumerable.Range(0, 4).Select(_ => (JsonNode)new JsonObject { ["total_num_beat"] = 1 })]));
        StartWith();

        App.Close();

        Assert.Contains(App.NewAchievements!, a => a.Id.StartsWith("returner", StringComparison.Ordinal));
    }

    // --- HUD wiring ---

    [Fact]
    public void starting_session_shows_session_timer_at_zero()
    {
        StartWith();

        Assert.Equal("⏱ 00:00", App.Hud.SessionTimerText);
    }

    [Fact]
    public void stopping_session_hides_the_captions()
    {
        StartWith();

        StopSayingNothing();

        Assert.Null(App.Hud.SessionTimerText);
        Assert.Null(App.Hud.RecordChaseText);
    }

    [Fact]
    public void beat_event_wired_to_record_chase_update()
    {
        _data.Save("session_history", new JsonArray(new JsonObject { ["total_num_beat"] = 1 }));
        App.Beat.MinBeatFreq = App.Beat.MaxBeatFreq = 4.0;
        StartWith();

        RunFor(1.0);

        Assert.True(App.Score.BeatCount >= 1);
        Assert.StartsWith("\U0001f3c6 New Total Beats Record!", App.Hud.RecordChaseText);
    }

    // Whatever the talking chance: the session opens with a greeting, not with the first rhythm
    // passing for a change of tempo.
    [Fact]
    public void a_session_opens_with_a_greeting_while_callouts_are_on()
    {
        _settings.SetValue("CalloutHandler/active_callout", true);
        _settings.SetValue("CalloutHandler/talking_chance", 0.0);

        StartWith();
        RunFor(0.5);

        Assert.Equal("en session_started phrase", App.Hud.CalloutText);
    }

    [Fact]
    public void a_forced_line_reaches_the_hud()
    {
        StartWith();

        App.EdgeReached();

        Assert.Equal("en edge_reached phrase", App.Hud.CalloutText);
    }

    // --- settings and the diagnostic log ---

    private static readonly string[] DefaultKeys = ["ask_for_outcome", "diagnostic_log", "diagnostic_log_level", "show_startup_splash"];

    [Fact]
    public void defaults_dict_matches_init_defaults()
    {
        Assert.True(App.ShowStartupSplash);
        Assert.True(App.AskForOutcome);
        Assert.False(App.DiagnosticLog);
        Assert.Equal(AppLog.DefaultLevel, App.DiagnosticLogLevel);
        Assert.Equal(GoonerApp.Defaults.Keys.Order(), DefaultKeys);
    }

    [Fact]
    public void settings_keys_come_from_an_explicit_group_constant() => Assert.Equal("GoonerApp", GoonerApp.SettingsGroup);

    [Fact]
    public void show_startup_splash_respects_saved_setting()
    {
        _settings.SetValue("GoonerApp/show_startup_splash", false);

        Assert.False(App.ShowStartupSplash);
    }

    [Fact]
    public void show_startup_splash_defaults_to_true() => Assert.True(App.ShowStartupSplash);

    [Fact]
    public void show_record_chase_defaults_to_true() => Assert.True(App.Hud.ShowRecordChase);

    [Fact]
    public void show_session_timer_defaults_to_true() => Assert.True(App.Hud.ShowSessionTimer);

    [Fact]
    public void the_diagnostic_log_level_defaults_to_info() => Assert.Equal(AppLog.DefaultLevel, App.DiagnosticLogLevel);

    [Fact]
    public void a_saved_diagnostic_log_level_is_restored()
    {
        _settings.SetValue("GoonerApp/diagnostic_log_level", "ERROR");

        Assert.Equal("ERROR", App.DiagnosticLogLevel);
    }

    // --- what's new ---

    private GoonerApp WithChangelog(string version, params string[] versions) => new(_stage, _clock, new GoonerAppOptions
    {
        Settings = _settings,
        DataStore = _data,
        Version = version,
        Changelog = [.. versions.Select(v => new ChangelogEntry(v, $"{v} stuff"))],
    });

    [Fact]
    public void maybe_show_whats_new_shows_dialog_when_new_entries_exist()
    {
        _settings.SetValue("GoonerApp/last_seen_version", "0.1.0");
        var app = WithChangelog("0.2.0", "0.1.0", "0.2.0");

        var entries = app.WhatsNewOnStartup();

        Assert.Equal(["0.2.0"], entries.Select(e => e.Version));
        Assert.Equal("0.2.0", (string?)_settings.Value("GoonerApp/last_seen_version"));
    }

    [Fact]
    public void maybe_show_whats_new_skips_dialog_when_no_new_entries()
    {
        _settings.SetValue("GoonerApp/last_seen_version", "0.2.0");
        var app = WithChangelog("0.2.0", "0.1.0", "0.2.0");

        Assert.Empty(app.WhatsNewOnStartup());
        Assert.Equal("0.2.0", (string?)_settings.Value("GoonerApp/last_seen_version"));
    }

    [Fact]
    public void show_whats_new_dialog_shows_full_changelog() =>
        Assert.Same(Changelog.Entries, App.ChangelogEntries);

    // The Python app cannot start without its VERSION file; the engine reads one beside it and says
    // "unknown" when there is none. That is no reason to show every entry ever written, nor to
    // forget which one was seen last.
    [Fact]
    public void a_version_that_does_not_parse_shows_nothing_and_remembers_nothing()
    {
        _settings.SetValue("GoonerApp/last_seen_version", "0.1.0");
        var app = WithChangelog("unknown", "0.1.0", "0.2.0");

        Assert.Empty(app.WhatsNewOnStartup());
        Assert.Equal("0.1.0", (string?)_settings.Value("GoonerApp/last_seen_version"));
    }

    [Fact]
    public void diagnostic_log_is_off_by_default()
    {
        Assert.False(App.DiagnosticLog);
        Assert.Empty(AppLog.LogFilePaths(_data.BaseDir));
    }

    [Fact]
    public void enabling_the_diagnostic_log_starts_writing()
    {
        App.SetDiagnosticLog(true);

        AppLog.Info("Test", "now recording");

        Assert.Contains("now recording", File.ReadAllText(AppLog.LogFilePath(_data.BaseDir)));
    }

    [Fact]
    public void disabling_the_diagnostic_log_stops_writing()
    {
        App.SetDiagnosticLog(true);
        AppLog.Info("Test", "kept");
        App.SetDiagnosticLog(false);

        AppLog.Info("Test", "dropped");

        var contents = File.ReadAllText(AppLog.LogFilePath(_data.BaseDir));
        Assert.Contains("kept", contents);
        Assert.DoesNotContain("dropped", contents);
    }

    // The import runs before there is a log to write to - the switch for it is one of the settings
    // being imported - so what it did is written once the app has configured the log from them.
    [Fact]
    public void the_profile_import_is_logged_once_the_log_is_configured()
    {
        var registry = new FakeLegacySettings().Set("GoonerApp/diagnostic_log", true).Set("BeatHandler/min_beat_freq", 2.0);
        var report = Gooner.Core.Migration.ProfileMigration.Run(_data, registry, roamingDir: null);

        _app = new GoonerApp(_stage, _clock, new GoonerAppOptions
        {
            Settings = new JsonSettingsStore(_data),
            DataStore = _data,
            Migration = report,
        });

        Assert.True(_app.DiagnosticLog);
        var contents = File.ReadAllText(AppLog.LogFilePath(_data.BaseDir));
        Assert.Contains("Imported 2 setting(s) from the registry", contents);
        Assert.True(contents.IndexOf("starting", StringComparison.Ordinal) < contents.IndexOf("Imported", StringComparison.Ordinal));
    }

    [Fact]
    public void the_diagnostic_log_setting_is_persisted()
    {
        App.SetDiagnosticLog(true, "ERROR");

        Assert.True(_settings.GetBool("GoonerApp/diagnostic_log", false));
        Assert.Equal("ERROR", _settings.GetString("GoonerApp/diagnostic_log_level", ""));
    }

    [Fact]
    public void a_saved_diagnostic_log_setting_is_restored()
    {
        _settings.SetValue("GoonerApp/diagnostic_log", true);
        _settings.SetValue("GoonerApp/diagnostic_log_level", "ERROR");

        Assert.True(App.DiagnosticLog);
        Assert.Equal("ERROR", App.DiagnosticLogLevel);
        Assert.True(AppLog.IsFileLoggingActive);
    }
}
