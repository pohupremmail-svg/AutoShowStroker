using System.Numerics;

namespace Gooner.Core.Characters;

// A mesh as the engine takes it: per vertex a position, a normal and an sRGB colour, and triangles
// counter-clockwise seen from outside, three indices each. In metres, Y up.
public sealed record ToyMesh(IReadOnlyList<Vector3> Positions, IReadOnlyList<Vector3> Normals, IReadOnlyList<Vector4> Colors, IReadOnlyList<int> Indices);

// The toy the app brings itself, built from code: it needs no model file, so it is the app's own, has
// no licence to clear, puts nothing explicit on GitHub and is in every package. A model in res/props
// still takes its place where there is one.
//
// Real size, standing on its foot along +Y with its balls on +Z, as ToyShape reads any model: a
// suction foot with a lip, a shaft that thickens a little towards its middle and curves up away from
// its balls, veins standing out of its upper side and a seam down its underside, a head over a neck
// whose ridge falls towards the underside where it meets the seam, a slit at its tip, and two balls
// in one sac with a seam of its own. The shaft is a body of revolution round a curved axis with all of
// that laid onto it; the sac is found along rays from its middle on a smooth union of the balls.
// Nothing is drawn from dice: it is the same toy every time.
public static class ToyModel
{
    public const float Length = 0.202f;

    // The neck under the head and the head's ridge, at the height they reach on the shaft's sides.
    public const float Neck = 0.1624f;
    public const float Rim = 0.1709f;

    // How far its tip curves away from its balls.
    private const float Bend = 0.013f;

    // How much lower the ridge sits on the underside than on top.
    private const float Dip = 0.0052f;

    private const int Around = 128;
    private const int Along = 260;

    // The outline turned round the axis, (radius, height) from the middle of the foot to the tip.
    private static readonly Vector2[] Outline =
    [
        new(0, 0), new(0.012f, 0), new(0.024f, 0.00005f), new(0.0305f, 0.0004f), new(0.0336f, 0.0019f),
        new(0.0334f, 0.0036f), new(0.0300f, 0.0055f), new(0.0245f, 0.0080f), new(0.0203f, 0.0125f),
        new(0.0189f, 0.0220f), new(0.0187f, 0.0400f), new(0.0193f, 0.0750f), new(0.0198f, 0.1100f),
        new(0.0193f, 0.1420f), new(0.0184f, 0.1560f), new(0.0176f, 0.1635f), new(0.0186f, 0.1668f),
        new(0.0218f, 0.1697f), new(0.0232f, 0.1722f),
        // The head: a rounded dome, a quarter ellipse from the ridge to the tip.
        new(0.02241f, 0.17991f), new(0.02009f, 0.18710f), new(0.01640f, 0.19327f), new(0.01160f, 0.19801f),
        new(0.00717f, 0.20054f), new(0.00323f, 0.20171f), new(0, Length),
    ];

    private static readonly Vector3 Body = Srgb(0.90f, 0.68f, 0.60f);
    private static readonly Vector3 BodyLow = Srgb(0.84f, 0.60f, 0.53f);
    private static readonly Vector3 Head = Srgb(0.86f, 0.50f, 0.51f);
    private static readonly Vector3 Vein = Srgb(0.68f, 0.47f, 0.58f);
    private static readonly Vector3 Sac = Srgb(0.80f, 0.56f, 0.50f);

    private static Vector3 Srgb(float r, float g, float b) => new(r, g, b);

    public static ToyMesh Build()
    {
        var mesh = new Builder();
        Shaft(mesh);
        Balls(mesh);
        return mesh.Finish();
    }

    // --- the shaft ---

