using Gooner.Core.Beat;
using Gooner.Core.Sessions;

namespace Gooner.Core.Tests.Sessions;

// Port of tests/test_session_recorder.py. What played when, in order - the data behind the
// Session Explorer. Every call takes the moment it happened, so the stitching is tested by handing
// it timestamps directly.
public class SessionRecorderTests
{
    private readonly SessionRecorder _recorder = new();

    public SessionRecorderTests() => _recorder.SessionStarted(at: 1000.0);

    private static Segment Beat(int index = 0, double freq = 2.0, string pattern = "Standard Beat", string kind = "beat") =>
        new(kind, 20.0, freq, pattern, index);

    private static List<string> Paths(RecordedSegment segment) => [.. segment.Media.Select(m => m.Path)];

    // --- segments ---

    [Fact]
    public void a_fresh_recorder_has_an_empty_timeline() =>
        Assert.Empty(new SessionRecorder().Timeline().Segments);

    [Fact]
    public void a_segment_ends_where_the_next_one_starts()
    {
        _recorder.SegmentStarted(Beat(0), at: 1000.0);
        _recorder.SegmentStarted(Beat(1), at: 1030.0);
        _recorder.SessionEnded(at: 1100.0);

        var segments = _recorder.Timeline().Segments;

        Assert.Equal((1000.0, 1030.0), (segments[0].Start, segments[0].End));
    }

    [Fact]
    public void the_last_segment_ends_when_the_session_does()
    {
        _recorder.SegmentStarted(Beat(0), at: 1000.0);
        _recorder.SegmentStarted(Beat(1), at: 1030.0);
        _recorder.SessionEnded(at: 1100.0);

        Assert.Equal(1100.0, _recorder.Timeline().Segments[^1].End);
    }

    [Fact]
    public void a_segment_carries_its_rhythm_speed_and_type()
    {
        _recorder.SegmentStarted(Beat(0, freq: 3.5, pattern: "Quick Swing"), at: 1000.0);
        _recorder.SessionEnded(at: 1030.0);

        var segment = _recorder.Timeline().Segments[0];

        Assert.Equal("beat", segment.Kind);
        Assert.Equal("Quick Swing", segment.Pattern);
        Assert.Equal(3.5, segment.Freq);
    }

    [Fact]
    public void a_pause_segment_keeps_its_kind_and_has_no_rhythm()
    {
        _recorder.SegmentStarted(new Segment("pause", 5.0, null, null, 0), at: 1000.0);
        _recorder.SessionEnded(at: 1010.0);

        var segment = _recorder.Timeline().Segments[0];

        Assert.Equal("pause", segment.Kind);
        Assert.Null(segment.Pattern);
        Assert.Null(segment.Freq);
    }

    [Fact]
    public void a_session_that_ends_without_a_segment_yields_nothing()
    {
        _recorder.SessionEnded(at: 1100.0);
        Assert.Empty(_recorder.Timeline().Segments);
    }

    // --- media inside the segments ---

    [Fact]
    public void media_land_in_the_segment_they_were_shown_in()
    {
        // b.png is still up when the second segment begins, so it belongs to both - see
        // a_medium_spanning_a_boundary_is_listed_in_both_segments.
        _recorder.SegmentStarted(Beat(0), at: 1000.0);
        _recorder.MediaShown("a.png", at: 1005.0);
        _recorder.MediaShown("b.png", at: 1010.0);
        _recorder.SegmentStarted(Beat(1), at: 1030.0);
        _recorder.MediaShown("c.png", at: 1035.0);
        _recorder.SessionEnded(at: 1060.0);

        var segments = _recorder.Timeline().Segments;

        Assert.Equal(["a.png", "b.png"], Paths(segments[0]));
        Assert.Equal(["b.png", "c.png"], Paths(segments[1]));
    }

    // It really was on screen in both - dropping it from the second would hide the very picture
    // someone is scrolling back to find.
    [Fact]
    public void a_medium_spanning_a_boundary_is_listed_in_both_segments()
    {
        _recorder.SegmentStarted(Beat(0), at: 1000.0);
        _recorder.MediaShown("long.png", at: 1020.0);
        _recorder.SegmentStarted(Beat(1), at: 1030.0);
        _recorder.SessionEnded(at: 1060.0);

        var segments = _recorder.Timeline().Segments;

        Assert.Equal(["long.png"], Paths(segments[0]));
        Assert.Equal(["long.png"], Paths(segments[1]));
    }

    [Fact]
    public void a_medium_carried_into_the_next_segment_is_marked_as_such()
    {
        _recorder.SegmentStarted(Beat(0), at: 1000.0);
        _recorder.MediaShown("long.png", at: 1020.0);
        _recorder.SegmentStarted(Beat(1), at: 1030.0);
        _recorder.SessionEnded(at: 1060.0);

        var segments = _recorder.Timeline().Segments;

        Assert.False(segments[0].Media[0].CarriedOver);
        Assert.True(segments[1].Media[0].CarriedOver);
    }

    [Fact]
    public void a_medium_ends_where_the_next_one_starts()
    {
        _recorder.SegmentStarted(Beat(0), at: 1000.0);
        _recorder.MediaShown("a.png", at: 1005.0);
        _recorder.MediaShown("b.png", at: 1012.0);
        _recorder.SessionEnded(at: 1030.0);

        var media = _recorder.Timeline().Segments[0].Media;

        Assert.Equal((1005.0, 1012.0), (media[0].Start, media[0].End));
        Assert.Equal(1030.0, media[1].End);
    }

