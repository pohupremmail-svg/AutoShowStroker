using Gooner.Core.Timing;

namespace Gooner.Core.Tests.Support;

// The C# side of monkeypatching time.time: a clock that stands wherever the test puts it, in
// seconds since the Unix epoch. Serves as a TimeProvider and as a session clock alike.
internal sealed class FakeTime : TimeProvider, ISessionClock
{
    public double Now { get; set; } = 1_000_000.0;

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddSeconds(Now);

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
