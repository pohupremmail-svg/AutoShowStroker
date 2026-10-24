using System.Numerics;
using Gooner.Core.Characters;

namespace Gooner.Core.Tests.Characters;

// The toy the app brings itself: built from code rather than read from a model file, so it is the
// app's own, needs no licence and puts no explicit model on GitHub - and a package built anywhere has
// it. Held to what her hand needs from any toy: ToyShape measures it as it measures a loaded model,
// and it has to come out as the toy it was made to be.
public sealed class ToyModelTests
{
    private static readonly ToyMesh Mesh = ToyModel.Build();
    private static readonly ToyShape Shape = ToyShape.Measure(Mesh.Positions)!;

    [Fact]
    public void it_is_measured_as_a_loaded_model_would_be()
    {
        Assert.NotNull(Shape);
        Assert.Equal(ToyModel.Length, Shape.Length, 3);
        // A hand-sized shaft, about 4 cm across.
        Assert.InRange(Shape.Radius, 0.017f, 0.022f);
        // ToyShape puts the foot where the axis of the shaft's middle meets the floor; a curved shaft has
        // that a few millimetres off the middle of the foot itself.
        Assert.Equal(Vector3.Zero, Shape.Foot, new Vec3Tolerance(0.006f));
    }

    // Its balls are on +Z, which ToyShape reads as towards the viewer, and turns round towards her.
    [Fact]
    public void its_balls_stand_out_on_one_side()
    {
        Assert.True(Shape.BallsTowardsViewer);
        Assert.InRange(Shape.Behind, 0.03f, 0.06f);
    }

    // A real one curves - up, away from its balls - and her fingers follow the middle of its shaft.
    [Fact]
    public void it_curves_away_from_its_balls()
    {
        var foot = Shape.CentreAt(0.35f);
        var top = Shape.CentreAt(0.9f);
        Assert.InRange(foot.Z - top.Z, 0.004f, 0.025f);
    }

    // The head is wider than the neck below it: the ridge her hand slides over.
    [Fact]
    public void the_head_stands_out_over_its_neck()
    {
        float Across(float y) => Mesh.Positions.Where(p => Math.Abs(p.Y - y) < 0.0008f).Max(p => Math.Abs(p.X));
        Assert.True(Across(ToyModel.Rim) > Across(ToyModel.Neck) + 0.002f);
    }

    // ... but by a few millimetres, no more: the user found a ridge standing 3.7 mm proud of the shaft
    // too much.
    [Fact]
    public void the_head_stands_out_by_little_more_than_two_millimetres()
    {
        float Across(float y) => Mesh.Positions.Where(p => Math.Abs(p.Y - y) < 0.0008f).Max(p => Math.Abs(p.X));
        Assert.InRange(Across(ToyModel.Rim) - Across(0.14f), 0.0005f, 0.0025f);
    }

    // The head is a dome all the way to its tip. Its ridge slants down towards the seam, and done by
    // lowering the underside alone that left a step just below the tip, where the head rose back up to
    // a tip that had stayed put: along the underside the outline only ever bends inwards.
    [Fact]
    public void the_head_rounds_off_to_its_tip_without_a_step()
    {
        var shape = Shape;
        var underside = Mesh.Positions
            .Where(p => Math.Abs(p.X) < 0.0004f && p.Y > ToyModel.Rim + 0.002f && p.Y < ToyModel.Length - 0.004f)
            .Select(p => (p.Y, Out: p.Z - shape.CentreAt((p.Y - shape.Foot.Y) / shape.Length).Z))
            .Where(p => p.Out > 0)
            .OrderBy(p => p.Y)
            .ToList();
        Assert.True(underside.Count > 8);
        var slopes = underside.Zip(underside.Skip(1))
            .Where(pair => pair.Second.Y - pair.First.Y > 1e-4f)
            .Select(pair => (pair.Second.Out - pair.First.Out) / (pair.Second.Y - pair.First.Y))
            .ToList();
        for (var i = 1; i < slopes.Count; i++)
            Assert.True(slopes[i] <= slopes[i - 1] + 0.15f, $"the outline bends outwards at step {i}: {slopes[i - 1]:0.00} -> {slopes[i]:0.00}");
    }

