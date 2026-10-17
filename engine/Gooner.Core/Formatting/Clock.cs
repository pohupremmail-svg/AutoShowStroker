using System.Globalization;
using Gooner.Core.Localization;

namespace Gooner.Core.Formatting;

// Port of format_clock / format_duration in src/utils.py.
public static class Clock
{
    // 08:42, or 1:02:15 past the hour. Truncates, like int() on the Python side.
    public static string FormatClock(double? seconds)
    {
        if (seconds is null) return L10n.T("N/A");
        var total = (long)Math.Truncate(seconds.Value);
        var hours = total / 3600;
        var minutes = total % 3600 / 60;
        var secs = total % 60;
        return hours != 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{secs:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes:00}:{secs:00}");
    }

    // 45s, 1 Min, 2 Min 5s. Rounds half to even, as Python's round() does.
    public static string FormatDuration(double? seconds)
    {
        if (seconds is null) return L10n.T("N/A");
        var total = (long)Math.Round(seconds.Value, MidpointRounding.ToEven);
        if (total < 60) return L10n.F("{0}s", total);
        var minutes = total / 60;
        var secs = total % 60;
        return secs == 0 ? L10n.F("{0} Min", minutes) : L10n.F("{0} Min {1}s", minutes, secs);
    }
}
