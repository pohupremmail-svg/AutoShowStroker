using System.Text.Json.Nodes;
using Gooner.Core.Data;
using Gooner.Core.Media;
using Gooner.Core.Sessions;
using Gooner.Core.Settings;
using Gooner.Core.Tests.Support;
using Gooner.Core.Timing;

namespace Gooner.Core.Tests.Media;

// Port of the player tests in tests/test_gooner_app.py (src/PlaylistPlayer.py). What is on screen
// and what replaces it; the screen itself is the engine's, behind IMediaStage, so here it is a
// recording of what the player asked of it.
public sealed class PlaylistPlayerTests : IDisposable
{
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-player-");
    private readonly FakeTime _time = new() { Now = 1000.0 };
    private readonly ClockScheduler _scheduler;
    private readonly FakeStage _stage = new();
    private readonly List<string> _shown = [];

    public PlaylistPlayerTests() => _scheduler = new ClockScheduler(_time);

    public void Dispose() => _tmp.Delete(recursive: true);

    private PlaylistPlayer Player(ISettingsStore? settings = null)
    {
        var player = new PlaylistPlayer(_stage, settings, new ScriptedRandom(), _scheduler);
        player.MediaShown += _shown.Add;
        return player;
    }

    private static List<string> Files(params string[] names) => [.. names.Select(n => Path.Combine("C:", "media", n))];

    private void RunTo(double time)
    {
        _time.Now = time;
        _scheduler.Advance();
    }

    // --- moving through the playlist ---

    [Fact]
    public void show_next_wraps_around_playlist()
    {
        var player = Player();
        player.SetPlaylist(Files("0.png", "1.png", "2.png"));
        player.CurrentIndex = 2;

        player.ShowNext();

        Assert.Equal(0, player.CurrentIndex);
    }

    [Fact]
    public void show_prev_wraps_around_playlist()
    {
        var player = Player();
        player.SetPlaylist(Files("0.png", "1.png", "2.png"));

        player.ShowPrev();

        Assert.Equal(2, player.CurrentIndex);
    }

    [Fact]
    public void show_next_noop_on_empty_playlist()
    {
        var player = Player();

        player.ShowNext();

        Assert.Equal(0, player.CurrentIndex);
        Assert.Empty(_stage.Calls);
    }

    [Fact]
    public void load_current_index_noop_on_empty_playlist()
    {
        var player = Player();

        player.LoadCurrent();

        Assert.Empty(_stage.Calls);
    }

    [Fact]
    public void set_playlist_rewinds_to_its_start()
    {
        var player = Player();
        player.SetPlaylist(Files("0.png", "1.png"));
        player.ShowNext();

        player.SetPlaylist(Files("a.png", "b.png"));

        Assert.Equal(0, player.CurrentIndex);
    }

    // --- what the stage is asked to show ---

    [Fact]
    public void load_media_image_extension_shows_image_label()
    {
        var player = Player();

        player.LoadMedia(Files("pic.png")[0]);

        Assert.Equal(["image " + Files("pic.png")[0]], _stage.Calls);
    }

    [Fact]
    public void load_media_gif_extension_shows_image_label_and_sets_movie()
    {
        var player = Player();

        player.LoadMedia(Files("clip.gif")[0]);

        Assert.Equal(["gif " + Files("clip.gif")[0]], _stage.Calls);
    }

    [Fact]
    public void load_media_video_extension_switches_to_video_widget()
    {
        var player = Player();

        player.LoadMedia(Files("clip.mp4")[0]);

        Assert.Equal(["video " + Files("clip.mp4")[0] + " at 1"], _stage.Calls);
    }

    [Fact]
    public void an_unknown_kind_shows_nothing()
    {
        var player = Player();

        player.LoadMedia(Files("notes.txt")[0]);

        Assert.Empty(_stage.Calls);
    }

