using Gooner.Core.Randomness;

namespace Gooner.Core.Track;

// Where the Hypno Ring sits, as fractions of the window. Wandering, it drifts across the picture all
// the time - slowly, in smooth curves that never repeat quite the same way - and the eye goes with
// it: a path of two slow waves a side, their speeds and starting points drawn once, so every session
// takes its own route. It follows the path on a spring, so switching wandering on or off glides
// rather than jumps. Not wandering, it glides home to the middle.
public sealed class WanderingFocus
{
    public static readonly (double X, double Y) Home = (0.5, 0.47);

    // Far enough in from the edges for the spiral around it to stay on the picture.
    public const double MinX = 0.25, MaxX = 0.75, MinY = 0.28, MaxY = 0.66;

    private const double Stiffness = 4.0;
    private const double Damping = 0.9;
    private readonly double[] _phase = new double[4];
    private readonly double[] _speed = new double[4];
    private double _vx, _vy;

    public WanderingFocus(IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        for (var i = 0; i < 4; i++) _phase[i] = random.Uniform(0, 2 * Math.PI);
        // Cycles a second: one slow sweep across, one quicker sway on top of it.
        _speed[0] = random.Uniform(0.045, 0.065);
        _speed[1] = random.Uniform(0.11, 0.15);
        _speed[2] = random.Uniform(0.035, 0.055);
        _speed[3] = random.Uniform(0.09, 0.13);
    }

    public double X { get; private set; } = Home.X;
    public double Y { get; private set; } = Home.Y;

    // Where the path is at a moment of the session clock: always inside the bounds.
    public (double X, double Y) PathAt(double time)
    {
        double Wave(int slow, int quick) =>
            0.65 * Math.Sin(2 * Math.PI * _speed[slow] * time + _phase[slow]) + 0.35 * Math.Sin(2 * Math.PI * _speed[quick] * time + _phase[quick]);
        return ((MinX + MaxX) / 2 + (MaxX - MinX) / 2 * Wave(0, 1), (MinY + MaxY) / 2 + (MaxY - MinY) / 2 * Wave(2, 3));
    }

    public void Step(double dt, double now, bool wander)
    {
        var (tx, ty) = wander ? PathAt(now) : Home;
        var c = 2 * Math.Sqrt(Stiffness) * Damping;
        _vx += ((tx - X) * Stiffness - _vx * c) * dt;
        _vy += ((ty - Y) * Stiffness - _vy * c) * dt;
        X += _vx * dt;
        Y += _vy * dt;
    }
}
