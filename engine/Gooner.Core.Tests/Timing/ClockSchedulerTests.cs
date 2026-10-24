using Gooner.Core.Timing;

namespace Gooner.Core.Tests.Timing;

// What Qt's event loop did for the timers, minus the loop. One rule is the loop's own: a timer that
// is due again at the very moment it fired - a 0 ms interval - goes off once per pass, as a 0 ms
// QTimer goes off once per pass of the event loop. Without a loop to pass, Advance() is the pass;
// firing it again within the same one never got any further in time, and froze the app (a saved
// session with a freq of 5000 has a note gap of 0 ms).
public class ClockSchedulerTests
{
    private readonly FrameClock _clock = new(1000.0);
    private readonly ClockScheduler _scheduler;

    public ClockSchedulerTests() => _scheduler = new ClockScheduler(_clock);

    [Fact]
    public void a_repeating_timer_of_0_ms_fires_once_per_advance()
    {
        var fired = 0;
        var timer = _scheduler.CreateTimer(() => fired++);
        timer.Start(0);

        _scheduler.Advance();
        Assert.Equal(1, fired);

        _clock.Now += 0.016;
        _scheduler.Advance();
        Assert.Equal(2, fired);
    }

    [Fact]
    public void a_single_shot_restarted_at_0_ms_from_its_own_slot_fires_once_per_advance()
    {
        var fired = 0;
        ClockTimer? timer = null;
        timer = _scheduler.CreateTimer(() =>
        {
            fired++;
            timer!.Start(0);
        }, singleShot: true);
        timer.Start(0);

        _scheduler.Advance();
        Assert.Equal(1, fired);
        Assert.True(timer.Active);

        _clock.Now += 0.016;
        _scheduler.Advance();
        Assert.Equal(2, fired);
    }

    [Fact]
    public void a_timer_started_in_the_past_from_a_slot_waits_for_the_next_advance()
    {
        var fired = 0;
        var late = _scheduler.CreateTimer(() => fired++, singleShot: true);
        var first = _scheduler.CreateTimer(() => late.Start(-50), singleShot: true);
        first.Start(0);

        _scheduler.Advance();
        Assert.Equal(0, fired);

        _scheduler.Advance();
        Assert.Equal(1, fired);
    }

    // The rule is about time standing still, nothing else: a timer of one millisecond still catches
    // up on every tick a long frame held back, each at the moment it was due.
    [Fact]
    public void a_short_interval_still_catches_up_within_one_advance()
    {
        var at = new List<double>();
        var timer = _scheduler.CreateTimer(() => at.Add(_scheduler.Now));
        timer.Start(1);

        _clock.Now += 0.0105;
        _scheduler.Advance();

        Assert.Equal(10, at.Count);
        Assert.Equal(1000.010, at[^1], 9);
    }

    [Fact]
    public void a_0_ms_timer_started_before_the_advance_fires_in_it()
    {
        var fired = 0;
        var timer = _scheduler.CreateTimer(() => fired++, singleShot: true);
        timer.Start(0);

        _scheduler.Advance();

        Assert.Equal(1, fired);
        Assert.False(timer.Active);
    }

    // Due order is untouched by it: what came due first still fires first.
    [Fact]
    public void timers_fire_in_the_order_they_came_due()
    {
        var order = new List<string>();
        var b = _scheduler.CreateTimer(() => order.Add("b"), singleShot: true);
        var a = _scheduler.CreateTimer(() => order.Add("a"), singleShot: true);
        b.Start(5);
        a.Start(2);

        _clock.Now += 0.01;
        _scheduler.Advance();

        Assert.Equal(["a", "b"], order);
    }
}
