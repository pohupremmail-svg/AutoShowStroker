using Gooner.Core.Randomness;

namespace Gooner.Core.Track;

// Where the Hypno Ring sits, as fractions of the window. Wandering, it glides to a new spot every
// few seconds and at every change of rhythm - a spring rather than a tween, so a new spot picked
// halfway there bends the path instead of restarting it - and the eye goes with it. Not wandering,
// it glides home to the middle.
public sealed class WanderingFocus(IRandomSource random)
{
    public static readonly (double X, double Y) Home = (0.5, 0.47);

    // Far enough in from the edges for the spiral around it to stay on the picture.
    public const double MinX = 0.25, MaxX = 0.75, MinY = 0.27, MaxY = 0.67;

    private const double Stiffness = 2.2;
    private const double Damping = 0.85;
    private double _vx, _vy, _next;

    public double X { get; private set; } = Home.X;
    public double Y { get; private set; } = Home.Y;
    public double TargetX { get; private set; } = Home.X;
    public double TargetY { get; private set; } = Home.Y;

    // Somewhere new, and the next move a few seconds from now.
    public void MoveOn(double now)
    {
        TargetX = random.Uniform(MinX, MaxX);
        TargetY = random.Uniform(MinY, MaxY);
        _next = now + random.Uniform(5, 9);
    }

    public void Step(double dt, double now, bool wander)
    {
        if (!wander) (TargetX, TargetY) = Home;
        else if (now >= _next) MoveOn(now);
        var c = 2 * Math.Sqrt(Stiffness) * Damping;
        _vx += ((TargetX - X) * Stiffness - _vx * c) * dt;
        _vy += ((TargetY - Y) * Stiffness - _vy * c) * dt;
        X += _vx * dt;
        Y += _vy * dt;
    }
}
