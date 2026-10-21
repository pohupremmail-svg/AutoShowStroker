using System.Numerics;
using Gooner.Core.Characters;

namespace Gooner.Core.Tests.Characters;

// How far down her free arm hangs. Close by her side is how an arm hangs, but on wide hips that put her
// hand into them (the user saw her arms in her belly and her hoodie). It hangs as close as it can
// without passing through her.
public sealed class ArmHangTests
{
    // A torso round the Y axis: `chest` wide above 1.1, `hips` wide below it.
    private static List<Vector3> Body(float chest, float hips)
    {
        var points = new List<Vector3>();
        for (var y = 0.7f; y <= 1.45f; y += 0.02f)
            for (var a = 0; a < 360; a += 6)
            {
                var r = y > 1.1f ? chest : hips;
                points.Add(new Vector3(r * MathF.Cos(a * MathF.PI / 180), y, r * MathF.Sin(a * MathF.PI / 180)));
            }
        return points;
    }

    private static readonly Vector3 LeftShoulder = new(0.19f, 1.4f, 0);
    private const float Length = 0.55f, Thickness = 0.03f;

    [Fact]
    public void on_wide_hips_her_arm_hangs_further_out_and_clear_of_them()
    {
        var body = Body(0.12f, 0.3f);
        Assert.True(ArmHang.Hits(LeftShoulder, Vector3.UnitX, Length, -77, body, Thickness) > 0);
        var angle = ArmHang.Pick(LeftShoulder, Vector3.UnitX, Length, -77, body, Thickness);
        Assert.InRange(angle, -76, -52);
        Assert.Equal(0, ArmHang.Hits(LeftShoulder, Vector3.UnitX, Length, angle, body, Thickness));
    }

    // Wide clothes or hair hanging far out never hold her arm out further than 25 degrees.
    [Fact]
    public void however_wide_she_is_her_arm_is_never_held_far_out()
    {
        foreach (var hips in new[] { 0.35f, 0.45f, 0.6f })
            Assert.InRange(ArmHang.Pick(LeftShoulder, Vector3.UnitX, Length, -77, Body(0.12f, hips), Thickness), -77, -52);
    }

    [Fact]
    public void on_slim_hips_it_hangs_as_it_always_did()
    {
        Assert.Equal(-77, ArmHang.Pick(LeftShoulder, Vector3.UnitX, Length, -77, Body(0.12f, 0.15f), Thickness));
    }

    [Fact]
    public void the_right_arm_turns_the_other_way()
    {
        var body = Body(0.12f, 0.3f);
        var right = LeftShoulder with { X = -LeftShoulder.X };
        var angle = ArmHang.Pick(right, -Vector3.UnitX, Length, 76, body, Thickness);
        Assert.InRange(angle, 51, 75);
        Assert.Equal(0, ArmHang.Hits(right, -Vector3.UnitX, Length, angle, body, Thickness));
    }
}
