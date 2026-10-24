using Gooner.Core.App;
using Gooner.Core.Data;
using Gooner.Core.Settings;
using Gooner.Core.Tests.Support;
using Gooner.Core.Timing;

namespace Gooner.Core.Tests.App;

// Port of tests/test_session_hud_widget.py: the captions over the media - the callout being said,
// the record chase, the clock - as text the engine draws; null is a caption that is not shown.
// Tested against a score double, as the Python tests are, because the HUD decides for itself
// whether a session is running.
public sealed class SessionHudTests : IDisposable
{
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-hud-");
    private readonly FakeTime _time = new() { Now = 1000.0 };
    private readonly ClockScheduler _scheduler;

    public SessionHudTests() => _scheduler = new ClockScheduler(_time);

    public void Dispose() => _tmp.Delete(recursive: true);

    private SessionHud Hud(ScoreDouble? score = null, ISettingsStore? settings = null) =>
        new(score ?? new ScoreDouble(), settings, _scheduler);

    [Fact]
    public void captions_start_hidden()
    {
        var hud = Hud();

        Assert.Null(hud.RecordChaseText);
        Assert.Null(hud.SessionTimerText);
        Assert.Null(hud.CalloutText);
    }

    [Fact]
    public void nothing_is_shown_outside_a_session()
    {
        var hud = Hud(new ScoreDouble(chase: ("total_dur_sec", 50.0, 100.0), elapsed: 42.0));

        hud.Refresh();

        Assert.Null(hud.RecordChaseText);
        Assert.Null(hud.SessionTimerText);
    }

    [Fact]
    public void a_started_session_shows_the_clock()
    {
        var hud = Hud(new ScoreDouble(elapsed: 65.0));

        hud.SessionStarted();

        Assert.Equal("⏱ 01:05", hud.SessionTimerText);
    }

    [Fact]
    public void the_clock_ticks_once_a_second()
    {
        var score = new ScoreDouble(elapsed: 0.0);
        var hud = Hud(score);
        hud.SessionStarted();

        score.Elapsed = 1.0;
        _time.Now = 1001.0;
        _scheduler.Advance();

        Assert.Equal("⏱ 00:01", hud.SessionTimerText);
        Assert.Equal(SessionHud.TickMs, hud.ClockTimer.IntervalMs);
    }

    [Fact]
    public void the_chase_names_the_record_being_closed_in_on()
    {
        var hud = Hud(new ScoreDouble(chase: ("total_dur_sec", 50.0, 100.0)));

        hud.SessionStarted();

        Assert.Equal("\U0001f3c6 Closing in on your Total Duration record: 50s / 1 Min 40s", hud.RecordChaseText);
    }

    [Fact]
    public void beating_the_record_says_so()
    {
        var hud = Hud(new ScoreDouble(chase: ("total_dur_sec", 150.0, 100.0)));

        hud.SessionStarted();

        Assert.Equal("\U0001f3c6 New Total Duration Record! 2 Min 30s", hud.RecordChaseText);
    }

    [Fact]
    public void no_chase_worth_showing_hides_the_caption()
    {
        var hud = Hud(new ScoreDouble(chase: null));

        hud.SessionStarted();

        Assert.Null(hud.RecordChaseText);
    }

    [Fact]
    public void the_bests_are_the_ones_the_session_started_with()
    {
        var score = new ScoreDouble(chase: ("total_dur_sec", 50.0, 100.0));
        var hud = Hud(score);

        hud.SessionStarted();
        hud.RefreshRecordChase();
        hud.RefreshRecordChase();

        Assert.Equal(1, score.BestsAsked);
    }

    [Fact]
    public void switching_the_chase_off_hides_it_on_the_next_refresh()
    {
        var hud = Hud(new ScoreDouble(chase: ("total_dur_sec", 50.0, 100.0)));
        hud.SessionStarted();
        Assert.NotNull(hud.RecordChaseText);

        hud.ShowRecordChase = false;
        hud.Refresh();

        Assert.Null(hud.RecordChaseText);
        Assert.NotNull(hud.SessionTimerText);
    }

    [Fact]
    public void switching_the_clock_off_hides_it_on_the_next_refresh()
    {
        var hud = Hud(new ScoreDouble(chase: ("total_dur_sec", 50.0, 100.0)));
        hud.SessionStarted();

        hud.ShowSessionTimer = false;
        hud.Refresh();

        Assert.Null(hud.SessionTimerText);
        Assert.NotNull(hud.RecordChaseText);
    }

    [Fact]
    public void ending_a_session_clears_both_captions_and_stops_the_clock()
    {
        var hud = Hud(new ScoreDouble(chase: ("total_dur_sec", 50.0, 100.0)));
        hud.SessionStarted();
        Assert.True(hud.ClockTimer.Active);

        hud.SessionEnded();

        Assert.Null(hud.RecordChaseText);
        Assert.Null(hud.SessionTimerText);
        Assert.False(hud.ClockTimer.Active);
    }

    [Fact]
    public void a_tease_is_shown_and_then_cleared()
    {
        var hud = Hud();

        hud.ShowTease("good boy");
        Assert.Equal("good boy", hud.CalloutText);

        hud.HideTease();
        Assert.Null(hud.CalloutText);
    }

    [Fact]
    public void both_switches_default_to_on() =>
        Assert.True(Hud() is { ShowRecordChase: true, ShowSessionTimer: true });

    [Fact]
    public void the_switches_are_read_from_settings()
    {
        var settings = new JsonSettingsStore(new UserDataStore(_tmp.FullName));
        settings.SetValue("GoonerApp/show_record_chase", "false");
        settings.SetValue("GoonerApp/show_session_timer", false);

        Assert.True(Hud(settings: settings) is { ShowRecordChase: false, ShowSessionTimer: false });
    }

    // The Python double: the bests, the chase and the clock, as ScoreTracker would answer them.
    private sealed class ScoreDouble(
        (string Metric, double Current, double Best)? chase = null, double elapsed = 0.0) : IRecordChaseSource
    {
        public double Elapsed { get; set; } = elapsed;

        public int BestsAsked { get; private set; }

        public Dictionary<string, double> GetAllTimeBests()
        {
            BestsAsked++;
            return new() { ["total_dur_sec"] = 100.0 };
        }

        public (string Metric, double Current, double Best)? RecordChaseStatus(IReadOnlyDictionary<string, double> bestValues) => chase;

        public IReadOnlyDictionary<string, double> LiveMetrics() => new Dictionary<string, double> { ["total_dur_sec"] = Elapsed };
    }
}
