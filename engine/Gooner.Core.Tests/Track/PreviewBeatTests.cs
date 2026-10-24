using Gooner.Core.Track;

namespace Gooner.Core.Tests.Track;

// The beat the Look tab's live preview plays: no session behind it, so a steady rhythm of its own,
// handed to the real note track as BeatHandler.Forecast would hand it.
public sealed class PreviewBeatTests
{
    [Fact]
    public void notes_come_on_a_steady_beat_up_to_the_horizon()
    {
        var forecast = PreviewBeat.Forecast(10.1, 2.5);

        Assert.NotEmpty(forecast.Notes);
        Assert.All(forecast.Notes, n => Assert.InRange(n.At, 10.1, 12.6));
        var gaps = forecast.Notes.Zip(forecast.Notes.Skip(1), (a, b) => b.At - a.At).ToList();
        Assert.All(gaps, gap => Assert.Equal(PreviewBeat.Gap, gap, 9));
        Assert.All(forecast.Notes, n => Assert.True(n.Audible));
    }

    // Always a rhythm on the air - the track draws nothing without one.
    [Fact]
    public void a_rhythm_is_always_on_the_air()
    {
        var span = Assert.Single(PreviewBeat.Forecast(42, 2.5).Spans);

        Assert.Equal("rhythm", span.Kind);
        Assert.True(span.Start <= 42 && span.End > 42 + 2.5);
    }

    // The same moment gives the same notes, so a note keeps its place from one frame to the next.
    [Fact]
    public void the_notes_do_not_move_between_frames()
    {
        var first = PreviewBeat.Forecast(5, 2.5).Notes.Select(n => n.At).ToList();
        var later = PreviewBeat.Forecast(5.2, 2.5).Notes.Select(n => n.At).ToList();

        Assert.Equal(first.Where(at => at >= 5.2), later.Where(at => at <= first[^1]));
    }
}
