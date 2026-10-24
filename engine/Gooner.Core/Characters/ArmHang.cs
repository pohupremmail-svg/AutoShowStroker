using System.Numerics;

namespace Gooner.Core.Characters;

// How far down her free arm hangs: turned from where it rests about the axis towards the viewer (+Z),
// by an angle in degrees - negative for her left arm, positive for her right. Close by her side is how an
// arm hangs, but on wide hips that put her hand into them (the user saw her arms in her belly and her
// hoodie). Settled once per figure, from the points of her body at rest: the angle asked for if the arm
// passes clear of her there, otherwise the first one further out that does - or, failing that, the one
// that passes through her least. Skeleton space, +Y up.
public static class ArmHang
{
    // Never further out than this: a figure whose loose clothes or hair hang wide would otherwise hold
    // her arm out like a scarecrow's.
    private const float Step = 2, Furthest = 25;

    public static float Pick(Vector3 shoulder, Vector3 rest, float length, float preferred, IReadOnlyList<Vector3> body, float thickness)
    {
        var (best, fewest) = (preferred, Hits(shoulder, rest, length, preferred, body, thickness));
        for (var out_ = Step; out_ <= Furthest && fewest > 0; out_ += Step)
        {
            var angle = preferred - Math.Sign(preferred) * out_;
            var hits = Hits(shoulder, rest, length, angle, body, thickness);
            if (hits < fewest) (best, fewest) = (angle, hits);
        }
        return best;
    }

    // How many points of her body the arm passes through, hanging at `angle`: from below the shoulder,
    // where it always meets her, to the end of the hand.
    public static int Hits(Vector3 shoulder, Vector3 rest, float length, float angle, IReadOnlyList<Vector3> body, float thickness)
    {
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle * MathF.PI / 180);
        var dir = Vector3.Normalize(Vector3.Transform(rest, turn));
        var from = shoulder + dir * length * 0.3f;
        var to = shoulder + dir * length;
        var hits = 0;
        foreach (var p in body)
            if (Distance(p, from, to) < thickness) hits++;
        return hits;
    }

    private static float Distance(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        var t = Math.Clamp(Vector3.Dot(p - a, ab) / Math.Max(1e-12f, ab.LengthSquared()), 0, 1);
        return (p - (a + ab * t)).Length();
    }
}
