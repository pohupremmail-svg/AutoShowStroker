namespace Gooner.Core.Characters;

// A figure standing with nothing to do, the way a person stands - the curves her pose is made from.
// Pure sines read as a machine: the same rise and fall, at the same pace, for ever. So she breathes in
// quicker than out with a rest at the bottom, each breath a little different; her weight sits on one
// leg for a while and then moves over; whatever wanders (a sway, a turn of the head) does so on three
// slow waves that never line up the same way twice; and her eyes rest and then dart, now and then off
// to the side. All of it is a function of the time and a seed: the same seed moves the same, and time
// may be asked about in any order.
public sealed class IdleMotion
{
    // A breath: in, out, a rest before the next one, as shares of its length.
    private const double BreathSeconds = 4.3, Inhale = 0.38, Exhale = 0.47;
    private const double WeightShiftSeconds = 1.4;
    private const double DartSeconds = 0.05;

    // The waves a wander is made of: periods of 17, 9 and 4 seconds, the slow one counting most.
    private static readonly (double Hz, double Share)[] Waves = [(0.06, 0.5), (0.11, 0.3), (0.23, 0.2)];

    private readonly Schedule _breaths, _weight, _gaze;
    private readonly double[] _phases;

    public IdleMotion(int seed)
    {
        var random = new Random(seed);
        _phases = [.. Enumerable.Range(0, 64 * Waves.Length).Select(_ => random.NextDouble() * 2 * Math.PI)];
        _breaths = new Schedule(new Random(random.Next()), r => (BreathSeconds * (0.85 + 0.3 * r.NextDouble()), 0, 0));
        var side = random.Next(2) == 0 ? -1.0 : 1.0;
        _weight = new Schedule(new Random(random.Next()), r =>
        {
            side = -side;
            return (6 + 8 * r.NextDouble(), side * (0.7 + 0.3 * r.NextDouble()), 0);
        });
        var aside = false;
        _gaze = new Schedule(new Random(random.Next()), r =>
        {
            // After a glance aside her eyes come back to the viewer.
            if (!aside && r.NextDouble() < 0.15)
            {
                aside = true;
                var way = r.Next(2) == 0 ? -1 : 1;
                return (0.8 + 0.8 * r.NextDouble(), way * (0.6 + 0.4 * r.NextDouble()), 0.3 * (r.NextDouble() - 0.5));
            }
            aside = false;
            return (0.6 + 2.4 * r.NextDouble(), 0.7 * (r.NextDouble() - 0.5), 0.5 * (r.NextDouble() - 0.5));
        });
    }

    // 0 breathed out, 1 breathed in.
    public double Breath(double t)
    {
        var (start, length, _, _) = _breaths.At(t);
        var u = (t - start) / length;
        if (u < Inhale) return 0.5 - 0.5 * Math.Cos(Math.PI * u / Inhale);
        if (u < Inhale + Exhale) return 0.5 + 0.5 * Math.Cos(Math.PI * (u - Inhale) / Exhale);
        return 0;
    }

    // Which leg carries her: towards -1 her right, towards 1 her left. Held, then moved over in a
    // second and a half, eased at both ends.
    public double Weight(double t)
    {
        var (start, _, to, _) = _weight.At(t);
        var from = _weight.Before(t) is { } before ? before.X : 0;
        var u = Math.Clamp((t - start) / WeightShiftSeconds, 0, 1);
        return from + (to - from) * Ease(u);
    }

    // A slow wander between -1 and 1 for one part of her (`channel`, 0 to 63), on its own waves.
    public double Drift(double t, int channel)
    {
        var sum = 0.0;
        for (var wave = 0; wave < Waves.Length; wave++)
            sum += Waves[wave].Share * Math.Sin(2 * Math.PI * Waves[wave].Hz * t + _phases[(channel & 63) * Waves.Length + wave]);
        return sum;
    }

    // Where her eyes are, as a small offset from the viewer (-1..1 each way; beyond 0.55 across is a
    // glance aside). They rest, then dart to the next place in a twentieth of a second.
    public (double X, double Y) Gaze(double t)
    {
        var (start, _, x, y) = _gaze.At(t);
        var from = _gaze.Before(t) is { } before ? (before.X, before.Y) : (0, 0);
        var u = Math.Clamp((t - start) / DartSeconds, 0, 1);
        if (u >= 1) return (x, y);
        var e = Ease(u);
        return (from.Item1 + (x - from.Item1) * e, from.Item2 + (y - from.Item2) * e);
    }

    private static double Ease(double u) => u * u * (3 - 2 * u);

    // Spans of time one after another, each with a length and a value or two, drawn as far ahead as
    // anyone has asked.
    private sealed class Schedule(Random random, Func<Random, (double Length, double X, double Y)> next)
    {
        private readonly List<(double Start, double Length, double X, double Y)> _spans = [];

        public (double Start, double Length, double X, double Y) At(double t) => _spans[Index(t)];

        public (double Start, double Length, double X, double Y)? Before(double t) => Index(t) is > 0 and var i ? _spans[i - 1] : null;

        private int Index(double t)
        {
            t = Math.Max(0, t);
            while (_spans.Count == 0 || _spans[^1].Start + _spans[^1].Length <= t)
            {
                var start = _spans.Count == 0 ? 0 : _spans[^1].Start + _spans[^1].Length;
                var (length, x, y) = next(random);
                _spans.Add((start, length, x, y));
            }
            var lo = 0;
            var hi = _spans.Count - 1;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                if (_spans[mid].Start <= t) lo = mid;
                else hi = mid - 1;
            }
            return lo;
        }
    }
}