    [Fact]
    public void vid_loudness_is_restored_from_settings()
    {
        var settings = new JsonSettingsStore(new UserDataStore(_tmp.FullName));
        settings.SetValue("GoonerApp/vid_loudness", 0.25);

        Player(settings).LoadMedia(Files("clip.mp4")[0]);

        Assert.Equal(["video " + Files("clip.mp4")[0] + " at 0.25"], _stage.Calls);
    }

    [Fact]
    public void the_timings_are_read_from_settings()
    {
        var settings = new JsonSettingsStore(new UserDataStore(_tmp.FullName));
        settings.SetValue("GoonerApp/min_dur", "1.5");
        settings.SetValue("GoonerApp/max_dur", 2.5);
        settings.SetValue("GoonerApp/video_min_dur", 3);

        var player = Player(settings);

        Assert.Equal((1.5, 2.5, 3.0), (player.MinDur, player.MaxDur, player.VideoMinDur));
    }

    [Fact]
    public void show_message_puts_a_line_where_the_picture_goes()
    {
        Player().ShowMessage("No supported files found.");

        Assert.Equal(["message No supported files found."], _stage.Calls);
    }

    [Fact]
    public void set_muted_silences_the_clip()
    {
        Player().SetMuted(true);

        Assert.Equal(["muted True"], _stage.Calls);
    }

    // --- how long a picture stays up ---

    [Fact]
    public void a_picture_stays_up_for_a_drawn_while_and_then_the_next_one_comes()
    {
        var player = Player();
        player.MinDur = player.MaxDur = 2.0;
        player.SetPlaylist(Files("a.png", "b.png"));
        player.SessionStarted();
        player.LoadCurrent();

        RunTo(1001.9);
        Assert.Equal(0, player.CurrentIndex);
        RunTo(1002.0);
        Assert.Equal(1, player.CurrentIndex);
        Assert.Equal(2000, player.AutoPlayTimer.IntervalMs);
    }

    [Fact]
    public void the_gap_is_drawn_between_the_two_durations_in_whole_milliseconds()
    {
        var player = new PlaylistPlayer(_stage, null, new ScriptedRandom { Uniform = () => 0.5 }, _scheduler);
        player.MinDur = 1.0;
        player.MaxDur = 2.0;

        player.RecalcAutoplayTimer();

        Assert.Equal(1500, player.AutoPlayTimer.IntervalMs);
    }

    // --- video ---

    [Fact]
    public void a_video_starting_a_session_is_not_cut_short_by_the_autoplay_timer()
    {
        var player = Player();
        player.SetPlaylist(Files("clip.mp4"));
        player.SessionStarted();

        player.LoadCurrent();

        Assert.False(player.AutoPlayTimer.Active);
    }

    [Fact]
    public void video_status_changed_replays_if_below_min_duration()
    {
        var player = Player();
        player.SessionStarted();
        player.VideoMinDur = 5.0;
        player.LoadMedia(Files("clip.mp4")[0]);
        _time.Now += 2.0;

        player.VideoEnded();

        Assert.Equal("replay", _stage.Calls[^1]);
    }

    [Fact]
    public void video_status_changed_advances_if_above_min_duration()
    {
        var player = Player();
        player.SetPlaylist(Files("clip.mp4", "b.png"));
        player.SessionStarted();
        player.VideoMinDur = 1.0;
        player.LoadCurrent();
        _time.Now += 5.0;

        player.VideoEnded();

        Assert.Equal(1, player.CurrentIndex);
    }

    [Fact]
    public void end_of_media_is_ignored_once_the_session_stopped()
    {
        var player = Player();
        player.SetPlaylist(Files("clip.mp4", "b.png"));
        player.SessionStarted();
        player.LoadCurrent();
        player.SessionEnded();
        _time.Now += 60.0;

        player.VideoEnded();

        Assert.Equal(0, player.CurrentIndex);
    }

