using Gooner.Core.Panels;
using Gooner.Core.Sessions;

namespace Gooner.Core.Tests.Panels;

// Port of tests/test_session_explorer_dialog.py: scrub the session's timeline and see what was on
// screen. The bar's geometry is asked with the width it would be drawn at; which clips go to the
// thumbnail grabber is what a scrub says it needs.
public sealed class SessionExplorerTests
{
    private const double Width = 800;
    private static readonly double[] ScrubTimes = [10.0, 20.0, 30.0];

    private static SessionTimeline Timeline(IReadOnlyList<RecordedSegment>? segments = null, double endedAt = 1100.0,
        double? climaxAt = null, string? outcome = null, IReadOnlyList<double>? fakes = null) =>
        new(1000.0, endedAt, climaxAt, outcome, fakes ?? [], segments ?? [Segment()]);

    private static RecordedSegment Segment(string kind = "beat", string? pattern = "Standard Beat", double? freq = 2.0,
        double start = 1000.0, double end = 1100.0, params RecordedMedium[] media) =>
        new(kind, pattern, freq, start, end, media);

    private static RecordedMedium Medium(string path = "a.png", double start = 1000.0, double end = 1100.0, bool carriedOver = false) =>
        new(path, start, end, carriedOver);

    // --- the timeline bar ---

    [Fact]
    public void the_bar_spans_the_whole_session()
    {
        var explorer = new SessionExplorer(Timeline(endedAt: 1100.0));

        Assert.Equal(0.0, explorer.TimeAtX(0, Width));
        Assert.Equal(100.0, explorer.TimeAtX(Width, Width), 6);
    }

    [Fact]
    public void a_position_halfway_along_is_halfway_through() =>
        Assert.Equal(50.0, new SessionExplorer(Timeline(endedAt: 1100.0)).TimeAtX(Width / 2, Width), 6);

    [Fact]
    public void a_position_off_the_bar_is_held_to_its_ends()
    {
        var explorer = new SessionExplorer(Timeline(endedAt: 1100.0));

        Assert.Equal(0.0, explorer.TimeAtX(-20, Width));
        Assert.Equal(100.0, explorer.TimeAtX(Width + 20, Width));
    }

    [Fact]
    public void a_zero_length_session_does_not_divide_by_zero()
    {
        var explorer = new SessionExplorer(Timeline([], endedAt: 1000.0));

        Assert.Equal(0.0, explorer.TimeAtX(50, Width));
        Assert.Equal(0.0, explorer.XForTime(10, Width));
    }

    [Fact]
    public void the_bar_knows_where_the_climax_sits()
    {
        var explorer = new SessionExplorer(Timeline(climaxAt: 1075.0, outcome: "ruined"));

        Assert.Equal(75.0, explorer.ClimaxOffset);
        Assert.Equal(Width * 0.75, explorer.XForTime(75.0, Width), 6);
    }

    [Fact]
    public void every_segment_is_drawn_as_wide_as_it_lasted()
    {
        var explorer = new SessionExplorer(Timeline([Segment(start: 1000.0, end: 1040.0), Segment("pause", null, null, 1040.0, 1100.0)]));

        Assert.Equal([("beat", 0.0, 40.0), ("pause", 40.0, 100.0)], explorer.Segments.Select(s => (s.Kind, s.Start, s.End)));
    }

    // --- scrubbing ---

    [Fact]
    public void scrubbing_shows_the_medium_that_was_on_screen()
    {
        var explorer = new SessionExplorer(Timeline([Segment(media: [Medium("early.png", 1000.0, 1050.0), Medium("late.png", 1050.0, 1100.0)])]));

        Assert.Equal("early.png", explorer.ScrubTo(10.0).Path);
        Assert.Equal("late.png", explorer.ScrubTo(80.0).Path);
        Assert.Equal("late.png", explorer.SelectedPath);
    }

