using Gooner.Core.Characters;

namespace Gooner.Core.Tests.Characters;

// A figure standing with nothing to do, the way a person stands: breathing in shorter than out, her
// weight on one leg and then the other, wandering a little without ever repeating, her eyes resting
// and then darting. What these curves promise is what keeps her from looking like a machine.
public sealed class IdleMotionTests
{
    private const double Frame = 1.0 / 60;

    private static IEnumerable<double> Times(double seconds, double step = Frame)
    {
        for (var t = 0.0; t < seconds; t += step) yield return t;
    }

    // --- breathing ---

    [Fact]
    public void she_breathes_eleven_to_eighteen_times_a_minute()
    {
        var idle = new IdleMotion(1);
        var breaths = 0;
        var last = idle.Breath(0);
        var rising = false;
        foreach (var t in Times(60))
        {
            var now = idle.Breath(t);
            if (now > last + 1e-9 && !rising) breaths++;
            rising = now > last + 1e-9 || (rising && now >= last - 1e-9);
            last = now;
        }
        Assert.InRange(breaths, 11, 18);
    }

    [Fact]
    public void a_breath_goes_in_quicker_than_it_goes_out()
    {
        var idle = new IdleMotion(2);
        var samples = Times(30).Select(t => (t, b: idle.Breath(t))).ToList();
        var rises = samples.Zip(samples.Skip(1)).Count(p => p.Second.b > p.First.b + 1e-9);
        var falls = samples.Zip(samples.Skip(1)).Count(p => p.Second.b < p.First.b - 1e-9);
        Assert.True(rises < falls, $"{rises} frames in, {falls} out");
    }

    [Fact]
    public void the_breath_stays_between_out_and_in_and_never_jumps()
    {
        var idle = new IdleMotion(3);
        var last = idle.Breath(0);
        foreach (var t in Times(60))
        {
            var now = idle.Breath(t);
            Assert.InRange(now, 0, 1);
            Assert.True(Math.Abs(now - last) < 0.05, $"jump at {t:F2} s");
            last = now;
        }
    }

    // --- her weight ---

    [Fact]
    public void her_weight_rests_on_one_leg_and_then_the_other()
    {
        var idle = new IdleMotion(4);
        var sides = Times(60, 0.1).Select(t => Math.Sign(Math.Round(idle.Weight(t), 3))).Where(s => s != 0).ToList();
        var changes = sides.Zip(sides.Skip(1)).Count(p => p.First != p.Second);
        Assert.InRange(changes, 3, 12);
        Assert.All(Times(60).Select(idle.Weight), w => Assert.InRange(w, -1, 1));
    }

    [Fact]
    public void she_holds_her_weight_most_of_the_time_and_shifts_it_in_about_a_second()
    {
        var idle = new IdleMotion(5);
        var still = 0;
        var moving = 0;
        var run = 0;
        var longest = 0;
        var last = idle.Weight(0);
        foreach (var t in Times(60).Skip(1))
        {
            var now = idle.Weight(t);
            if (Math.Abs(now - last) < 1e-9)
            {
                still++;
                longest = Math.Max(longest, run);
                run = 0;
            }
            else
            {
                moving++;
                run++;
            }
            Assert.True(Math.Abs(now - last) < 0.05, $"jump at {t:F2} s");
            last = now;
        }
        Assert.True(still > 3 * moving, $"{still} frames still, {moving} shifting");
        Assert.InRange(longest * Frame, 0.8, 2.0);
    }

    // --- wandering ---

    [Fact]
    public void wandering_stays_within_bounds_and_moves_smoothly()
    {
        var idle = new IdleMotion(6);
        foreach (var channel in Enumerable.Range(0, 4))
        {
            var last = idle.Drift(0, channel);
            foreach (var t in Times(120))
            {
                var now = idle.Drift(t, channel);
                Assert.InRange(now, -1, 1);
                Assert.True(Math.Abs(now - last) < 0.02, $"channel {channel} jumps at {t:F2} s");
                last = now;
            }
        }
    }

    [Fact]
    public void each_part_of_her_wanders_on_its_own()
    {
        var idle = new IdleMotion(7);
        var a = Times(20, 0.5).Select(t => idle.Drift(t, 0)).ToList();
        var b = Times(20, 0.5).Select(t => idle.Drift(t, 1)).ToList();
        Assert.NotEqual(a, b);
    }

    // --- her eyes ---

    [Fact]
    public void her_eyes_rest_and_then_dart()
    {
        var idle = new IdleMotion(8);
        var holds = new List<double>();
        var held = 0.0;
        var darting = 0.0;
        var last = idle.Gaze(0);
        foreach (var t in Times(60).Skip(1))
        {
            var now = idle.Gaze(t);
            Assert.InRange(now.X, -1, 1);
            Assert.InRange(now.Y, -1, 1);
            if (now == last) held += Frame;
            else
            {
                if (held > 0) holds.Add(held);
                held = 0;
                darting += Frame;
            }
            last = now;
        }
        Assert.InRange(holds.Count, 15, 100);
        Assert.All(holds, h => Assert.InRange(h, 0.4, 3.2));
        // A dart is quick: all of them together take up a small part of the minute.
        Assert.True(darting < 0.15 * 60, $"{darting:F1} s of darting");
    }

    [Fact]
    public void now_and_then_she_glances_aside()
    {
        var idle = new IdleMotion(9);
        Assert.Contains(Times(120, 0.1).Select(t => idle.Gaze(t).X), x => Math.Abs(x) > 0.55);
    }

    // --- a seed ---

    [Fact]
    public void the_same_seed_moves_the_same_and_another_does_not()
    {
        var times = Times(30, 0.37).ToList();
        double[] Curve(int seed)
        {
            var idle = new IdleMotion(seed);
            return [.. times.SelectMany(t => new[] { idle.Breath(t), idle.Weight(t), idle.Drift(t, 0), idle.Gaze(t).X })];
        }
        Assert.Equal(Curve(11), Curve(11));
        Assert.NotEqual(Curve(11), Curve(12));
    }

    [Fact]
    public void time_can_be_asked_about_in_any_order()
    {
        var forwards = new IdleMotion(13);
        var backwards = new IdleMotion(13);
        var later = backwards.Weight(40);
        Assert.Equal(forwards.Weight(10), backwards.Weight(10));
        Assert.Equal(forwards.Weight(40), later);
    }
}
