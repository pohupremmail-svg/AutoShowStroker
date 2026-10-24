using System.Numerics;

namespace Gooner.Core.Characters;

// Which way her elbow bends while her hand holds the toy. Out to her side and back is how an arm hangs,
// but on a wide body it took her forearm through her waist on its way to the toy in front of her (the
// user: "der Arm morphed schon wieder in den Bauch"). Settled once per figure, from the points of her
// body at rest: of a few directions, the first in which neither part of the arm passes through her at
// either end of the stroke - the hanging one if it does - or else the one that passes through least.
// Skeleton space, +Y up, +Z towards the viewer, her right towards -X.
public static class ElbowChoice
{
    // The directions tried after `preferred`: forward and back of it, and with the elbow raised out to
    // her side - on hips wider than her shoulders a hanging upper arm is in her flank whichever way the
    // elbow bends. Nearest to `preferred` first, so she keeps the most natural one that is clear.
    private static readonly float[] Forward = [-0.7f, -0.5f, -0.3f, -0.1f, 0.1f, 0.3f, 0.5f, 0.7f];
    private static readonly float[] Raised = [0, 0.3f, 0.6f, 0.9f];

    public static Vector3 Pick(Vector3 shoulder, float upper, float fore, IReadOnlyList<Vector3> wrists,
        IReadOnlyList<Vector3> body, float thickness, Vector3 preferred)
    {
        var first = Vector3.Normalize(preferred);
        var candidates = Raised.SelectMany(up => Forward.Select(z => Vector3.Normalize(preferred with { Y = preferred.Y + up, Z = z })))
            .OrderBy(pole => -Vector3.Dot(pole, first));
        var (best, fewest) = (first, Hits(shoulder, upper, fore, wrists, first, body, thickness));
        foreach (var pole in candidates)
        {
            if (fewest == 0) break;
            var hits = Hits(shoulder, upper, fore, wrists, pole, body, thickness);
            if (hits < fewest) (best, fewest) = (pole, hits);
        }
        return best;
    }

    // How many points of her body the arm passes through, reaching each wrist with the elbow bent
    // towards `pole`: the upper arm from below the shoulder (where it always meets her) and the forearm
    // short of the wrist (the hand's business, at the toy).
    public static int Hits(Vector3 shoulder, float upper, float fore, IReadOnlyList<Vector3> wrists, Vector3 pole,
        IReadOnlyList<Vector3> body, float thickness)
    {
        var hits = 0;
        foreach (var wrist in wrists)
        {
            var elbow = Elbow(shoulder, upper, fore, wrist, pole);
            var upperFrom = Vector3.Lerp(shoulder, elbow, 0.4f);
            var foreTo = Vector3.Lerp(elbow, wrist, 0.85f);
            foreach (var p in body)
                if (Distance(p, upperFrom, elbow) < thickness || Distance(p, elbow, foreTo) < thickness) hits++;
        }
        return hits;
    }

    // Where the elbow is when the wrist is at `wrist`: the two bones' lengths, bent out towards `pole` -
    // the same two-bone solution the figure's arm is posed with; out of reach, the arm straight towards it.
    public static Vector3 Elbow(Vector3 shoulder, float upper, float fore, Vector3 wrist, Vector3 pole)
    {
        var reach = wrist - shoulder;
        var d = Math.Clamp(reach.Length(), Math.Abs(upper - fore) + 1e-4f, upper + fore - 1e-4f);
        var dir = Vector3.Normalize(reach);
        var along = (upper * upper - fore * fore + d * d) / (2 * d);
        var off = MathF.Sqrt(Math.Max(0, upper * upper - along * along));
        var bend = Vector3.Normalize(pole - dir * Vector3.Dot(pole, dir));
        return shoulder + dir * along + bend * off;
    }

    private static float Distance(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        var t = Math.Clamp(Vector3.Dot(p - a, ab) / Math.Max(1e-12f, ab.LengthSquared()), 0, 1);
        return (p - (a + ab * t)).Length();
    }
}
