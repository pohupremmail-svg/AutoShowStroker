using System.Globalization;
using Gooner.Core.App;
using Gooner.Core.Timing;
using Gooner.Core.Tests.Support;

namespace Gooner.Core.Tests.App;

// The colour schemes the look offers. Neon is the palette of src/theme.py, unchanged; Boudoir is the
// engine's own. Whichever is in use, everything that asks the palette gets its colours.
public sealed class PaletteTests : IDisposable
{
    public void Dispose() => Palette.Use("neon");

    private static (int R, int G, int B) Rgb(string hex) =>
        (int.Parse(hex[1..3], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
         int.Parse(hex[3..5], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
         int.Parse(hex[5..7], NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private static double Distance(string a, string b)
    {
        var (r1, g1, b1) = Rgb(a);
        var (r2, g2, b2) = Rgb(b);
        return Math.Sqrt(Math.Pow(r1 - r2, 2) + Math.Pow(g1 - g2, 2) + Math.Pow(b1 - b2, 2));
    }

    [Fact]
    public void every_colour_scheme_the_look_offers_has_a_palette() =>
        Assert.Equal(Look.Choices["color_scheme"].Select(o => o.Value).Order(), Palette.Schemes.Keys.Order());

    [Fact]
    public void neon_is_the_python_apps_palette()
    {
        var neon = Palette.Schemes["neon"];

        Assert.Equal(("#2D1D3A", "#FF00BF", "#FF33CC", "#FFC2ED"), (neon.Background, neon.Accent, neon.AccentHover, neon.Text));
        Assert.Equal(("#5A3D73", "#3A2650", "#241730", "#8C3D73"), (neon.Secondary, neon.Surface, neon.SurfaceDark, neon.Pause));
        Assert.Equal(("#FFA733", "#FF3B3B"), (neon.Ruined, neon.Denied));
    }

    [Fact]
    public void using_a_scheme_hands_out_its_colours()
    {
        Palette.Use("boudoir");

        Assert.Equal(Palette.Schemes["boudoir"].Accent, Palette.Accent);
        Assert.Equal(Palette.Schemes["boudoir"].Background, Palette.Background);
        Assert.Equal("boudoir", Palette.Current.Name);
    }

    [Fact]
    public void a_scheme_it_does_not_know_is_neon()
    {
        Palette.Use("boudoir");
        Palette.Use("chartreuse");

        Assert.Equal("neon", Palette.Current.Name);
    }

    // The banner is colour-coded: in no scheme may cum, ruined and denied look alike - Boudoir's
    // accent is red already, so its denial cannot be.
    [Theory]
    [InlineData("neon")]
    [InlineData("boudoir")]
    public void the_three_outcomes_never_share_a_colour(string scheme)
    {
        var s = Palette.Schemes[scheme];

        Assert.True(Distance(s.Accent, s.Ruined) > 100, "cum and ruined");
        Assert.True(Distance(s.Accent, s.Denied) > 100, "cum and denied");
        Assert.True(Distance(s.Ruined, s.Denied) > 100, "ruined and denied");
    }

    [Fact]
    public void the_banner_shows_the_scheme_in_use()
    {
        Palette.Use("boudoir");
        var banner = new ClimaxBanner(new ClockScheduler(new FakeTime()));

        banner.Update("denied");

        Assert.Equal(Palette.Schemes["boudoir"].Denied, banner.Color);
    }
}
