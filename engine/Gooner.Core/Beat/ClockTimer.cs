namespace Gooner.Core.Beat;

// What a QTimer was on the Python side, minus the event loop: it holds a due time on the session
// clock, and whoever drives the clock (BeatHandler.Advance) fires it. Repeats unless the handler
// restarts or stops it, as a QTimer does.
internal sealed class ClockTimer(Func<double> now)
{
    public bool Active { get; private set; }

    public double Due { get; private set; }

    public int IntervalMs { get; private set; }

    // Bumped by every Start/Stop, so the driver can tell whether a callback re-armed the timer.
    public int Generation { get; private set; }

    public void Start(int intervalMs)
    {
        IntervalMs = intervalMs;
        Due = now() + intervalMs / 1000.0;
        Active = true;
        Generation++;
    }

    public void Stop()
    {
        Active = false;
        Generation++;
    }

    // Milliseconds until it fires, -1 while stopped - QTimer.remainingTime().
    public double RemainingMs => Active ? Math.Max(0, (Due - now()) * 1000) : -1;

    internal void Repeat() => Due += IntervalMs / 1000.0;
}
