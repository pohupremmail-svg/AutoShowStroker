using Gooner.Core.Characters;

namespace Gooner.Core.Tests.Characters;

// Her face while nothing in particular is said: never blank. A smile that never quite goes, small
// expressions flickering across it every few seconds, and - while she strokes in the frame - heavy
// lids, parted lips that breathe, a wink now and then. The user found her face "tot", "lustlos".
public sealed class FaceMotionTests
{
    private const double Frame = 1.0 / 60;
    private static readonly string[] Presets = ["happy", "angry", "sad", "relaxed", "surprised", "blink", "blinkLeft", "blinkRight", "aa", "ih", "ou", "ee", "oh"];

    private static IEnumerable<double> Times(double seconds, double step = Frame)
    {
        for (var t = 0.0; t < seconds; t += step) yield return t;
    }

    [Fact]
    public void every_weight_is_a_preset_between_none_and_full()
    {
        var face = new FaceMotion(1);
        foreach (var t in Times(60, 0.1))
            foreach (var (name, weight) in face.At(t, breath: 0.5, aroused: 1))
            {
                Assert.Contains(name, Presets);
                Assert.InRange(weight, 0, 1);
            }
    }

    [Fact]
    public void a_smile_never_quite_leaves_her()
    {
        var face = new FaceMotion(2);
        Assert.All(Times(60, 0.1), t => Assert.True(face.At(t, 0, 0).GetValueOrDefault("happy") > 0.03));
    }

    [Fact]
    public void something_crosses_her_face_every_few_seconds()
    {
        var face = new FaceMotion(3);
        Assert.InRange(face.Flickers(60).Count, 8, 25);
        Assert.All(face.Flickers(60).Zip(face.Flickers(60).Skip(1)), p => Assert.InRange(p.Second - p.First, 2, 9));
    }

    [Fact]
    public void her_face_moves_smoothly_but_for_a_wink()
    {
        var face = new FaceMotion(4);
        var last = face.At(0, 0, 1);
        foreach (var t in Times(60).Skip(1))
        {
            var now = face.At(t, 0.5 + 0.5 * Math.Sin(t), 1);
            foreach (var name in now.Keys.Union(last.Keys).Where(n => n is not ("blinkLeft" or "blinkRight")))
                Assert.True(Math.Abs(now.GetValueOrDefault(name) - last.GetValueOrDefault(name)) < 0.12, $"{name} jumps at {t:F2} s");
            last = now;
        }
    }

    [Fact]
    public void while_she_strokes_she_winks_now_and_then_and_never_on_the_pedestal()
    {
        var face = new FaceMotion(5);
        static int Winks(FaceMotion face, double aroused)
        {
            var winks = 0;
            var winking = false;
            foreach (var t in Times(120))
            {
                var w = face.At(t, 0, aroused);
                var now = w.GetValueOrDefault("blinkLeft") + w.GetValueOrDefault("blinkRight") > 0.5;
                if (now && !winking) winks++;
                winking = now;
            }
            return winks;
        }
        Assert.InRange(Winks(face, 1), 2, 8);
        Assert.Equal(0, Winks(face, 0));
    }

    [Fact]
    public void stroking_she_breathes_through_parted_lips_under_heavy_lids()
    {
        var face = new FaceMotion(6);
        var calm = face.At(10, breath: 1, aroused: 0);
        var heated = face.At(10, breath: 1, aroused: 1);
        Assert.True(heated.GetValueOrDefault("aa") > calm.GetValueOrDefault("aa") + 0.05);
        Assert.True(heated.GetValueOrDefault("blink") > calm.GetValueOrDefault("blink") + 0.05);
        Assert.True(face.At(10, breath: 1, aroused: 1).GetValueOrDefault("aa") > face.At(10, breath: 0, aroused: 1).GetValueOrDefault("aa"));
    }

    [Fact]
    public void the_same_seed_makes_the_same_face()
    {
        Assert.Equal(new FaceMotion(7).At(13.3, 0.4, 1), new FaceMotion(7).At(13.3, 0.4, 1));
        Assert.Equal(new FaceMotion(7).Flickers(60), new FaceMotion(7).Flickers(60));
        Assert.NotEqual(new FaceMotion(7).Flickers(60), new FaceMotion(8).Flickers(60));
    }
}
