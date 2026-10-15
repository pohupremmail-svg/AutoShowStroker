using System.Text.Json.Nodes;
using Gooner.Core.Sessions;

namespace Gooner.Parity.Tests;

// src/SessionScript.py vs Gooner.Core.Sessions.SessionScript: each script drained the way the
// consumers would drain it, everything it handed out compared.
public class SessionScriptParity
{
    private const string Module = "session_script";

    public static IEnumerable<object[]> Draining() => Golden.Cases(Module, "drain");

    [Theory]
    [MemberData(nameof(Draining))]
    public void drain(string name, string json)
    {
        var c = JsonNode.Parse(json)!;
        var script = new SessionScript(c["input"]!.AsObject(), ignorePaths: (bool)c["ignore_paths"]!);
        var actual = Drained(script, firstIndex: 5);
        Assert.True(Golden.Equivalent(c["expected"], actual), $"{name}:\nexpected {c["expected"]!.ToJsonString()}\nactual   {actual.ToJsonString()}");
    }

    // Mirrors parity/generate.py's _drain(), field for field.
    private static JsonObject Drained(SessionScript script, int firstIndex)
    {
        var patterns = new JsonObject();
        foreach (var (key, steps) in script.CustomPatterns)
            patterns[key] = new JsonArray([.. steps.Select(s => (JsonNode)s)]);

        var output = new JsonObject
        {
            ["segment_count"] = script.SegmentCount,
            ["duration"] = script.Duration,
            ["has_media_paths"] = script.HasMediaPaths,
            ["climax_offset"] = script.ClimaxOffset,
            ["climax_outcome"] = script.ClimaxOutcome,
            ["fake_offsets"] = new JsonArray([.. script.FakeOffsets.Select(f => (JsonNode)f)]),
            ["custom_patterns"] = patterns,
            ["had_segments"] = script.HasSegmentsLeft,
        };

        var segments = new JsonArray();
        for (var index = firstIndex; script.NextSegment(index) is { } s; index++)
            segments.Add(new JsonArray(s.Kind, s.DurationSec, s.Freq, s.PatternName, s.Index));
        output["segments"] = segments;
        output["has_segments_left"] = script.HasSegmentsLeft;

        var gaps = new JsonArray();
        while (script.NextMediaGap() is { } gap)
            gaps.Add(gap);
        output["gaps"] = gaps;

        var paths = new JsonArray();
        while (script.NextMediaPath() is { } path)
            paths.Add(path);
        output["paths"] = paths;
        return output;
    }
}
