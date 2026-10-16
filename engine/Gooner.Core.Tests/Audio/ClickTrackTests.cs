using Gooner.Core.Audio;

namespace Gooner.Core.Tests.Audio;

// The click, mixed into the audio stream at the exact sample its note is due on. The main thread
// hands over what the rhythm is about to play; the audio thread mixes it block by block, ahead of
// what is being heard. On the Python side the click played whenever a QTimer got round to it, on
// the same thread that painted.
public class ClickTrackTests
{
    // 1 kHz keeps the arithmetic readable: one frame is one millisecond.
    private const int Rate = 1000;
    private const double Origin = 100.0;

    // Mono and short, so where it lands can be read off directly.
    private static readonly float[] Click = [1.0f, 0.5f, 0.25f];

    private static ClickTrack Track() => new(ClickTrack.Stereo(Click), Rate, Origin);

    private static float[] Left(ClickTrack track, int frames)
    {
        var block = new float[frames * 2];
        track.Mix(block);
        return [.. Enumerable.Range(0, frames).Select(i => block[i * 2])];
    }

    [Fact]
    public void a_click_starts_on_the_frame_its_note_is_due()
    {
        var track = Track();
        track.Schedule([100.010]);

        var left = Left(track, 20);

        Assert.Equal([1.0f, 0.5f, 0.25f], left[10..13]);
        Assert.All(left[..10], s => Assert.Equal(0f, s));
        Assert.All(left[13..], s => Assert.Equal(0f, s));
    }

    [Fact]
    public void both_channels_carry_it()
    {
        var track = Track();
        track.Schedule([100.002]);
        var block = new float[10 * 2];

        track.Mix(block);

        Assert.Equal(1.0f, block[4]);
        Assert.Equal(1.0f, block[5]);
    }

    [Fact]
    public void a_click_carries_on_into_the_next_block()
    {
        var track = Track();
        track.Schedule([100.009]);

        var first = Left(track, 10);
        var second = Left(track, 10);

        Assert.Equal(1.0f, first[9]);
        Assert.Equal([0.5f, 0.25f], second[..2]);
    }

    [Fact]
    public void clicks_that_overlap_are_added()
    {
        var track = Track();
        track.Schedule([100.000, 100.001]);

        var left = Left(track, 5);

        Assert.Equal([1.0f, 1.5f, 0.75f, 0.25f, 0f], left);
    }

    [Fact]
    public void a_note_that_is_no_longer_predicted_is_not_played()
    {
        var track = Track();
        track.Schedule([100.010]);
        // An edge: the rhythm stops before the note came due.
        track.Schedule([]);

        Assert.All(Left(track, 20), s => Assert.Equal(0f, s));
    }

    [Fact]
    public void a_click_already_sounding_plays_out_when_the_prediction_drops_it()
    {
        var track = Track();
        track.Schedule([100.004]);
        var first = Left(track, 5);
        track.Schedule([]);

        var second = Left(track, 5);

        Assert.Equal(1.0f, first[4]);
        Assert.Equal([0.5f, 0.25f], second[..2]);
    }

    // The main thread predicts from what is being heard, which is behind what the audio thread has
    // already written - so the note it predicts next has often started already.
    [Fact]
    public void a_note_predicted_again_after_it_started_is_not_played_twice()
    {
        var track = Track();
        track.Schedule([100.004, 100.008]);
        Left(track, 5);
        track.Schedule([100.004, 100.008]);

        var left = Left(track, 10);

        Assert.Equal([0.5f, 0.25f, 0f, 1.0f, 0.5f, 0.25f, 0f, 0f, 0f, 0f], left);
    }

    // Known only after its frame was written - better late than silent, the way the Python side
    // played every click whenever its timer got round to it.
    [Fact]
    public void a_note_predicted_too_late_plays_at_once()
    {
        var track = Track();
        Left(track, 10);
        track.Schedule([100.005]);

        var left = Left(track, 5);

        Assert.Equal([1.0f, 0.5f, 0.25f, 0f, 0f], left);
    }

    // So late it no longer belongs to its beat - after a stall, say. A click there would be a beat
    // nobody asked for.
    [Fact]
    public void a_note_far_too_late_is_dropped()
    {
        var track = Track();
        Left(track, 1000);
        track.Schedule([100.500]);

        Assert.All(Left(track, 10), s => Assert.Equal(0f, s));
    }

    [Fact]
    public void the_volume_scales_it()
    {
        var track = Track();
        track.Volume = 0.5f;
        track.Schedule([100.000]);

        Assert.Equal([0.5f, 0.25f, 0.125f], Left(track, 3));
    }

    [Fact]
    public void muted_it_is_silent_but_keeps_its_place()
    {
        var track = Track();
        track.Muted = true;
        track.Schedule([100.000, 100.004]);
        Left(track, 2);
        track.Muted = false;

        // The first click was silenced, not postponed; the second plays where it belongs.
        Assert.Equal([0f, 0f, 1.0f, 0.5f, 0.25f], Left(track, 5));
    }

    [Fact]
    public void it_counts_the_frames_it_has_written()
    {
        var track = Track();
        Left(track, 7);
        Left(track, 5);

        Assert.Equal(12, track.FramesWritten);
    }

    [Fact]
    public void stereo_interleaves_a_mono_click() =>
        Assert.Equal([1.0f, 1.0f, 0.5f, 0.5f, 0.25f, 0.25f], ClickTrack.Stereo(Click));
}
