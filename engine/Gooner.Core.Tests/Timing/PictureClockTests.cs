using Gooner.Core.Timing;

namespace Gooner.Core.Tests.Timing;

// The clock the picture runs on. The audio clock is the truth, but it moves in mix-sized steps and
// jitters by a few milliseconds; drawn straight off it, a note visibly stutters on its way to the
// hit zone. This one advances by frame time, is pulled gently towards the audio, and never runs
// backwards - what the phase 0 spike found a note needs to land on its click.
public class PictureClockTests
{
    [Fact]
    public void the_first_reading_is_taken_as_it_is()
    {
        var clock = new PictureClock();

        Assert.Equal(10.0, clock.Next(audio: 10.0, frameSeconds: 0.016));
    }

    [Fact]
    public void it_advances_by_frame_time_while_the_audio_agrees()
    {
        var clock = new PictureClock();
        clock.Next(10.0, 0.016);

        Assert.Equal(10.016, clock.Next(10.016, 0.016), 12);
    }

    [Fact]
    public void it_is_pulled_a_tenth_of_the_way_towards_the_audio_each_frame()
    {
        var clock = new PictureClock();
        clock.Next(10.0, 0.016);

        // Frame time says 10.016, the audio 10.026: a tenth of the 10 ms gap is taken.
        Assert.Equal(10.017, clock.Next(10.026, 0.016), 12);
    }

    [Fact]
    public void it_never_runs_backwards()
    {
        var clock = new PictureClock();
        clock.Next(10.0, 0.016);

        // The audio says time went back; the picture holds still instead.
        Assert.Equal(10.0, clock.Next(9.99, 0.0), 12);
    }

    [Fact]
    public void it_slows_down_rather_than_jump_when_the_audio_lags_a_little()
    {
        var clock = new PictureClock();
        clock.Next(10.0, 0.016);

        var next = clock.Next(10.006, 0.016);

        Assert.InRange(next, 10.006, 10.016);
        Assert.Equal(10.015, next, 12);
    }

    // After a long stall (a model loading on the main thread) frame time knows nothing about what
    // the audio played meanwhile; nudging would take seconds to catch up. It jumps instead.
    [Fact]
    public void it_snaps_forward_when_it_is_hopelessly_behind()
    {
        var clock = new PictureClock();
        clock.Next(10.0, 0.016);

        Assert.Equal(10.3, clock.Next(10.3, 0.016), 12);
        Assert.Equal(1, clock.Snaps);
    }

    // Far ahead of the audio - the device stalled - it waits for the audio to catch up.
    [Fact]
    public void it_waits_when_the_audio_has_fallen_far_behind()
    {
        var clock = new PictureClock();
        clock.Next(10.0, 0.016);

        Assert.Equal(10.0, clock.Next(9.5, 0.016), 12);
        Assert.Equal(10.0, clock.Next(9.6, 0.016), 12);
    }
}
