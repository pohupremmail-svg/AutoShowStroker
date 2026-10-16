namespace Gooner.Core.Timing;

// A clock that stands wherever it is put, in seconds since the Unix epoch - for everything that
// reads the time, the session clock and the TimeProviders alike. The host sets it once a frame: the
// headless session to wherever its simulation has got to, the engine to the picture clock it
// derives from the audio. Read twice within one frame it answers the same both times, which is
// what lets a prediction taken off it be turned back into absolute times.
public sealed class FrameClock(double start) : TimeProvider, ISessionClock
{
    public double Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddSeconds(Now);
}