    private static void Shaft(Builder mesh)
    {
        var outline = Resample(Outline, Along);
        var rows = new List<(Vector3[] Points, Vector3[] Colours)>();
        for (var i = 0; i < outline.Length; i++)
        {
            var (r0, h) = (outline[i].X, outline[i].Y);
            if (r0 < 1e-6f)
            {
                // The middle of the foot and the very tip: one point each.
                var at = Axis(h);
                rows.Add(([at], [h > Length / 2 ? Head : BodyLow]));
                continue;
            }
            var points = new Vector3[Around];
            var colours = new Vector3[Around];
            for (var j = 0; j < Around; j++)
            {
                var theta = 2 * MathF.PI * j / Around;
                var (cos, sin) = (MathF.Cos(theta), MathF.Sin(theta));
                var (raised, veined) = Relief(h, theta, r0);
                // The ridge and everything above it fall towards the underside (+Z, theta 0).
                var head = Smooth(0.150f, 0.168f, h) * (1 - Smooth(0.198f, Length, h));
                var fall = (1 + cos) / 2;
                var y = h - Dip * head * fall * fall;
                var (centre, ventral, side) = Frame(y);
                points[j] = centre + (r0 + raised) * (cos * ventral + sin * side);
                colours[j] = Colour(h, head, veined);
            }
            rows.Add((points, colours));
        }
        mesh.Grid(rows);
    }

    // The axis bends away from the balls (-Z), more and more towards the tip; at the foot it stands
    // straight up, so the foot sits flat.
    private static Vector3 Axis(float h) => new(0, h, -Bend * (h / Length) * (h / Length));

    // Where the axis is at height `h`, and the two directions round it: towards the underside, and to
    // the side (+X).
    private static (Vector3 Centre, Vector3 Ventral, Vector3 Side) Frame(float h)
    {
        var slope = -2 * Bend * h / (Length * Length);
        var along = Vector3.Normalize(new Vector3(0, 1, slope));
        return (Axis(h), Vector3.Normalize(Vector3.Cross(Vector3.UnitX, along)), Vector3.UnitX);
    }

    // What stands out of the turned outline at that height and angle, and how much of it is vein.
    private static (float Raised, float Veined) Relief(float h, float theta, float radius)
    {
        var raised = 0f;
        var veined = 0f;
        foreach (var vein in Veins)
        {
            if (h < vein.From || h > vein.To) continue;
            var fade = Smooth(vein.From, vein.From + 0.012f, h) * (1 - Smooth(vein.To - 0.015f, vein.To, h));
            // A vein swells and thins along its way.
            var amp = vein.Amp * fade * (0.75f + 0.25f * MathF.Sin(h * 61 + vein.Phase * 3));
            var off = Angle(theta - vein.Path(h)) * radius;
            var bump = amp * MathF.Exp(-(off * off) / (vein.Width * vein.Width));
            raised += bump;
            veined = MathF.Max(veined, bump / 0.0012f);
        }
        // The seam down the underside, from the foot to the ridge.
        var seam = Angle(theta) * radius;
        raised += 0.00045f * Smooth(0.012f, 0.03f, h) * (1 - Smooth(0.150f, 0.163f, h)) * MathF.Exp(-(seam * seam) / (0.0016f * 0.0016f));
        // Where the seam meets the head, a small notch in the ridge.
        raised -= 0.0016f * Bump(h, 0.1665f, 0.0045f) * MathF.Exp(-(seam * seam) / (0.0032f * 0.0032f));
        // The slit at the tip, across the seam's line.
        var across = MathF.Sin(theta) * radius;
        raised -= 0.00045f * Bump(h, 0.2008f, 0.0014f) * MathF.Exp(-(across * across) / (0.0005f * 0.0005f));
        return (raised, Math.Clamp(veined, 0, 1));
    }

    private sealed record VeinPath(float From, float To, float Amp, float Width, float Phase, Func<float, float> Path);

    // Their way round the shaft (theta, pi on top) by height: one long one along the top, two beside
    // it, a branch off the long one.
    private static readonly VeinPath[] Veins =
    [
        new(0.016f, 0.158f, 0.0014f, 0.0019f, 0.5f, h => MathF.PI + 0.32f * MathF.Sin(h * 34 + 0.5f) + 0.12f * MathF.Sin(h * 91)),
        new(0.030f, 0.140f, 0.0010f, 0.0016f, 1.7f, h => MathF.PI * 0.62f + 0.22f * MathF.Sin(h * 27 + 1.7f)),
        new(0.050f, 0.150f, 0.0009f, 0.0015f, 2.9f, h => MathF.PI * 1.42f + 0.28f * MathF.Sin(h * 22 + 2.9f)),
        new(0.088f, 0.136f, 0.0008f, 0.0013f, 4.1f, h => MathF.PI + 0.32f * MathF.Sin(h * 34 + 0.5f) - 0.95f * Smooth(0.088f, 0.136f, h)),
    ];

