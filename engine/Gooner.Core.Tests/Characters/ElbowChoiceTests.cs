using System.Numerics;
using Gooner.Core.Characters;

namespace Gooner.Core.Tests.Characters;

// Which way her elbow bends while her hand holds the toy: out to her side and back by default, but on
// a wide body that took her forearm through her waist on its way to the toy (the user: "der Arm morphed
// schon wieder in den Bauch"). The elbow goes where neither part of the arm passes through her.
public sealed class ElbowChoiceTests
{
    // A torso as a column of points round the Y axis, from the hips up to the chest.
    private static List<Vector3> Torso(float radius)
    {
        var points = new List<Vector3>();
        for (var y = 0.8f; y <= 1.45f; y += 0.02f)
            for (var a = 0; a < 360; a += 6)
                points.Add(new Vector3(radius * MathF.Cos(a * MathF.PI / 180), y, radius * MathF.Sin(a * MathF.PI / 180)));
        return points;
    }

    private static readonly Vector3 Shoulder = new(-0.19f, 1.4f, 0);
    private const float Upper = 0.28f, Fore = 0.26f, Thickness = 0.03f;
    private static readonly Vector3 Back = new(-1, -0.4f, -0.7f);
    private static readonly Vector3[] Wrists = [new(-0.04f, 0.98f, 0.22f), new(-0.04f, 1.04f, 0.23f)];

    [Fact]
    public void on_a_wide_waist_the_elbow_leaves_the_back_and_the_arm_stays_outside_her()
    {
        var body = Torso(0.17f);
        Assert.True(ElbowChoice.Hits(Shoulder, Upper, Fore, Wrists, Back, body, Thickness) > 0);
        var pole = ElbowChoice.Pick(Shoulder, Upper, Fore, Wrists, body, Thickness, Back);
        Assert.Equal(0, ElbowChoice.Hits(Shoulder, Upper, Fore, Wrists, pole, body, Thickness));
    }

    // Hips far wider than her shoulders (a figure the user brought): an elbow bent back or forward still
    // ends up in her flank - it has to go out, and up, as anyone's would.
    [Fact]
    public void on_hips_wider_than_her_shoulders_the_elbow_goes_out_and_up()
    {
        var body = new List<Vector3>();
        for (var y = 0.8f; y <= 1.45f; y += 0.02f)
            for (var a = 0; a < 360; a += 6)
            {
                var r = y > 1.3f ? 0.12f : 0.24f;
                body.Add(new Vector3(r * MathF.Cos(a * MathF.PI / 180), y, r * MathF.Sin(a * MathF.PI / 180)));
            }
        Vector3[] wrists = [new(-0.06f, 1.08f, 0.33f), new(-0.06f, 1.14f, 0.34f)];
        var shoulder = Shoulder with { X = -0.22f };
        var pole = ElbowChoice.Pick(shoulder, Upper, Fore, wrists, body, Thickness, Back);
        Assert.Equal(0, ElbowChoice.Hits(shoulder, Upper, Fore, wrists, pole, body, Thickness));
        Assert.True(pole.Y > Vector3.Normalize(Back).Y);
    }

    [Fact]
    public void a_slim_waist_keeps_the_elbow_out_to_the_side_and_back()
    {
        var body = Torso(0.09f);
        Assert.Equal(0, ElbowChoice.Hits(Shoulder, Upper, Fore, Wrists, Back, body, Thickness));
        Assert.Equal(Vector3.Normalize(Back), ElbowChoice.Pick(Shoulder, Upper, Fore, Wrists, body, Thickness, Back));
    }

    [Fact]
    public void the_elbow_sits_where_the_two_bones_meet()
    {
        var wrist = Wrists[0];
        var elbow = ElbowChoice.Elbow(Shoulder, Upper, Fore, wrist, Back);
        Assert.Equal(Upper, (elbow - Shoulder).Length(), 3);
        Assert.Equal(Fore, (wrist - elbow).Length(), 3);
        Assert.True(Vector3.Dot(elbow - (Shoulder + wrist) / 2, Back) > 0);
    }
}
