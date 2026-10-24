using System.Numerics;

namespace Gooner.Core.Characters;

// The toy in her hand, measured from the points of its model (res/props) where it is loaded, never
// assumed - so any model will do: standing on its foot along +Y, its shaft where it is thickest
// between its foot and its tip, and whatever sticks out of it to one side (balls, a suction cup) put
// behind it, towards her. In the model's own units and axes.
public sealed record ToyShape(
    // The middle of its foot, where its axis meets the floor it stands on.
    Vector3 Foot,
    // From its foot to its tip.
    float Length,
    // The shaft's, where the hand slides.
    float Radius,
    // How far it reaches behind its axis on the side its balls are, and to either side of it.
    float Behind,
    float HalfWidth,
    // Its balls on +Z, the side that faces the viewer: turned round before it is placed.
    bool BallsTowardsViewer,
    // The middle of the shaft at evenly spaced heights from foot to tip; a real one curves.
    IReadOnlyList<Vector3> Middle)
{
    // The stretch of its height that is shaft for certain: above balls and foot, below the tip.
    private const float ShaftFrom = 0.45f, ShaftTo = 0.7f;

    public static ToyShape? Measure(IReadOnlyCollection<Vector3> points)
    {
        if (points.Count < 3) return null;
        var bottom = points.Min(p => p.Y);
        var length = points.Max(p => p.Y) - bottom;
        if (length < 1e-4f) return null;
        var shaft = points.Where(p => p.Y >= bottom + ShaftFrom * length && p.Y <= bottom + ShaftTo * length).ToList();
        if (shaft.Count == 0) return null;
        var (left, right) = (shaft.Min(p => p.X), shaft.Max(p => p.X));
        var (back, front) = (shaft.Min(p => p.Z), shaft.Max(p => p.Z));
        var axis = new Vector3((left + right) / 2, bottom, (back + front) / 2);
        var behind = axis.Z - points.Min(p => p.Z);
        var ahead = points.Max(p => p.Z) - axis.Z;
        var middle = new List<Vector3>();
        // Its thickness slice by slice where it is shaft for certain: measured over the whole stretch at
        // once, a curve would add the distance it sweeps.
        var radii = new List<float>();
        for (var i = 0; i <= Steps; i++)
        {
            var y = bottom + length * i / Steps;
            var slice = points.Where(p => Math.Abs(p.Y - y) < Slice * length).ToList();
            if (slice.Count == 0)
            {
                middle.Add(axis with { Y = y });
                continue;
            }
            var (sx, bx, sz, bz) = (slice.Min(p => p.X), slice.Max(p => p.X), slice.Min(p => p.Z), slice.Max(p => p.Z));
            middle.Add(new Vector3((sx + bx) / 2, y, (sz + bz) / 2));
            if (i >= ShaftFrom * Steps && i <= ShaftTo * Steps) radii.Add((bx - sx + bz - sz) / 4);
        }
        var radius = radii.Count > 0 ? radii.Average() : (right - left + front - back) / 4;
        return new ToyShape(axis, length, radius, Math.Max(behind, ahead),
            points.Max(p => Math.Abs(p.X - axis.X)), ahead > behind, middle);
    }

    private const int Steps = 20;
    private const float Slice = 0.03f;

    // The middle of the shaft a share of the way from its foot to its tip, where the hand closes on
    // it. Never lower than the shaft goes: down there balls and foot pull the middle aside.
    public Vector3 CentreAt(float share)
    {
        var at = Math.Clamp(share, ShaftLowest, 1) * Steps;
        var i = Math.Min((int)at, Steps - 1);
        return Vector3.Lerp(Middle[i], Middle[i + 1], at - i);
    }

    private const float ShaftLowest = 0.3f;
}
