namespace Gooner.Core.Tests.Support;

// The C# side of monkeypatching time.time: a clock that stands wherever the test puts it, in
// seconds since the Unix epoch.
internal sealed class FakeTime : TimeProvider
{
    public double Now { get; set; } = 1_000_000.0;

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddSeconds(Now);

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
