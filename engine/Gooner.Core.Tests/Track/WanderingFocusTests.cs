using Gooner.Core.Randomness;
using Gooner.Core.Track;

namespace Gooner.Core.Tests.Track;

// Where the Hypno Ring sits on the picture: in the middle, or - when it wanders - drifting across the
// picture all the time, slowly and in smooth curves, never jumping and never stopping, and never so
// close to an edge that its spiral leaves the picture.
public class WanderingFocusTests
{
    private static List<(double X, double Y)> Run(WanderingFocus focus, double from, double seconds, bool wander)
    {
        var seen = new List<(double X, double Y)>();
        for (var t = from; t < from + seconds; t += 1 / 60.0)
        {
            focus.Step(1 / 60.0, t, wander);
            seen.Add((focus.X, focus.Y));
        }
        return seen;
    }

    [Fact]
    public void it_starts_in_the_middle()
    {
        var focus = new WanderingFocus(new SystemRandomSource(new Random(1)));

        Assert.Equal((WanderingFocus.Home.X, WanderingFocus.Home.Y), (focus.X, focus.Y));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void while_it_wanders_it_stays_where_the_spiral_fits(int seed)
    {
        var seen = Run(new WanderingFocus(new SystemRandomSource(new Random(seed))), 0, 600, wander: true);

        Assert.All(seen, p => Assert.InRange(p.X, WanderingFocus.MinX - 0.02, WanderingFocus.MaxX + 0.02));
        Assert.All(seen, p => Assert.InRange(p.Y, WanderingFocus.MinY - 0.02, WanderingFocus.MaxY + 0.02));
        // And it gets around: most of the room it has, both ways.
        Assert.True(seen.Max(p => p.X) - seen.Min(p => p.X) > (WanderingFocus.MaxX - WanderingFocus.MinX) * 0.6);
        Assert.True(seen.Max(p => p.Y) - seen.Min(p => p.Y) > (WanderingFocus.MaxY - WanderingFocus.MinY) * 0.6);
    }

    // Slow and smooth: never faster than a fifth of the picture a second.
    [Fact]
    public void it_glides_rather_than_jumps()
    {
        var focus = new WanderingFocus(new SystemRandomSource(new Random(3)));
        var seen = Run(focus, 0, 300, wander: true);

        Assert.All(seen.Zip(seen.Skip(1)), p =>
            Assert.True(Math.Abs(p.Second.X - p.First.X) * 60 < 0.2 && Math.Abs(p.Second.Y - p.First.Y) * 60 < 0.2));
    }

    // Always on its way: there is no stretch of a couple of seconds where it stands still and then
    // picks somewhere new.
    [Fact]
    public void it_never_stands_still_while_it_wanders()
    {
        var focus = new WanderingFocus(new SystemRandomSource(new Random(5)));
        Run(focus, 0, 10, wander: true);
        var seen = Run(focus, 10, 300, wander: true);

        for (var i = 0; i + 120 < seen.Count; i += 60)
        {
            var travelled = 0.0;
            for (var k = i; k < i + 120; k++)
                travelled += Math.Sqrt(Math.Pow(seen[k + 1].X - seen[k].X, 2) + Math.Pow(seen[k + 1].Y - seen[k].Y, 2));
            Assert.True(travelled > 0.01, $"stood still around {10 + i / 60.0:0} s");
        }
    }

    [Fact]
    public void when_it_stops_wandering_it_goes_home()
    {
        var focus = new WanderingFocus(new SystemRandomSource(new Random(9)));
        Run(focus, 0, 30, wander: true);

        Run(focus, 30, 10, wander: false);

        Assert.Equal(WanderingFocus.Home.X, focus.X, 2);
        Assert.Equal(WanderingFocus.Home.Y, focus.Y, 2);
    }
}