    // A medium carried over into the next segment is listed there again; it is still one medium.
    [Fact]
    public void a_medium_spanning_two_segments_is_found_in_both()
    {
        var explorer = new SessionExplorer(Timeline([
            Segment(start: 1000.0, end: 1050.0, media: [Medium("long.png", 1000.0, 1080.0)]),
            Segment(start: 1050.0, end: 1100.0, media: [Medium("long.png", 1000.0, 1080.0, carriedOver: true), Medium("next.png", 1080.0, 1100.0)]),
        ]));

        Assert.Equal("long.png", explorer.ScrubTo(60.0).Path);
        Assert.Equal("next.png", explorer.ScrubTo(90.0).Path);
    }

    [Fact]
    public void scrubbing_reports_the_segment_at_that_moment()
    {
        var explorer = new SessionExplorer(Timeline([
            Segment(pattern: "Slow Pulse", freq: 1.25, start: 1000.0, end: 1050.0),
            Segment("pause", null, null, 1050.0, 1100.0),
        ]));

        var beat = explorer.ScrubTo(10.0).Text;
        Assert.Contains("Slow Pulse", beat);
        Assert.Contains("1.25", beat);

        var pause = explorer.ScrubTo(80.0).Text;
        Assert.Contains("Pause", pause);
        Assert.DoesNotContain("Hz", pause);
    }

    [Fact]
    public void the_moment_reads_as_the_dialog_wrote_it() =>
        Assert.Equal("00:10   Beat   1.25 Hz   Slow Pulse",
            new SessionExplorer(Timeline([Segment(pattern: "Slow Pulse", freq: 1.25)])).ScrubTo(10.0).Text);

    [Fact]
    public void the_moment_label_shows_the_time() =>
        Assert.Contains("1:05", new SessionExplorer(Timeline()).ScrubTo(65.0).Text);

    [Fact]
    public void scrubbing_past_every_medium_clears_the_preview() =>
        Assert.Null(new SessionExplorer(Timeline([Segment(media: [Medium("a.png", 1000.0, 1010.0)])])).ScrubTo(50.0).Path);

    [Fact]
    public void scrubbing_moves_the_playhead()
    {
        var explorer = new SessionExplorer(Timeline(endedAt: 1100.0));

        explorer.ScrubTo(40.0);

        Assert.Equal(40.0, explorer.PlayheadOffset);
    }

    [Fact]
    public void a_finale_segment_is_still_scrubbable() =>
        Assert.Contains("Finale", new SessionExplorer(Timeline([Segment("finale")])).ScrubTo(50.0).Text);

    // --- thumbnails ---

    // Images and GIFs decode straight off disk - queueing them behind a video would make them
    // appear late for no reason.
    [Fact]
    public void only_videos_go_to_the_thumbnail_source()
    {
        var explorer = new SessionExplorer(Timeline([Segment(media: [Medium("a.png", 1000.0, 1050.0), Medium("b.mp4", 1050.0, 1100.0)])]));

        Assert.False(explorer.ScrubTo(10.0).RequestFrame);
        var video = explorer.ScrubTo(80.0);

        Assert.True(video.IsVideo);
        Assert.True(video.RequestFrame);
    }

    // Scrubbing back and forth over the same clip must not decode it every time.
    [Fact]
    public void a_video_frame_is_only_grabbed_once()
    {
        var explorer = new SessionExplorer(Timeline([Segment(media: [Medium("b.mp4")])]));

        Assert.Equal([true, false, false], ScrubTimes.Select(at => explorer.ScrubTo(at).RequestFrame));
    }

    // --- acting on what you found ---

    [Fact]
    public void the_buttons_are_disabled_until_something_is_on_screen()
    {
        var explorer = new SessionExplorer(Timeline([Segment(media: [Medium("a.png", 1000.0, 1010.0)])]));
        Assert.False(explorer.CanReveal);

        explorer.ScrubTo(5.0);

        Assert.True(explorer.CanReveal);
    }

    // The containing folder rather than the file: opening the file would launch it in whatever is
    // registered for the type, which is what Open in player is for.
    [Fact]
    public void revealing_opens_the_containing_folder()
    {
        var file = Path.Combine("C:", "media", "keep_this.png");
        var explorer = new SessionExplorer(Timeline([Segment(media: [Medium(file)])]));

        explorer.ScrubTo(50.0);

        Assert.Equal(Path.Combine("C:", "media"), explorer.RevealFolder);
    }

