namespace Gooner.Core.Timing;

// The clock the rhythm runs on, in seconds. In the engine it is the one derived from the audio
// output, so a note lands on the click it belongs to; anywhere else it is the wall clock. A double
// rather than a TimeProvider on purpose: those count in 100 ns ticks, and a rhythm scheduled in
// whole milliseconds on top of them comes back a tick off.
public interface ISessionClock
{
    double Now { get; }
}

// Seconds since the Unix epoch - what Python's time.time() answers.
public sealed class WallClock(TimeProvider? time = null) : ISessionClock
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public double Now => (_time.GetUtcNow() - DateTimeOffset.UnixEpoch).TotalSeconds;
}
