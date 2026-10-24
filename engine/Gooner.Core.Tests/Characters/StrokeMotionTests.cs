using Gooner.Core.Characters;

namespace Gooner.Core.Tests.Characters;

// Her hand on the toy: down on each note, up near the tip between two, one stroke per note. A fast
// beat gets a shorter stroke, kept at the top, so that the hand never covers so much from one frame to
// the next that it seems to jump; how deep a note goes is settled before the hand gets there.
public sealed class StrokeMotionTests
{
    private const double Frame = 1.0 / 60;

    [Fact]
    public void on_a_note_of_a_slow_beat_the_hand_is_all_the_way_down()
    {
        Assert.Equal(1, StrokeMotion.Down(10, before: 9, last: 10, next: 11, after: 12)!.Value, 6);
    }

    [Fact]
    public void halfway_between_two_notes_it_is_at_the_top()
    {
        Assert.Equal(0, StrokeMotion.Down(10.5, before: 9, last: 10, next: 11, after: 12)!.Value, 6);
    }

    [Fact]
    public void a_fast_beat_gets_a_shorter_stroke()
    {
        // Five notes a second: two fifths of the whole stroke, from the top down.
        Assert.Equal(0.4, StrokeMotion.Down(10, before: 9.8, last: 10, next: 10.2, after: 10.4)!.Value, 6);
        Assert.Equal(0, StrokeMotion.Down(10.1, before: 9.8, last: 10, next: 10.2, after: 10.4)!.Value, 6);
    }

    [Fact]
    public void however_fast_the_beat_there_is_still_a_stroke()
    {
        Assert.Equal(StrokeMotion.ShortestStroke, StrokeMotion.Down(10, before: 9.95, last: 10, next: 10.05, after: 10.1)!.Value, 6);
    }

    [Fact]
    public void the_last_slow_note_before_a_fast_run_already_stops_short()
    {
        // A second's gap behind it, a fifth of one ahead: the hand comes down only as far as the fast
        // stroke after it goes, so it is up there when the run begins rather than climbing to it.
        Assert.Equal(0.4, StrokeMotion.Down(10, before: 9, last: 10, next: 10.2, after: 10.4)!.Value, 6);
        // And on the way down to it, from the top of the slow stroke.
        Assert.Equal(0.4, StrokeMotion.Down(10 - 1e-9, before: 8, last: 9, next: 10, after: 10.2)!.Value, 6);
    }

    [Fact]
    public void the_hand_never_jumps_at_a_note()
    {
        // The end of one gap and the start of the next are the same place, whatever the two gaps.
        foreach (var (a, b, c, d) in new[] { (9.0, 10.0, 10.2, 10.4), (9.8, 10.0, 11.0, 12.0), (9.0, 10.0, 10.07, 11.0) })
        {
            var arriving = StrokeMotion.Down(c - 1e-9, before: a, last: b, next: c, after: d)!.Value;
            var leaving = StrokeMotion.Down(c, before: b, last: c, next: d, after: d + 1)!.Value;
            Assert.Equal(arriving, leaving, 6);
        }
    }

    [Fact]
    public void from_one_frame_to_the_next_the_hand_moves_a_tenth_of_the_stroke_at_most()
    {
        foreach (var gap in new[] { 0.1, 0.15, 0.2, 0.3, 0.5, 0.8, 1.5 })
        {
            var most = 0.0;
            for (var t = 10.0; t + Frame < 10 + gap; t += Frame / 4)
            {
                var now = StrokeMotion.Down(t, before: 10 - gap, last: 10, next: 10 + gap, after: 10 + 2 * gap)!.Value;
                var then = StrokeMotion.Down(t + Frame, before: 10 - gap, last: 10, next: 10 + gap, after: 10 + 2 * gap)!.Value;
                most = Math.Max(most, Math.Abs(then - now));
            }
            Assert.True(most <= 0.11, $"{gap} s between notes: {most:F3} of the stroke in one frame");
        }
    }

    [Fact]
    public void a_pause_is_no_stroke()
    {
        Assert.Null(StrokeMotion.Down(10, before: 9, last: 10, next: 10 + StrokeMotion.PauseGap + 1, after: null));
    }

    [Fact]
    public void without_a_note_on_either_side_there_is_no_stroke()
    {
        Assert.Null(StrokeMotion.Down(10, before: null, last: null, next: 11, after: 12));
        Assert.Null(StrokeMotion.Down(10, before: 9, last: 10, next: null, after: null));
    }

    [Fact]
    public void a_pause_next_to_a_note_does_not_shorten_its_stroke()
    {
        // The gap into a pause is no stroke at all; the note before it goes as deep as its other side.
        Assert.Equal(1, StrokeMotion.Down(11 - 1e-9, before: 9, last: 10, next: 11, after: 20)!.Value, 6);
    }

    [Fact]
    public void a_note_with_nothing_known_beyond_it_goes_as_deep_as_the_gap_it_closes()
    {
        Assert.Equal(0.4, StrokeMotion.Down(10.2 - 1e-9, before: null, last: 10, next: 10.2, after: null)!.Value, 6);
    }
}
