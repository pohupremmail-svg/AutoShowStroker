using System.Numerics;
using Gooner.Core.Characters;

namespace Gooner.Core.Tests.Characters;

// The toy in her hand is a model (res/props), measured where it is loaded rather than assumed: how long
// it is, how thick its shaft, where its axis runs, how far its foot and balls stand out behind it and on
// which side they are - so her hand closes on the shaft it has and it stands clear of her belly by
// what really sticks out of it, whatever model it is.
public sealed class ToyShapeTests
{
    // A shaft round (axisX, axisZ) from a foot at y = 0 to `length`, a wider foot, and balls on the
    // side `ballsZ` points to.
    private static List<Vector3> Toy(float axisX, float axisZ, float radius, float length, float ballsZ)
    {
        var points = new List<Vector3>();
        for (var a = 0; a < 360; a += 10)
        {
            var (c, s) = (MathF.Cos(a * MathF.PI / 180), MathF.Sin(a * MathF.PI / 180));
            for (var y = 0.02f; y <= length; y += 0.005f) points.Add(new(axisX + radius * c, y, axisZ + radius * s));
            points.Add(new(axisX + 1.7f * radius * c, 0, axisZ + 1.7f * radius * s));
            points.Add(new(axisX + 0.9f * radius * c, 0.03f + 0.9f * radius * s, axisZ + ballsZ + 0.9f * radius * s));
        }
        return points;
    }

    [Fact]
    public void it_measures_the_shaft_it_has()
    {
        var shape = ToyShape.Measure(Toy(0.01f, -0.005f, 0.02f, 0.22f, -0.04f))!;
        Assert.Equal(0.22f, shape.Length, 2);
        Assert.Equal(0.02f, shape.Radius, 3);
        Assert.Equal(new Vector3(0.01f, 0, -0.005f), shape.Foot, new Vec3Tolerance(0.003f));
    }

    [Fact]
    public void the_balls_side_is_behind_and_its_reach_is_measured()
    {
        var shape = ToyShape.Measure(Toy(0, 0, 0.02f, 0.22f, -0.04f))!;
        Assert.False(shape.BallsTowardsViewer);
        Assert.Equal(0.04f + 0.018f, shape.Behind, 3);

        var turned = ToyShape.Measure(Toy(0, 0, 0.02f, 0.22f, 0.04f))!;
        Assert.True(turned.BallsTowardsViewer);
        Assert.Equal(0.04f + 0.018f, turned.Behind, 3);
    }

    // A real one curves: the hand follows its middle at the height it is at, not a straight line
    // through its foot (her fingers stood off the shaft of the user's model, seen from the side).
    [Fact]
    public void the_middle_of_a_curved_shaft_is_followed_up_its_length()
    {
        var points = new List<Vector3>();
        for (var y = 0f; y <= 0.2f; y += 0.004f)
            for (var a = 0; a < 360; a += 15)
                points.Add(new(0.02f * MathF.Cos(a * MathF.PI / 180), y, 0.3f * y * y + 0.02f * MathF.Sin(a * MathF.PI / 180)));
        var shape = ToyShape.Measure(points)!;
        // Its thickness across the shaft, not across the stretch its curve sweeps.
        Assert.Equal(0.02f, shape.Radius, 3);
        foreach (var share in new[] { 0.35f, 0.5f, 0.8f })
        {
            var y = share * 0.2f;
            var centre = shape.CentreAt(share);
            Assert.Equal(y, centre.Y, 3);
            Assert.Equal(0.3f * y * y, centre.Z, 3);
            Assert.Equal(0, centre.X, 3);
        }
    }

    [Fact]
    public void nothing_to_measure_is_no_shape()
    {
        Assert.Null(ToyShape.Measure([]));
        Assert.Null(ToyShape.Measure([Vector3.Zero, Vector3.Zero]));
    }

    private sealed class Vec3Tolerance(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => (a - b).Length() < tolerance;
        public int GetHashCode(Vector3 v) => 0;
    }
}