    [Fact]
    public void an_unplayable_video_advances_instead_of_stalling()
    {
        var player = Player();
        player.SetPlaylist(Files("a.mp4", "b.png"));
        player.SessionStarted();
        player.LoadCurrent();
        Assert.False(player.AutoPlayTimer.Active);

        player.VideoFailed("the backend can't decode it");

        Assert.True(player.AutoPlayTimer.Active);
        Assert.Equal(PlaylistPlayer.MediaErrorAdvanceMs, player.AutoPlayTimer.IntervalMs);
        RunTo(1001.0);
        Assert.Equal(1, player.CurrentIndex);
    }

    [Fact]
    public void a_media_error_outside_a_session_is_ignored()
    {
        var player = Player();

        player.VideoFailed("boom");

        Assert.False(player.AutoPlayTimer.Active);
    }

    // --- the session ---

    [Fact]
    public void session_ended_stops_everything()
    {
        var player = Player();
        player.SetPlaylist(Files("a.png"));
        player.SessionStarted();
        player.LoadCurrent();

        player.SessionEnded();

        Assert.False(player.AutoPlayTimer.Active);
        Assert.Equal("stop", _stage.Calls[^1]);
    }

    [Fact]
    public void showing_a_medium_is_recorded()
    {
        var player = Player();
        player.SetPlaylist(Files("a.png"));

        player.LoadCurrent();

        Assert.Equal(Files("a.png"), _shown);
    }

    // --- replaying a saved session ---

    private static SessionScript ReplayScript(JsonArray media, bool ignorePaths = false) => new(new JsonObject
    {
        ["duration_sec"] = 100.0,
        ["segments"] = new JsonArray(new JsonObject
        {
            ["kind"] = "beat", ["pattern"] = "Standard Beat", ["freq"] = 2.0, ["duration_sec"] = 100.0,
        }),
        ["custom_patterns"] = new JsonObject(),
        ["climax"] = null,
        ["fake_climaxes"] = new JsonArray(),
        ["media"] = media,
    }, ignorePaths);

    private static JsonObject At(double seconds, string? path = null)
    {
        var entry = new JsonObject { ["at_sec"] = seconds };
        if (path is not null) entry["path"] = path;
        return entry;
    }

    [Fact]
    public void a_replay_shows_the_recorded_media_in_order()
    {
        var (one, two) = (Files("one.png")[0], Files("two.png")[0]);
        var player = Player();
        // What replay_session does: the recorded paths are the playlist.
        player.SetPlaylist([one, two]);
        player.Script = ReplayScript([At(0.0, one), At(4.0, two)]);
        player.SessionStarted();

        player.LoadCurrent();
        RunTo(1004.0);

        Assert.Equal([one, two], _shown);
    }

    [Fact]
    public void a_replay_uses_the_recorded_media_gaps()
    {
        var player = Player();
        player.SetPlaylist(Files("a.png"));
        player.Script = ReplayScript([At(0.0, Files("a.png")[0]), At(7.0, Files("a.png")[0])]);

        player.LoadCurrent();

        Assert.Equal(7000, player.AutoPlayTimer.IntervalMs);
    }

    [Fact]
    public void a_replay_falls_back_to_the_settings_once_the_script_runs_out()
    {
        var player = Player();
        player.SetPlaylist(Files("a.png"));
        player.MinDur = player.MaxDur = 2.0;
        player.Script = ReplayScript([At(0.0, Files("a.png")[0])]);
        player.LoadCurrent();

        player.RecalcAutoplayTimer();  // the single recorded gap is already spent

        Assert.Equal(2000, player.AutoPlayTimer.IntervalMs);
    }

    [Fact]
    public void ignoring_the_paths_uses_the_loaded_playlist()
    {
        var mine = Files("mine.png")[0];
        var player = Player();
        player.SetPlaylist([mine]);
        player.Script = ReplayScript([At(0.0, @"C:\someone\else.png")], ignorePaths: true);

        player.LoadCurrent();

        Assert.Equal([mine], _shown);
    }