    [Fact]
    public void the_play_button_only_shows_for_video()
    {
        var explorer = new SessionExplorer(Timeline([Segment(media: [Medium("a.png", 1000.0, 1050.0), Medium("b.mp4", 1050.0, 1100.0)])]));

        Assert.False(explorer.ScrubTo(10.0).IsVideo);
        Assert.True(explorer.ScrubTo(80.0).IsVideo);
    }

    [Fact]
    public void a_timeline_with_no_segments_does_not_raise()
    {
        var explorer = new SessionExplorer(Timeline([]));

        Assert.Null(explorer.ScrubTo(10.0).Path);
        Assert.Equal("00:10", explorer.ScrubTo(10.0).Text);
    }

    [Fact]
    public void the_heading_counts_segments_fakes_and_the_outcome()
    {
        Assert.Equal("1 segments over 01:40", new SessionExplorer(Timeline()).Heading);
        Assert.Equal("1 segments over 01:40  -  2 fake-outs survived  -  Ruined climax",
            new SessionExplorer(Timeline(fakes: [1020.0, 1030.0], climaxAt: 1090.0, outcome: "ruined")).Heading);
        Assert.Contains("1 fake-out survived", new SessionExplorer(Timeline(fakes: [1020.0])).Heading);
    }

    // --- fake climaxes on the bar ---

    [Fact]
    public void the_bar_marks_every_fake_climax() =>
        Assert.Equal([20.0, 65.0], new SessionExplorer(Timeline(fakes: [1020.0, 1065.0])).FakeOffsets);

    [Fact]
    public void a_session_without_fake_climaxes_marks_none() => Assert.Empty(new SessionExplorer(Timeline()).FakeOffsets);

    // Otherwise the bar shows a mark with no way to find out what it was.
    [Fact]
    public void scrubbing_onto_a_fake_climax_says_so() =>
        Assert.Contains("Fake-out", new SessionExplorer(Timeline(fakes: [1050.0])).ScrubTo(50.5).Text);

    [Fact]
    public void scrubbing_away_from_a_fake_climax_stops_saying_so() =>
        Assert.DoesNotContain("Fake-out", new SessionExplorer(Timeline(fakes: [1050.0])).ScrubTo(20.0).Text);

    [Fact]
    public void a_fake_climax_is_not_confused_with_the_real_one()
    {
        var explorer = new SessionExplorer(Timeline(fakes: [1020.0], climaxAt: 1080.0, outcome: "real"));

        Assert.Contains("Fake-out", explorer.ScrubTo(20.0).Text);

        var real = explorer.ScrubTo(80.0).Text;
        Assert.DoesNotContain("Fake-out", real);
        Assert.Contains("Climax landed here", real);
    }

    // --- saving the session ---

    // The explorer is also opened on sessions that have nowhere to go - a button that cannot do
    // anything is worse than no button.
    [Fact]
    public void there_is_no_save_button_when_nothing_can_be_saved_to() => Assert.False(new SessionExplorer(Timeline()).CanSave);

    [Fact]
    public void saving_hands_the_session_over_once()
    {
        var calls = 0;
        var explorer = new SessionExplorer(Timeline(), () => ++calls > 0);

        Assert.True(explorer.Save());

        Assert.Equal(1, calls);
    }

    // Two clicks used to mean two identical sessions on the shelf.
    [Fact]
    public void the_button_says_it_saved_and_will_not_save_the_same_session_twice()
    {
        var calls = 0;
        var explorer = new SessionExplorer(Timeline(), () => ++calls > 0);

        explorer.Save();
        explorer.Save();

        Assert.Equal(1, calls);
        Assert.True(explorer.Saved);
    }

    // Saving writes media paths to disk, which is the one thing this app otherwise never does - so
    // the save says so, and says where to delete it again.
    [Fact]
    public void the_user_is_told_where_the_saved_session_lives_and_that_it_holds_paths()
    {
        Assert.Contains("path", SessionExplorer.SavedMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Privacy", SessionExplorer.SavedMessage);
    }

    [Fact]
    public void a_save_that_fails_says_so_and_can_be_tried_again()
    {
        var explorer = new SessionExplorer(Timeline(), () => false);

        Assert.False(explorer.Save());

        Assert.False(explorer.Saved);
    }
}
