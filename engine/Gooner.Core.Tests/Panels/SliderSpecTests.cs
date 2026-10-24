using Gooner.Core.Panels;

namespace Gooner.Core.Tests.Panels;

// A setting's slider: where a value sits on it, what its badge says, and what a value typed into the
// badge means - the user's design (2026-10-24): sliders everywhere, each with a value one can type.
public sealed class SliderSpecTests
{
    // --- where a value sits ---

    // 0.5 s and 4 s of 0.1-60 s would sit on top of each other on a straight scale: a wide range that
    // starts above zero is logarithmic, one that starts at zero a square root, a narrow one straight.
    [Fact]
    public void a_wide_range_spreads_the_small_values_out()
    {
        var slide = new SliderSpec(0.1, 60, 0.1, ValueUnit.Seconds);

        Assert.Equal(SliderScale.Log, slide.Scale);
        Assert.True(slide.Fraction(4) - slide.Fraction(0.5) > 0.3);
        Assert.Equal(SliderScale.Sqrt, new SliderSpec(0, 900, 10, ValueUnit.Seconds).Scale);
        Assert.Equal(SliderScale.Log, new SliderSpec(0.1, 20, 0.1, ValueUnit.Hertz).Scale);
        Assert.Equal(SliderScale.Linear, new SliderSpec(5, 60, 1, ValueUnit.Seconds).Scale);
    }

    // A chance in steps of a tenth of a percent is usually small; one in whole percent is not.
    [Fact]
    public void a_fine_chance_spreads_its_small_end_and_a_coarse_one_is_straight()
    {
        Assert.Equal(SliderScale.Sqrt, new SliderSpec(0.001, 1, 0.001, ValueUnit.Percent).Scale);
        Assert.Equal(SliderScale.Linear, new SliderSpec(0, 1, 0.01, ValueUnit.Percent).Scale);
    }

    [Theory]
    [InlineData(0.1, 60, 0.1, ValueUnit.Seconds)]
    [InlineData(0, 900, 10, ValueUnit.Seconds)]
    [InlineData(10, 7200, 10, ValueUnit.Duration)]
    [InlineData(0, 1, 0.01, ValueUnit.Percent)]
    [InlineData(0.001, 1, 0.001, ValueUnit.Percent)]
    public void the_ends_are_the_ends_and_every_step_comes_back(double min, double max, double step, ValueUnit unit)
    {
        var slide = new SliderSpec(min, max, step, unit);

        Assert.Equal(0, slide.Fraction(min), 9);
        Assert.Equal(1, slide.Fraction(max), 9);
        foreach (var f in new[] { 0.0, 0.13, 0.5, 0.77, 1.0 })
            Assert.Equal(slide.At(f), slide.At(slide.Fraction(slide.At(f))), 9);
    }

    // A pulled value lands on the setting's own step, inside its bounds.
    [Fact]
    public void a_pulled_value_snaps_to_the_step_inside_the_bounds()
    {
        var slide = new SliderSpec(1, 120, 1, ValueUnit.Seconds);

        Assert.Equal(1, slide.At(0));
        Assert.Equal(120, slide.At(1));
        Assert.Equal(Math.Round(slide.At(0.5)), slide.At(0.5));
        Assert.Equal(5, slide.Snap(4.6));
        Assert.Equal(120, slide.Snap(500));
    }

    // --- what the badge says ---

    [Theory]
    [InlineData(0.1, 60, 0.1, ValueUnit.Seconds, 0.5, "0.5 s")]
    [InlineData(1, 180, 1, ValueUnit.Seconds, 20, "20 s")]
    [InlineData(0.1, 20, 0.1, ValueUnit.Hertz, 5, "5.0 Hz")]
    [InlineData(0, 1, 0.01, ValueUnit.Percent, 0.8, "80 %")]
    [InlineData(0.001, 1, 0.001, ValueUnit.Percent, 0.055, "5.5 %")]
    [InlineData(0.001, 1, 0.001, ValueUnit.Percent, 0.05, "5 %")]
    [InlineData(10, 7200, 10, ValueUnit.Duration, 450, "7:30 min")]
    [InlineData(10, 7200, 10, ValueUnit.Duration, 40, "40 s")]
    [InlineData(10, 7200, 10, ValueUnit.Duration, 3900, "1:05:00 h")]
    public void a_value_is_written_with_its_unit(double min, double max, double step, ValueUnit unit, double value, string text) =>
        Assert.Equal(text, new SliderSpec(min, max, step, unit).Text(value));

    // A range names its unit once - unless the two ends need different ones.
    [Fact]
    public void a_range_names_its_unit_once()
    {
        Assert.Equal("0.5 – 4.0 s", new SliderSpec(0.1, 60, 0.1, ValueUnit.Seconds).RangeText(0.5, 4));
        Assert.Equal("5:00 – 15:00 min", new SliderSpec(10, 7200, 10, ValueUnit.Duration).RangeText(300, 900));
        Assert.Equal("40 s – 2:00 min", new SliderSpec(10, 7200, 10, ValueUnit.Duration).RangeText(40, 120));
    }

    // --- what is typed ---

    [Theory]
    [InlineData(ValueUnit.Seconds, "4", 4)]
    [InlineData(ValueUnit.Seconds, "4.5 s", 4.5)]
    [InlineData(ValueUnit.Seconds, "4,5", 4.5)]
    [InlineData(ValueUnit.Percent, "20 %", 0.2)]
    [InlineData(ValueUnit.Percent, "20", 0.2)]
    [InlineData(ValueUnit.Hertz, " 2.5Hz ", 2.5)]
    [InlineData(ValueUnit.Duration, "7:30", 450)]
    [InlineData(ValueUnit.Duration, "7:30 min", 450)]
    [InlineData(ValueUnit.Duration, "5 min", 300)]
    [InlineData(ValueUnit.Duration, "90 s", 90)]
    [InlineData(ValueUnit.Duration, "90", 90)]
    [InlineData(ValueUnit.Duration, "1:05:00", 3900)]
    public void a_typed_value_is_read_as_its_unit_says(ValueUnit unit, string typed, double value) =>
        Assert.Equal(value, new SliderSpec(0, 10000, 0.001, unit).Parse(typed)!.Value, 9);

    // Typed beyond the slider or between its steps: held to it, as a pull would be.
    [Fact]
    public void a_typed_value_is_held_to_the_slider()
    {
        var slide = new SliderSpec(1, 180, 1, ValueUnit.Seconds);

        Assert.Equal(180, slide.Parse("999"));
        Assert.Equal(13, slide.Parse("12.6"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("lots")]
    [InlineData("5:xx")]
    public void what_is_not_a_value_is_refused(string typed) =>
        Assert.Null(new SliderSpec(0, 100, 1, ValueUnit.Seconds).Parse(typed));

    [Fact]
    public void a_typed_range_is_read_whichever_way_round_and_dash()
    {
        var slide = new SliderSpec(0.1, 20, 0.1, ValueUnit.Hertz);

        Assert.Equal((2, 8), slide.ParseRange("2 – 8 Hz"));
        Assert.Equal((2, 8), slide.ParseRange("8-2"));
        Assert.Equal((2, 8), slide.ParseRange("2 to 8"));
        Assert.Equal((300, 900), new SliderSpec(10, 7200, 10, ValueUnit.Duration).ParseRange("5:00 – 15:00 min"));
        Assert.Equal((300, 900), new SliderSpec(10, 7200, 10, ValueUnit.Duration).ParseRange("5 - 15 min"));
        Assert.Null(slide.ParseRange("2"));
        Assert.Null(slide.ParseRange("a - b"));
    }
}
