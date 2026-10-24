using System.Text.Json.Nodes;
using Gooner.Core.Beat;

namespace Gooner.Core.Sessions;

// A saved session, handed out to the parts that would otherwise be drawing dice. Port of
// src/SessionScript.py.
//
// Replaying is not a second engine: the planner reads its next segment, the climax its moment,
// the player its next media gap - a replay just answers those questions from the file instead of
// from random. Consumed in order and never rewound; once the script is spent the live behaviour
// takes over again.
//
// Takes the session as SessionFiles hands it over (validated, still a JsonObject).
public sealed class SessionScript
{
    private readonly List<JsonObject> _segments;
    private readonly List<JsonObject> _media;
    private readonly bool _ignorePaths;

    private int _segmentPos;
    // Two cursors, not one: own-library mode consumes the gaps and ignores the paths, so they have
    // to advance independently.
    private int _mediaPos;
    private int _pathPos;

    public SessionScript(JsonObject saved, bool ignorePaths = false)
    {
        ArgumentNullException.ThrowIfNull(saved);
        _segments = Objects(saved["segments"]);
        _media = Objects(saved["media"]);
        Duration = PyJson.Truthy(saved["duration_sec"]) ? PyJson.Number(saved["duration_sec"]) : 0.0;
        _ignorePaths = ignorePaths;
        CustomPatterns = saved["custom_patterns"] is JsonObject patterns
            ? patterns.ToDictionary(kv => kv.Key, kv => kv.Value!.AsArray().Select(s => (int)PyJson.Number(s)).ToArray())
            : [];

        var climax = saved["climax"] as JsonObject;
        ClimaxOffset = climax?["at_sec"] is { } at ? PyJson.Number(at) : null;
        ClimaxOutcome = (string?)climax?["outcome"];
        FakeOffsets = saved["fake_climaxes"] is JsonArray fakes ? fakes.Select(PyJson.Number).ToList() : [];
    }

    public IReadOnlyDictionary<string, int[]> CustomPatterns { get; }
    public double? ClimaxOffset { get; }
    public string? ClimaxOutcome { get; }
    public IReadOnlyList<double> FakeOffsets { get; }

    // How long the recorded session ran.
    public double Duration { get; }

    // --- segments ---

    public bool HasSegmentsLeft => _segmentPos < _segments.Count;

    // How many segments were recorded, cursor or no cursor. The planner numbers a session's
    // segments from 0 and hands the script those indices, so anything numbered at or above this
    // was drawn rather than replayed - which is how the climax tells the recorded stretch from the
    // improvised tail behind it.
    public int SegmentCount => _segments.Count;

    // The next recorded segment, or null once the script is spent. The index comes from the
    // planner's own monotonic counter rather than the file: a replay may be the second session of
    // the run, and the counter has to keep climbing.
    public Segment? NextSegment(int index)
    {
        if (!HasSegmentsLeft) return null;
        var data = _segments[_segmentPos++];
        return new Segment(
            (string)data["kind"]!,
            PyJson.Number(data["duration_sec"]),
            data["freq"] is { } freq ? PyJson.Number(freq) : null,
            (string?)data["pattern"],
            index);
    }

    // --- media ---

    public bool HasMediaPaths => !_ignorePaths && _media.Any(entry => entry.ContainsKey("path"));

    // Seconds the medium now on screen should stay up, or null once spent. The last medium runs to
    // the end of the recorded session. Gaps are handed out even when the paths are being ignored:
    // the pacing is part of what was saved, and it is most of what "the same session" means when
    // the pictures are someone else's.
    public double? NextMediaGap()
    {
        if (_mediaPos >= _media.Count) return null;
        var current = PyJson.Number(_media[_mediaPos]["at_sec"]);
        var following = _mediaPos + 1;
        var end = following < _media.Count ? PyJson.Number(_media[following]["at_sec"]) : Duration;
        _mediaPos++;
        return Math.Max(0.0, end - current);
    }

    // The path of the next recorded medium, or null when there is none to give. Skips entries
    // that carry no path at all, which is what a partially stripped file would look like.
    public string? NextMediaPath()
    {
        var (path, after) = FindPath();
        _pathPos = after;
        return path;
    }

    // The path NextMediaPath would give, without giving it: what the player gets ready in advance.
    public string? PeekNextMediaPath() => FindPath().Path;

    private (string? Path, int After) FindPath()
    {
        if (_ignorePaths) return (null, _pathPos);
        for (var pos = _pathPos; pos < _media.Count; pos++)
            if (_media[pos].ContainsKey("path"))
                return ((string?)_media[pos]["path"], pos + 1);
        return (null, _media.Count);
    }

    private static List<JsonObject> Objects(JsonNode? node) =>
        node is JsonArray array ? array.Select(n => n!.AsObject()).ToList() : [];
}
