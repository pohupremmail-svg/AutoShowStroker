using System.Globalization;
using System.Text.RegularExpressions;

namespace Gooner.Core.Panels;

// What a setting's number is, for its slider's badge: seconds, beats a second, a share shown as a
// percentage, or a stretch of time long enough to read as minutes.
public enum ValueUnit
{
    Plain,
    Seconds,
    Hertz,
    Percent,
    Duration,
}

public enum SliderScale
{
    Linear,
    Log,
    Sqrt,
}

// A setting's slider (the user's design, 2026-10-24: sliders everywhere, each with a value one can
// type): where a value sits on it, what its badge says, and what a value typed into the badge means.
// Numbers are written with a decimal point in every language, as everywhere in the app; a typed
// comma is read as one.
public sealed partial class SliderSpec(double min, double max, double step, ValueUnit unit)
{
    public double Min { get; } = min;

    public double Max { get; } = max;

    public double Step { get; } = step;

    public ValueUnit Unit { get; } = unit;

    // 0.5 s and 4 s of 0.1-60 s would sit on top of each other on a straight scale: a wide range that
    // starts above zero is logarithmic, one that starts at zero a square root. A share in tenths of a
    // percent is usually small, so its small end is spread out too.
    public SliderScale Scale { get; } = unit == ValueUnit.Percent
        ? step < 0.01 ? SliderScale.Sqrt : SliderScale.Linear
        : min > 0 && max / min < 20 && max < 100 ? SliderScale.Linear
        : min > 0 ? SliderScale.Log
        : max < 100 ? SliderScale.Linear : SliderScale.Sqrt;

    private int Decimals => Step >= 1 ? 0 : (int)Math.Ceiling(-Math.Log10(Step) - 1e-9);

    // Where a value sits, 0 at the left end and 1 at the right.
    public double Fraction(double value)
    {
        var v = Math.Clamp(value, Min, Max);
        if (Max <= Min) return 0;
        return Scale switch
        {
            SliderScale.Log => Math.Log(v / Min) / Math.Log(Max / Min),
            SliderScale.Sqrt => Math.Sqrt((v - Min) / (Max - Min)),
            _ => (v - Min) / (Max - Min),
        };
    }

    // The value at a point of the slider, on the setting's own step.
    public double At(double fraction)
    {
        var f = Math.Clamp(fraction, 0, 1);
        return Snap(Scale switch
        {
            SliderScale.Log => Min * Math.Pow(Max / Min, f),
            SliderScale.Sqrt => Min + f * f * (Max - Min),
            _ => Min + f * (Max - Min),
        });
    }

    // On the step, inside the bounds.
    public double Snap(double value) =>
        Math.Clamp(Math.Round(Math.Round(value / Step) * Step, Math.Max(Decimals, 0) + 6), Min, Max);

    public string Text(double value) => Number(value) is var (number, symbol) ? $"{number} {symbol}".TrimEnd() : "";

    // A range names its unit once, unless its ends need different ones.
    public string RangeText(double low, double high)
    {
        var (a, unitA) = Number(low);
        var (b, unitB) = Number(high);
        return unitA == unitB ? $"{a} – {b} {unitB}".TrimEnd() : $"{Text(low)} – {Text(high)}";
    }

    private (string Number, string Symbol) Number(double value)
    {
        var inv = CultureInfo.InvariantCulture;
        switch (Unit)
        {
            case ValueUnit.Percent:
                var percent = value * 100;
                var places = Math.Max(0, Decimals - 2);
                var text = percent.ToString("F" + places, inv);
                if (text.Contains('.', StringComparison.Ordinal)) text = text.TrimEnd('0').TrimEnd('.');
                return (text, "%");
            case ValueUnit.Duration:
                var seconds = (long)Math.Round(value);
                if (seconds < 60) return (seconds.ToString(inv), "s");
                if (seconds < 3600) return ($"{seconds / 60}:{seconds % 60:00}", "min");
                return ($"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}", "h");
            default:
                return (value.ToString("F" + Decimals, inv), Unit switch
                {
                    ValueUnit.Seconds => "s",
                    ValueUnit.Hertz => "Hz",
                    _ => "",
                });
        }
    }

    // A typed value, held to the slider; null for what is not one.
    public double? Parse(string typed) => Read(typed, null) is { } value ? Snap(value) : null;

    // A typed range - "2 – 8 Hz", "8-2", "5 to 15 min" - low first; null unless it is two values. A
    // unit written once at the end goes for both.
    public (double Low, double High)? ParseRange(string typed)
    {
        ArgumentNullException.ThrowIfNull(typed);
        var parts = RangeSplit().Split(typed.Trim());
        if (parts.Length != 2) return null;
        var trailing = TrailingUnit().Match(parts[1]) is { Success: true } m ? m.Groups[1].Value : null;
        if (Read(parts[0], trailing) is not { } a || Read(parts[1], null) is not { } b) return null;
        return (Snap(Math.Min(a, b)), Snap(Math.Max(a, b)));
    }

    private double? Read(string typed, string? impliedUnit)
    {
        var text = typed.Trim().Replace(',', '.').ToLowerInvariant();
        if (text.Length == 0) return null;
        var inv = CultureInfo.InvariantCulture;
        if (Unit == ValueUnit.Duration && Clock().Match(text) is { Success: true } clock)
        {
            var first = double.Parse(clock.Groups[1].Value, inv);
            var second = double.Parse(clock.Groups[2].Value, inv);
            return clock.Groups[3].Success ? first * 3600 + second * 60 + double.Parse(clock.Groups[3].Value, inv) : first * 60 + second;
        }
        if (Amount().Match(text) is not { Success: true } amount) return null;
        var number = double.Parse(amount.Groups[1].Value, inv);
        var symbol = amount.Groups[2].Value.Length > 0 ? amount.Groups[2].Value : impliedUnit ?? "";
        return Unit switch
        {
            ValueUnit.Percent => number / 100,
            ValueUnit.Duration => symbol switch { "min" => number * 60, "h" => number * 3600, _ => number },
            _ => number,
        };
    }

    [GeneratedRegex(@"\s*(?:–|—|-|\bto\b)\s*")]
    private static partial Regex RangeSplit();

    [GeneratedRegex(@"(min|h|s|hz|%)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingUnit();

    [GeneratedRegex(@"^(\d+):(\d{1,2})(?::(\d{1,2}))?\s*(?:min|h)?$")]
    private static partial Regex Clock();

    [GeneratedRegex(@"^(\d+(?:\.\d+)?)\s*(min|h|s|hz|%)?$")]
    private static partial Regex Amount();
}