    // Veins stand out of the shaft on the side facing away from the balls; the shaft is not a turned
    // cylinder.
    [Fact]
    public void the_shaft_carries_veins()
    {
        // Measured from the ring's own middle: the shaft curves, so its axis is not at the origin.
        var ring = Mesh.Positions.Where(p => Math.Abs(p.Y - 0.09f) < 0.0007f).ToList();
        var middle = new Vector2((ring.Min(p => p.X) + ring.Max(p => p.X)) / 2, (ring.Min(p => p.Z) + ring.Max(p => p.Z)) / 2);
        var away = ring.Where(p => p.Z < middle.Y).Select(p => (new Vector2(p.X, p.Z) - middle).Length()).ToList();
        Assert.True(away.Max() - away.Min() > 0.0006f);
    }

    [Fact]
    public void every_triangle_is_a_real_one_and_faces_out()
    {
        var p = Mesh.Positions;
        Assert.Equal(0, Mesh.Indices.Count % 3);
        Assert.All(Mesh.Indices, i => Assert.InRange(i, 0, p.Count - 1));
        // Counter-clockwise seen from outside, every part: the volume they enclose comes out positive,
        // and about what a shaft of that size with its balls holds (a quarter of a litre and a bit).
        var volume = 0.0;
        for (var t = 0; t < Mesh.Indices.Count; t += 3)
            volume += Vector3.Dot(p[Mesh.Indices[t]], Vector3.Cross(p[Mesh.Indices[t + 1]], p[Mesh.Indices[t + 2]])) / 6.0;
        Assert.InRange(volume, 1.5e-4, 6e-4);
        // And the normals agree with them.
        var inward = 0;
        for (var t = 0; t < Mesh.Indices.Count; t += 3)
        {
            var (a, b, c) = (p[Mesh.Indices[t]], p[Mesh.Indices[t + 1]], p[Mesh.Indices[t + 2]]);
            var face = Vector3.Cross(b - a, c - a);
            if (face.LengthSquared() < 1e-20f) continue;
            var normal = Mesh.Normals[Mesh.Indices[t]] + Mesh.Normals[Mesh.Indices[t + 1]] + Mesh.Normals[Mesh.Indices[t + 2]];
            if (Vector3.Dot(face, normal) <= 0) inward++;
        }
        Assert.True(inward == 0, $"{inward} triangles face in");
    }

    // Not a colour but where it goes - head, vein, sac, how near the foot - so that the colour the user
    // picks is laid on at once, without building it again (ToyColours).
    [Fact]
    public void every_vertex_has_a_normal_and_says_what_part_it_is()
    {
        Assert.Equal(Mesh.Positions.Count, Mesh.Normals.Count);
        Assert.Equal(Mesh.Positions.Count, Mesh.Regions.Count);
        Assert.All(Mesh.Normals, n => Assert.Equal(1f, n.Length(), 3));
        Assert.All(Mesh.Regions, r => Assert.True(r.X is >= 0 and <= 1 && r.Y is >= 0 and <= 1 && r.Z is >= 0 and <= 1 && r.W is >= 0 and <= 1));
    }

    [Fact]
    public void the_parts_are_where_they_are()
    {
        var p = Mesh.Positions;
        var r = Mesh.Regions;
        var tip = Enumerable.Range(0, p.Count).MaxBy(i => p[i].Y);
        Assert.Equal(1f, r[tip].X, 2);
        var ball = Enumerable.Range(0, p.Count).MaxBy(i => p[i].Z);
        Assert.Equal(1f, r[ball].Z, 2);
        Assert.True(Enumerable.Range(0, p.Count).Any(i => r[i].Y > 0.8f), "no vein");
        var foot = Enumerable.Range(0, p.Count).First(i => p[i].Y < 0.001f && Math.Abs(p[i].X) > 0.03f);
        Assert.Equal(1f, r[foot].W, 2);
    }

    // The same toy every time: nothing in it is drawn from dice.
    [Fact]
    public void it_is_the_same_toy_every_time()
    {
        var again = ToyModel.Build();
        Assert.Equal(Mesh.Positions, again.Positions);
        Assert.Equal(Mesh.Indices, again.Indices);
    }

    // Detailed enough to look like one up close in the frame, small enough to build in a moment.
    [Fact]
    public void it_is_a_model_of_a_sensible_size() =>
        Assert.InRange(Mesh.Indices.Count / 3, 15_000, 80_000);

    private sealed class Vec3Tolerance(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => (a - b).Length() < tolerance;
        public int GetHashCode(Vector3 v) => 0;
    }
}
