namespace Gooner.Core.Beat;

// One stretch of the session, decided before it happens: a rhythm to follow, a pause to endure,
// or the "finale" run-in that covers the moment the climax is announced. Port of the Segment
// NamedTuple in src/BeatHandler.py.
//
// Freq/PatternName are null on a pause. Index is a monotonic counter that survives replanning,
// so a consumer can pin something to a specific upcoming boundary.
public sealed record Segment(string Kind, double DurationSec, double? Freq, string? PatternName, int Index);
