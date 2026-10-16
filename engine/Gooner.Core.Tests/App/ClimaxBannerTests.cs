using Gooner.Core.App;
using Gooner.Core.Tests.Support;
using Gooner.Core.Timing;

namespace Gooner.Core.Tests.App;

// Port of the climax status banner tests in tests/test_gooner_app.py: the outcome in large
// letters over the note track, blinking between two shades of its colour.
public sealed class ClimaxBannerTests
{
    private readonly FakeTime _time = new() { Now = 1000.0 };
    private readonly ClockScheduler _scheduler;
    private readonly ClimaxBanner _banner;

    public ClimaxBannerTests()
    {
        _scheduler = new ClockScheduler(_time);
        _banner = new ClimaxBanner(_scheduler);
    }

    [Fact]
    public void climax_status_label_hidden_by_default()
    {
        Assert.Null(_banner.Text);
        Assert.False(_banner.BlinkTimer.Active);
    }

    [Theory]
    [InlineData("cum", "CUM")]
    [InlineData("ruined", "RUINED")]
    [InlineData("denied", "DENIED")]
    public void update_climax_status_label_shows_the_outcome(string status, string text)
    {
        _banner.Update(status);

        Assert.Equal(text, _banner.Text);
        Assert.True(_banner.BlinkTimer.Active);
        Assert.Equal(ClimaxBanner.StatusColors[status].On, _banner.Color);
    }

    [Fact]
    public void update_climax_status_label_neutral_hides_and_stops_blink()
    {
        _banner.Update("cum");

        _banner.Update("neutral");

        Assert.Null(_banner.Text);
        Assert.False(_banner.BlinkTimer.Active);
    }

    [Fact]
    public void toggle_climax_blink_alternates_colors_keeps_text()
    {
        _banner.Update("cum");
        var (on, off) = ClimaxBanner.StatusColors["cum"];

        _banner.ToggleBlink();
        Assert.Equal(off, _banner.Color);
        Assert.Equal("CUM", _banner.Text);

        _banner.ToggleBlink();
        Assert.Equal(on, _banner.Color);
        Assert.Equal("CUM", _banner.Text);
    }

    [Fact]
    public void the_blink_runs_on_its_timer()
    {
        _banner.Update("ruined");

        _time.Now += ClimaxBanner.BlinkIntervalMs / 1000.0;
        _scheduler.Advance();

        Assert.Equal(ClimaxBanner.StatusColors["ruined"].Off, _banner.Color);
    }

    [Fact]
    public void climax_blink_interval_is_fast() => Assert.True(ClimaxBanner.BlinkIntervalMs <= 150);

    // Stop: the result stays readable rather than disappearing or flashing forever.
    [Fact]
    public void freezing_keeps_the_outcome_up_in_its_bright_colour()
    {
        _banner.Update("cum");
        _banner.ToggleBlink();

        _banner.Freeze();

        Assert.Equal("CUM", _banner.Text);
        Assert.False(_banner.BlinkTimer.Active);
        Assert.Equal(ClimaxBanner.StatusColors["cum"].On, _banner.Color);
    }

    [Fact]
    public void freezing_with_nothing_announced_leaves_it_hidden()
    {
        _banner.Freeze();

        Assert.Null(_banner.Text);
    }

    [Fact]
    public void the_colours_are_the_themes() =>
        Assert.Equal(("#FF00BF", "#FF33CC"), ClimaxBanner.StatusColors["cum"]);
}
