namespace Gooner.Core.Sessions;

// What SessionRecorder hands over at the end of a session: wall-clock times, every segment in
// order with the media that were on screen during it. A medium spanning two segments is listed
// under both, the second time flagged CarriedOver.
public sealed record RecordedMedium(string Path, double Start, double End, bool CarriedOver);

public sealed record RecordedSegment(
    string Kind,
    string? Pattern,
    double? Freq,
    double Start,
    double End,
    IReadOnlyList<RecordedMedium> Media,
    double? PlannedSec = null);

public sealed record SessionTimeline(
    double? StartedAt,
    double? EndedAt,
    double? ClimaxAt,
    string? ClimaxOutcome,
    IReadOnlyList<double> FakeClimaxes,
    IReadOnlyList<RecordedSegment> Segments);