    private static Vector3 Colour(float h, float head, float veined)
    {
        var body = Vector3.Lerp(BodyLow, Body, Smooth(0.01f, 0.09f, h));
        // The neck flushes towards the head.
        body = Vector3.Lerp(body, Head, 0.35f * Smooth(0.145f, 0.163f, h));
        var colour = Vector3.Lerp(body, Head, Smooth(0.6f, 0.95f, head));
        return Vector3.Lerp(colour, Vein, 0.55f * veined);
    }

    // --- the balls ---

    private static readonly Vector3 SacMiddle = new(0, 0.033f, 0.016f);

    private static void Balls(Builder mesh)
    {
        const int rings = 48, around = 72;
        var rows = new List<(Vector3[] Points, Vector3[] Colours)>();
        for (var i = 0; i <= rings; i++)
        {
            var lat = -MathF.PI / 2 + MathF.PI * i / rings;
            var count = i == 0 || i == rings ? 1 : around;
            var points = new Vector3[count];
            var colours = new Vector3[count];
            for (var j = 0; j < count; j++)
            {
                var lon = 2 * MathF.PI * j / around;
                var dir = new Vector3(MathF.Sin(lon) * MathF.Cos(lat), MathF.Sin(lat), MathF.Cos(lon) * MathF.Cos(lat));
                points[j] = Surface(SacMiddle, dir);
                var seam = MathF.Exp(-(points[j].X * points[j].X) / (0.002f * 0.002f));
                colours[j] = Vector3.Lerp(Sac, Vein, 0.25f * seam * Smooth(SacMiddle.Z, SacMiddle.Z + 0.01f, points[j].Z));
            }
            rows.Add((points, colours));
        }
        mesh.Grid(rows);
    }

    // The sac's surface along a ray from its middle: the first point where the distance turns positive.
    private static Vector3 Surface(Vector3 from, Vector3 dir)
    {
        const float step = 0.0008f;
        var (inside, outside) = (0f, step);
        while (SacDistance(from + dir * outside) < 0 && outside < 0.08f)
            (inside, outside) = (outside, outside + step);
        for (var k = 0; k < 24; k++)
        {
            var mid = (inside + outside) / 2;
            if (SacDistance(from + dir * mid) < 0) inside = mid;
            else outside = mid;
        }
        return from + dir * ((inside + outside) / 2);
    }

    // Roughly the distance to the sac: two balls hanging a little, joined smoothly to each other and to
    // the root of the shaft, a seam down between them.
    private static float SacDistance(Vector3 p)
    {
        // Hanging: each ball longer than it is wide, one a little lower than the other, both well inside
        // one sac that runs up into the root of the shaft.
        var left = Ellipsoid(p, new Vector3(-0.0135f, 0.0285f, 0.0185f), new Vector3(0.0166f, 0.0208f, 0.0160f));
        var right = Ellipsoid(p, new Vector3(0.0130f, 0.0305f, 0.0180f), new Vector3(0.0162f, 0.0202f, 0.0158f));
        var root = Ellipsoid(p, new Vector3(0, 0.046f, 0.007f), new Vector3(0.0175f, 0.0245f, 0.0135f));
        var d = SmoothMin(SmoothMin(left, right, 0.012f), root, 0.012f);
        var front = Smooth(SacMiddle.Z - 0.004f, SacMiddle.Z + 0.010f, p.Z);
        // The seam down the middle, and the skin folded finely across it.
        var seam = 0.0010f * front * MathF.Exp(-(p.X * p.X) / (0.0024f * 0.0024f));
        var folds = 0.00032f * MathF.Sin(p.X * 820 + p.Y * 260) * MathF.Sin(p.Y * 610 - p.Z * 430 + p.X * 140);
        return d + seam + folds;
    }

    private static float Ellipsoid(Vector3 p, Vector3 centre, Vector3 radii)
    {
        var k = ((p - centre) / radii).Length();
        return (k - 1) * MathF.Min(radii.X, MathF.Min(radii.Y, radii.Z));
    }