    [Fact]
    public void a_replay_without_paths_keeps_the_recorded_gaps()
    {
        var player = Player();
        player.SetPlaylist(Files("a.png"));
        player.MinDur = player.MaxDur = 99.0;
        player.Script = ReplayScript([At(0.0), At(6.0)], ignorePaths: true);

        player.LoadCurrent();

        Assert.Equal(6000, player.AutoPlayTimer.IntervalMs);
    }

    [Fact]
    public void a_replayed_video_does_not_escape_its_recorded_gap()
    {
        var clip = Files("clip.mp4")[0];
        var player = Player();
        player.Script = ReplayScript([At(0.0, clip), At(5.0, clip)]);
        player.SessionStarted();

        player.LoadCurrent();

        Assert.True(player.AutoPlayTimer.Active);
        Assert.Equal(5000, player.AutoPlayTimer.IntervalMs);
    }

    [Fact]
    public void a_normal_session_afterwards_is_not_scripted()
    {
        var player = Player();
        player.SetPlaylist(Files("a.png"));
        player.MinDur = player.MaxDur = 3.0;
        player.Script = ReplayScript([At(0.0, Files("a.png")[0]), At(9.0, Files("a.png")[0])]);
        player.LoadCurrent();
        player.SessionEnded();

        player.Script = null;
        player.SessionStarted();
        player.LoadCurrent();

        Assert.Equal(3000, player.AutoPlayTimer.IntervalMs);
    }

    // --- the next medium, got ready while this one is up ---

    [Fact]
    public void the_next_medium_is_got_ready_while_this_one_is_up()
    {
        var files = Files("a.png", "b.gif", "c.mp4");
        var player = Player();
        player.SetPlaylist(files);

        player.LoadCurrent();
        Assert.Equal([files[1]], _stage.Prepared);

        player.ShowNext();
        Assert.Equal([files[1], files[2]], _stage.Prepared);
    }

    [Fact]
    public void after_the_last_one_the_first_is_got_ready()
    {
        var files = Files("a.png", "b.png", "c.png");
        var player = Player();
        player.SetPlaylist(files);
        player.CurrentIndex = 2;

        player.LoadCurrent();

        Assert.Equal([files[0]], _stage.Prepared);
    }

    [Fact]
    public void a_replay_gets_the_recorded_next_one_ready_without_using_it_up()
    {
        var (one, two) = (Files("one.png")[0], Files("two.png")[0]);
        var player = Player();
        player.SetPlaylist(Files("mine.png"));
        player.Script = ReplayScript([At(0.0, one), At(4.0, two)]);

        player.LoadCurrent();
        Assert.Equal([two], _stage.Prepared);

        player.ShowNext();
        Assert.Equal([one, two], _shown);
    }

    [Fact]
    public void once_the_recording_runs_out_the_playlist_is_got_ready_again()
    {
        var mine = Files("mine.png", "yours.png");
        var player = Player();
        player.SetPlaylist(mine);
        player.Script = ReplayScript([At(0.0, Files("one.png")[0])]);

        player.LoadCurrent();

        Assert.Equal([mine[1]], _stage.Prepared);
    }

    [Fact]
    public void nothing_is_got_ready_from_an_empty_playlist()
    {
        var player = Player();

        player.ShowNext();
        player.LoadCurrent();

        Assert.Empty(_stage.Prepared);
    }

    private sealed class FakeStage : IMediaStage
    {
        public List<string> Calls { get; } = [];

        public List<string> Prepared { get; } = [];

        public void Prepare(string path) => Prepared.Add(path);

        public void ShowImage(string path) => Calls.Add("image " + path);

        public void ShowGif(string path) => Calls.Add("gif " + path);

        public void PlayVideo(string path, double volume) =>
            Calls.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"video {path} at {volume}"));

        public void ReplayVideo() => Calls.Add("replay");

        public void ShowMessage(string text) => Calls.Add("message " + text);

        public void StopAll() => Calls.Add("stop");

        public void SetMuted(bool muted) => Calls.Add("muted " + muted);
    }
}