    // The slideshow keeps running while the beat rests, so the pause is not a gap.
    [Fact]
    public void media_shown_during_a_pause_land_in_the_pause_segment()
    {
        _recorder.SegmentStarted(new Segment("pause", 5.0, null, null, 0), at: 1000.0);
        _recorder.MediaShown("during_pause.png", at: 1002.0);
        _recorder.SessionEnded(at: 1010.0);

        Assert.Equal(["during_pause.png"], Paths(_recorder.Timeline().Segments[0]));
    }

    // Start loads the first medium before the beat handler plans anything.
    [Fact]
    public void media_shown_before_the_first_segment_are_not_lost()
    {
        _recorder.MediaShown("first.png", at: 1000.0);
        _recorder.SegmentStarted(Beat(0), at: 1000.5);
        _recorder.SessionEnded(at: 1030.0);

        Assert.Equal(["first.png"], Paths(_recorder.Timeline().Segments[0]));
    }

    // --- the climax ---

    [Fact]
    public void the_climax_moment_and_outcome_are_recorded()
    {
        _recorder.SegmentStarted(Beat(0), at: 1000.0);
        _recorder.ClimaxRecorded("ruined", at: 1042.0);
        _recorder.SessionEnded(at: 1060.0);

        var timeline = _recorder.Timeline();

        Assert.Equal(1042.0, timeline.ClimaxAt);
        Assert.Equal("ruined", timeline.ClimaxOutcome);
    }

    [Fact]
    public void no_climax_leaves_both_unset()
    {
        _recorder.SegmentStarted(Beat(0), at: 1000.0);
        _recorder.SessionEnded(at: 1060.0);

        var timeline = _recorder.Timeline();

        Assert.Null(timeline.ClimaxAt);
        Assert.Null(timeline.ClimaxOutcome);
    }

    // --- session boundaries ---

    [Fact]
    public void the_timeline_reports_the_session_bounds()
    {
        _recorder.SegmentStarted(Beat(0), at: 1000.0);
        _recorder.SessionEnded(at: 1234.0);

        var timeline = _recorder.Timeline();

        Assert.Equal(1000.0, timeline.StartedAt);
        Assert.Equal(1234.0, timeline.EndedAt);
    }

    [Fact]
    public void a_second_session_does_not_inherit_the_first()
    {
        _recorder.SegmentStarted(Beat(0), at: 1000.0);
        _recorder.MediaShown("old.png", at: 1005.0);
        _recorder.ClimaxRecorded("real", at: 1010.0);
        _recorder.SessionEnded(at: 1030.0);

        _recorder.SessionStarted(at: 2000.0);
        _recorder.SegmentStarted(Beat(0), at: 2000.0);
        _recorder.SessionEnded(at: 2030.0);

        var timeline = _recorder.Timeline();

        Assert.Equal(2000.0, timeline.StartedAt);
        var segment = Assert.Single(timeline.Segments);
        Assert.Empty(segment.Media);
        Assert.Null(timeline.ClimaxAt);
    }

    // The explorer only opens after Stop, but nothing should blow up if it is asked earlier - the
    // last segment is simply still running.
    [Fact]
    public void a_timeline_read_mid_session_ends_at_the_moment_it_is_read()
    {
        _recorder.SegmentStarted(Beat(0), at: 1000.0);

        var timeline = _recorder.Timeline(now: 1020.0);

        Assert.Equal(1020.0, timeline.Segments[0].End);
    }

    // --- fake climaxes ---

    [Fact]
    public void fake_climaxes_are_recorded_in_order()
    {
        _recorder.SegmentStarted(Beat(0), at: 1000.0);
        _recorder.FakeClimaxRecorded(at: 1012.0);
        _recorder.FakeClimaxRecorded(at: 1040.0);
        _recorder.SessionEnded(at: 1060.0);

        Assert.Equal([1012.0, 1040.0], _recorder.Timeline().FakeClimaxes);
    }

    [Fact]
    public void a_session_without_fake_climaxes_reports_none()
    {
        _recorder.SegmentStarted(Beat(0), at: 1000.0);
        _recorder.SessionEnded(at: 1060.0);
        Assert.Empty(_recorder.Timeline().FakeClimaxes);
    }

    [Fact]
    public void fake_climaxes_do_not_survive_into_the_next_session()
    {
        _recorder.FakeClimaxRecorded(at: 1012.0);
        _recorder.SessionEnded(at: 1060.0);

        _recorder.SessionStarted(at: 2000.0);

        Assert.Empty(_recorder.Timeline(now: 2010.0).FakeClimaxes);
    }

    // Next to the length it measured. A saved session replays the *planned* one: the measured one
    // already includes the overshoot to the next note, and replaying that makes the segment
    // overshoot a second time.
    [Fact]
    public void a_segment_carries_the_length_it_was_planned_to_be()
    {
        _recorder.SegmentStarted(new Segment("beat", 20.0, 2.0, "Standard Beat", 0), at: 1000.0);
        _recorder.SegmentStarted(new Segment("beat", 5.0, 3.0, "Quick Swing", 1), at: 1020.4);
        _recorder.SessionEnded(at: 1030.0);

        var segments = _recorder.Timeline().Segments;
        var (first, second) = (segments[0], segments[1]);

        Assert.Equal(20.0, first.PlannedSec);
        Assert.Equal(20.4, first.End - first.Start, 9);  // what it actually ran
        Assert.Equal(5.0, second.PlannedSec);
    }
}