    private static float SmoothMin(float a, float b, float k)
    {
        var h = Math.Clamp(0.5f + 0.5f * (b - a) / k, 0, 1);
        return b + (a - b) * h - k * h * (1 - h);
    }

    // --- helpers ---

    private static float Smooth(float from, float to, float x)
    {
        var t = Math.Clamp((x - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static float Bump(float x, float at, float width) => MathF.Exp(-((x - at) * (x - at)) / (width * width));

    // An angle brought into -pi..pi.
    private static float Angle(float a) => MathF.IEEERemainder(a, 2 * MathF.PI);

    // `count` points along the outline at even distances, on a Catmull-Rom curve through it: its first
    // and last are the outline's own.
    private static Vector2[] Resample(Vector2[] outline, int count)
    {
        var dense = new List<Vector2>();
        for (var k = 0; k < outline.Length - 1; k++)
        {
            var (p0, p1, p2, p3) = (outline[Math.Max(0, k - 1)], outline[k], outline[k + 1], outline[Math.Min(outline.Length - 1, k + 2)]);
            for (var s = 0; s < 40; s++)
                dense.Add(CatmullRom(p0, p1, p2, p3, s / 40f));
        }
        dense.Add(outline[^1]);
        var lengths = new float[dense.Count];
        for (var k = 1; k < dense.Count; k++) lengths[k] = lengths[k - 1] + Vector2.Distance(dense[k - 1], dense[k]);
        var result = new Vector2[count];
        var at = 0;
        for (var i = 0; i < count; i++)
        {
            var want = lengths[^1] * i / (count - 1);
            while (at < dense.Count - 2 && lengths[at + 1] < want) at++;
            var t = (want - lengths[at]) / MathF.Max(1e-9f, lengths[at + 1] - lengths[at]);
            result[i] = Vector2.Lerp(dense[at], dense[at + 1], Math.Clamp(t, 0, 1));
        }
        result[0] = outline[0];
        result[^1] = outline[^1];
        return result;
    }

    private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        var (t2, t3) = (t * t, t * t * t);
        return 0.5f * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (3 * p1 - p0 - 3 * p2 + p3) * t3);
    }

    // Rows of points from bottom to top, each round the same way (from +Z towards +X); a row of one is
    // a pole. Joined into triangles counter-clockwise from outside, with smooth normals.
    private sealed class Builder
    {
        private readonly List<Vector3> _positions = [];
        private readonly List<Vector3> _colours = [];
        private readonly List<int> _indices = [];

        public void Grid(List<(Vector3[] Points, Vector3[] Colours)> rows)
        {
            var starts = new List<int>();
            foreach (var (points, colours) in rows)
            {
                starts.Add(_positions.Count);
                _positions.AddRange(points);
                _colours.AddRange(colours);
            }
            for (var i = 0; i < rows.Count - 1; i++)
            {
                var (low, high) = (rows[i].Points.Length, rows[i + 1].Points.Length);
                var (a, b) = (starts[i], starts[i + 1]);
                for (var j = 0; j < Math.Max(low, high); j++)
                {
                    var next = j + 1;
                    if (low == 1) Triangle(a, b + next % high, b + j);
                    else if (high == 1) Triangle(a + j, a + next % low, b);
                    else
                    {
                        Triangle(a + j, a + next % low, b + next % high);
                        Triangle(a + j, b + next % high, b + j);
                    }
                }
            }
        }

        private void Triangle(int a, int b, int c)
        {
            _indices.Add(a);
            _indices.Add(b);
            _indices.Add(c);
        }

        public ToyMesh Finish()
        {
            var normals = new Vector3[_positions.Count];
            for (var t = 0; t < _indices.Count; t += 3)
            {
                var (a, b, c) = (_indices[t], _indices[t + 1], _indices[t + 2]);
                var face = Vector3.Cross(_positions[b] - _positions[a], _positions[c] - _positions[a]);
                normals[a] += face;
                normals[b] += face;
                normals[c] += face;
            }
            for (var k = 0; k < normals.Length; k++)
                normals[k] = normals[k].LengthSquared() > 0 ? Vector3.Normalize(normals[k]) : Vector3.UnitY;
            return new ToyMesh(_positions, normals, [.. _colours.Select(c => new Vector4(c, 1))], _indices);
        }
    }
}
