using Gooner.Core.Formatting;

namespace Gooner.Core.Tests.Formatting;

// Port of the format_* cases in tests/test_utils.py.
public class ClockTests
{
    [Theory]
    [InlineData(null, "N/A")]
    [InlineData(0.0, "0s")]
    [InlineData(45.0, "45s")]
    [InlineData(60.0, "1 Min")]
    [InlineData(125.0, "2 Min 5s")]
    // Python's round() rounds half to even; the port has to as well.
    [InlineData(59.5, "1 Min")]
    [InlineData(0.5, "0s")]
    public void format_duration(double? seconds, string expected) => Assert.Equal(expected, Clock.FormatDuration(seconds));

    [Theory]
    [InlineData(null, "N/A")]
    [InlineData(0.0, "00:00")]
    [InlineData(42.0, "00:42")]
    [InlineData(522.0, "08:42")]
    [InlineData(522.9, "08:42")]
    [InlineData(3600.0, "1:00:00")]
    [InlineData(3735.0, "1:02:15")]
    public void format_clock(double? seconds, string expected) => Assert.Equal(expected, Clock.FormatClock(seconds));
}
