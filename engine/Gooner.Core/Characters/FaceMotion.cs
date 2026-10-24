namespace Gooner.Core.Characters;

// Her face while nothing in particular is said - never blank (the user found it "tot", "lustlos"). A
// smile that never quite goes and drifts a little; every few seconds something small crosses her face
// (a smile breaking through, raised brows, a contented look); and while she strokes in the frame
// (`aroused`), heavier lids, lips parted and breathing, and now and then a wink. Weights of VRM preset
// expressions, laid under whatever the callout asks of her face. A function of the time and a seed.
public sealed class FaceMotion
{
    private readonly IdleMotion _drift;
    private readonly List<(double At, double Length, string Expression, double Strength)> _flickers = [];
    private readonly List<(double At, string Eye)> _winks = [];
    private readonly Random _flickerDice, _winkDice;

    public FaceMotion(int seed)
    {
        var random = new Random(seed);
        _drift = new IdleMotion(random.Next());
        _flickerDice = new Random(random.Next());
        _winkDice = new Random(random.Next());
    }

    // When something crosses her face, up to `seconds`.
    public IReadOnlyList<double> Flickers(double seconds)
    {
        Reach(seconds);
        return [.. _flickers.Where(f => f.At < seconds).Select(f => f.At)];
    }

    public Dictionary<string, double> At(double t, double breath, double aroused)
    {
        Reach(t + 10);
        var face = new Dictionary<string, double>
        {
            ["happy"] = 0.18 + 0.1 * _drift.Drift(t, 0),
            ["blink"] = 0.05 + 0.03 * _drift.Drift(t, 1) + 0.17 * aroused,
            ["aa"] = 0.03 + 0.02 * _drift.Drift(t, 2) + aroused * (0.06 + 0.1 * breath),
        };
        foreach (var (at, length, expression, strength) in _flickers)
        {
            if (t < at || t > at + length) continue;
            // In over a quarter of it, held, out over the last third.
            var u = (t - at) / length;
            var shape = u < 0.25 ? Ease(u / 0.25) : u > 0.67 ? Ease((1 - u) / 0.33) : 1;
            face[expression] = face.GetValueOrDefault(expression) + strength * shape;
        }
        if (aroused > 0.5)
            foreach (var (at, eye) in _winks)
                if (t >= at && t <= at + 0.4)
                    face[eye] = Math.Sin(Math.PI * (t - at) / 0.4);
        foreach (var name in face.Keys.ToList()) face[name] = Math.Clamp(face[name], 0, 1);
        return face;
    }

    private void Reach(double t)
    {
        while (_flickers.Count == 0 || _flickers[^1].At < t)
        {
            var at = (_flickers.Count == 0 ? 0 : _flickers[^1].At) + 2.5 + 4.5 * _flickerDice.NextDouble();
            _flickers.Add(_flickerDice.Next(3) switch
            {
                0 => (at, 0.8 + 0.8 * _flickerDice.NextDouble(), "happy", 0.25 + 0.2 * _flickerDice.NextDouble()),
                1 => (at, 0.4 + 0.3 * _flickerDice.NextDouble(), "surprised", 0.12 + 0.1 * _flickerDice.NextDouble()),
                _ => (at, 1.0 + 1.0 * _flickerDice.NextDouble(), "relaxed", 0.3 + 0.2 * _flickerDice.NextDouble()),
            });
        }
        while (_winks.Count == 0 || _winks[^1].At < t)
            _winks.Add(((_winks.Count == 0 ? 0 : _winks[^1].At) + 15 + 25 * _winkDice.NextDouble(), _winkDice.Next(2) == 0 ? "blinkLeft" : "blinkRight"));
    }

    private static double Ease(double u) => u * u * (3 - 2 * u);
}
