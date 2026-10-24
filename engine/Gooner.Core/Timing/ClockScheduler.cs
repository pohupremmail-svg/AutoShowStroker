namespace Gooner.Core.Timing;

// What Qt's event loop did for the timers on the Python side, minus the loop: every ClockTimer
// of a session lives here, and Advance() fires whatever has come due - across all of them, in
// the order they were due (a tie goes to the timer created first), each at the moment it was due.
// One scheduler is shared by everything on the session clock, so the climax can never be overtaken
// by notes that were due after it just because their handler was advanced first. The host calls
// Advance() every frame.
//
// Each Advance() is one pass of the event loop Qt had. A timer armed while one fires that is due at
// or before that very moment - a 0 ms interval, or a time already past - waits for the next pass, as
// a 0 ms QTimer goes off once per pass of the loop: fired again in the same one, time never moved on
// and the app froze (a saved session with a freq of 5000 has a note gap of 0 ms).
public sealed class ClockScheduler(ISessionClock clock)
{
    private readonly List<ClockTimer> _timers = [];
    private double? _dispatchAt;
    private long _pass;

    // The moment the firing timer was due while it fires; the clock itself otherwise.
    public double Now => _dispatchAt ?? clock.Now;

    // The pass a timer armed now may still fire in: this one, unless it was armed for a moment the
    // pass has already reached.
    internal long PassFor(double due) => _dispatchAt is { } at && due <= at ? _pass + 1 : _pass;

    public ClockTimer CreateTimer(Action timeout, bool singleShot = false)
    {
        var timer = new ClockTimer(this, timeout, singleShot);
        _timers.Add(timer);
        return timer;
    }

    internal void Remove(ClockTimer timer) => _timers.Remove(timer);

    public void Advance()
    {
        var until = clock.Now;
        _pass++;
        while (true)
        {
            ClockTimer? next = null;
            foreach (var timer in _timers)
                if (timer.Active && timer.Due <= until && timer.Pass <= _pass && (next is null || timer.Due < next.Due))
                    next = timer;
            if (next is null) return;

            _dispatchAt = next.Due;
            try
            {
                // A single-shot QTimer has stopped by the time it signals.
                if (next.SingleShot) next.Stop();
                var generation = next.Generation;
                next.Fire();
                // A repeating one goes again unless the slot restarted or stopped it.
                if (!next.SingleShot && next.Active && next.Generation == generation)
                    next.Repeat();
            }
            finally
            {
                _dispatchAt = null;
            }
        }
    }
}

// A QTimer on the session clock: a due time, fired by the ClockScheduler it belongs to.
public sealed class ClockTimer
{
    private readonly ClockScheduler _scheduler;
    private readonly Action _timeout;

    internal ClockTimer(ClockScheduler scheduler, Action timeout, bool singleShot)
    {
        _scheduler = scheduler;
        _timeout = timeout;
        SingleShot = singleShot;
    }

    public bool SingleShot { get; }

    public bool Active { get; private set; }

    public double Due { get; private set; }

    public double IntervalMs { get; private set; }

    // Bumped by every Start/Stop, so the scheduler can tell whether a slot re-armed the timer.
    internal int Generation { get; private set; }

    // The scheduler's pass this timer may fire in at the earliest.
    internal long Pass { get; private set; }

    public void Start(double intervalMs)
    {
        IntervalMs = intervalMs;
        Due = _scheduler.Now + intervalMs / 1000.0;
        Pass = _scheduler.PassFor(Due);
        Active = true;
        Generation++;
    }

    public void Stop()
    {
        Active = false;
        Generation++;
    }

    // Milliseconds until it fires, -1 while stopped - QTimer.remainingTime(), but not rounded to
    // whole milliseconds, so what is drawn from it moves smoothly.
    public double RemainingMs => Active ? Math.Max(0, (Due - _scheduler.Now) * 1000) : -1;

    // Stops it and takes it off the scheduler for good.
    public void Release()
    {
        Stop();
        _scheduler.Remove(this);
    }

    internal void Fire() => _timeout();

    internal void Repeat()
    {
        Due += IntervalMs / 1000.0;
        Pass = _scheduler.PassFor(Due);
    }
}
