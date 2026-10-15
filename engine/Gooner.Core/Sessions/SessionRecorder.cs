using Gooner.Core.Beat;

namespace Gooner.Core.Sessions;

// An ordered record of what the session actually did - which rhythm was playing when, and which
// media were on screen while it played. Port of src/SessionRecorder.py.
//
// Separate from ScoreTracker on purpose: that aggregates numbers, and an ordered recording is not
// a score - which also means the file ScoreTracker writes cannot pick any of this up.
//
// **Nothing here is ever logged, and nothing is persisted on its own.** The timeline holds media
// file paths, exactly what the app promises to keep out of its own data directory. It lives for
// one session, feeds the Session Explorer, and is dropped when the next session starts. The one
// way out is the user pressing Save, which hands the timeline to SessionFiles.ToSavedSession.
//
// Every call takes an optional moment, so the stitching can be tested by handing it timestamps;
// without one it reads the TimeProvider, in seconds since the Unix epoch.
public sealed class SessionRecorder(TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    private double? _startedAt;
    private double? _endedAt;
    private readonly List<(double Start, Segment Segment)> _segments = [];
    private readonly List<(double Start, string Path)> _media = [];
    private double? _climaxAt;
    private string? _climaxOutcome;
    private readonly List<double> _fakeClimaxes = [];

    private double Now => (_time.GetUtcNow() - DateTimeOffset.UnixEpoch).TotalSeconds;

    // --- recording ---

    public void SessionStarted(double? at = null)
    {
        _startedAt = at ?? Now;
        _endedAt = null;
        _segments.Clear();
        _media.Clear();
        _climaxAt = null;
        _climaxOutcome = null;
        _fakeClimaxes.Clear();
    }

    public void SessionEnded(double? at = null) => _endedAt = at ?? Now;

    public void SegmentStarted(Segment segment, double? at = null) => _segments.Add((at ?? Now, segment));

    public void MediaShown(string path, double? at = null) => _media.Add((at ?? Now, path));

    public void FakeClimaxRecorded(double? at = null) => _fakeClimaxes.Add(at ?? Now);

    public void ClimaxRecorded(string? outcome, double? at = null)
    {
        _climaxAt = at ?? Now;
        _climaxOutcome = outcome;
    }

    // --- reading it back ---

    // The recording, stitched into spans the Session Explorer can render. Each segment runs until
    // the next one starts, the last until the session ended. Media are placed into *every* segment
    // they overlap rather than only the one they started in: a picture that stayed up across a
    // rhythm change really was on screen for both, and dropping it from the second would hide the
    // very one someone is scrolling back to find. The later appearances are flagged CarriedOver so
    // they read as a continuation instead of a duplicate.
    public SessionTimeline Timeline(double? now = null)
    {
        var endOfSession = _endedAt ?? now ?? Now;
        var mediaSpans = MediaSpans(endOfSession);

        var segments = new List<RecordedSegment>();
        for (var index = 0; index < _segments.Count; index++)
        {
            var (start, segment) = _segments[index];
            var isLast = index == _segments.Count - 1;
            var end = isLast ? endOfSession : _segments[index + 1].Start;
            segments.Add(new RecordedSegment(
                segment.Kind,
                segment.PatternName,
                segment.Freq,
                start,
                end,
                MediaWithin(mediaSpans, start, end, isFirst: index == 0),
                // What the planner asked for, next to what it measured. They differ: a segment
                // ends at the first note *past* its planned end, and the final one is held open
                // until the session stops. Saving a session replays the planned figure.
                PlannedSec: segment.DurationSec));
        }

        return new SessionTimeline(_startedAt, endOfSession, _climaxAt, _climaxOutcome, [.. _fakeClimaxes], segments);
    }

    // (start, end, path) per medium - each runs until the next one is shown.
    private List<(double Start, double End, string Path)> MediaSpans(double endOfSession)
    {
        var spans = new List<(double, double, string)>();
        for (var index = 0; index < _media.Count; index++)
        {
            var (start, path) = _media[index];
            var end = index == _media.Count - 1 ? endOfSession : _media[index + 1].Start;
            spans.Add((start, end, path));
        }
        return spans;
    }

    // Media overlapping [segmentStart, segmentEnd). The first segment reaches back before its own
    // start: the first medium is loaded before the beat handler has planned anything, so it would
    // otherwise belong to no segment at all.
    private static List<RecordedMedium> MediaWithin(
        List<(double Start, double End, string Path)> spans, double segmentStart, double segmentEnd, bool isFirst)
    {
        var lowerBound = isFirst ? double.NegativeInfinity : segmentStart;
        return [.. spans
            .Where(span => !(span.End <= segmentStart || span.Start >= segmentEnd))
            .Select(span => new RecordedMedium(span.Path, span.Start, span.End, CarriedOver: span.Start < lowerBound))];
    }
}
