using Gooner.Core.Randomness;
using Gooner.Core.Track;

namespace Gooner.Core.Tests.Track;

// Where the Hypno Ring sits on the picture: in the middle, or - when it wanders - drifting from one
// spot to the next every few seconds and at every change of rhythm, gliding rather than jumping, and
// never so close to an edge that its spiral leaves the picture.
public class WanderingFocusTests
{
    private static void Run(WanderingFocus focus, double from, double seconds, bool wander)
    {
        for (var t = from; t < from + seconds; t += 1 / 60.0)
            focus.Step(1 / 60.0, t, wander);
    }

    [Fact]
    public void it_starts_in_the_middle()
    {
        var focus = new WanderingFocus(new SystemRandomSource(new Random(1)));

        Assert.Equal((WanderingFocus.Home.X, WanderingFocus.Home.Y), (focus.X, focus.Y));
    }

    [Fact]
    public void while_it_wanders_it_stays_where_the_spiral_fits()
    {
        var focus = new WanderingFocus(new SystemRandomSource(new Random(7)));
        var seen = new List<(double X, double Y)>();

        for (var t = 0.0; t < 600; t += 1 / 60.0)
        {
            focus.Step(1 / 60.0, t, wander: true);
            seen.Add((focus.X, focus.Y));
        }

        Assert.All(seen, p => Assert.InRange(p.X, WanderingFocus.MinX - 0.02, WanderingFocus.MaxX + 0.02));
        Assert.All(seen, p => Assert.InRange(p.Y, WanderingFocus.MinY - 0.02, WanderingFocus.MaxY + 0.02));
        // It went somewhere, and more than once.
        Assert.True(seen.Select(p => (Math.Round(p.X, 1), Math.Round(p.Y, 1))).Distinct().Count() > 5);
    }

    [Fact]
    public void it_glides_rather_than_jumps()
    {
        var focus = new WanderingFocus(new SystemRandomSource(new Random(3)));
        var (x, y) = (focus.X, focus.Y);

        for (var t = 0.0; t < 120; t += 1 / 60.0)
        {
            focus.Step(1 / 60.0, t, wander: true);
            Assert.True(Math.Abs(focus.X - x) < 0.02 && Math.Abs(focus.Y - y) < 0.02, $"jumped at {t:0.00} s");
            (x, y) = (focus.X, focus.Y);
        }
    }

    [Fact]
    public void a_change_of_rhythm_sends_it_somewhere_new()
    {
        var focus = new WanderingFocus(new SystemRandomSource(new Random(5)));
        Run(focus, 0, 1, wander: true);
        var before = (focus.TargetX, focus.TargetY);

        focus.MoveOn(1.0);

        Assert.NotEqual(before, (focus.TargetX, focus.TargetY));
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
