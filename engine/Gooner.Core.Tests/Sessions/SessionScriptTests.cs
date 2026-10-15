using System.Text.Json.Nodes;
using Gooner.Core.Sessions;

namespace Gooner.Core.Tests.Sessions;

// Port of tests/test_session_script.py. The saved session, handed out to the things that would
// otherwise be drawing dice.
public class SessionScriptTests
{
    private static JsonObject Saved(Action<JsonObject>? overrides = null)
    {
        var saved = new JsonObject
        {
            ["format"] = 1,
            ["duration_sec"] = 200.0,
            ["segments"] = new JsonArray
            {
                new JsonObject { ["kind"] = "beat", ["pattern"] = "Quick Swing", ["freq"] = 2.3, ["duration_sec"] = 60.0 },
                new JsonObject { ["kind"] = "pause", ["pattern"] = null, ["freq"] = null, ["duration_sec"] = 15.0 },
                new JsonObject { ["kind"] = "finale", ["pattern"] = "Build Up", ["freq"] = 4.5, ["duration_sec"] = 125.0 },
            },
            ["custom_patterns"] = new JsonObject(),
            ["climax"] = new JsonObject { ["at_sec"] = 180.0, ["outcome"] = "ruined" },
            ["fake_climaxes"] = new JsonArray { 40.0, 120.0 },
            ["media"] = new JsonArray
            {
                new JsonObject { ["at_sec"] = 0.0, ["path"] = "a.png" },
                new JsonObject { ["at_sec"] = 30.0, ["path"] = "b.png" },
            },
        };
        overrides?.Invoke(saved);
        return saved;
    }

    // --- segments ---

    [Fact]
    public void segments_come_back_in_order_as_real_segments()
    {
        var script = new SessionScript(Saved());

        var first = script.NextSegment(index: 0)!;
        var second = script.NextSegment(index: 1)!;

        Assert.Equal(("beat", "Quick Swing", (double?)2.3, 60.0), (first.Kind, first.PatternName, first.Freq, first.DurationSec));
        Assert.Equal("pause", second.Kind);
    }

    // BeatHandler's own monotonic counter owns the index, not the file - a replay may be the
    // second session in the same run.
    [Fact]
    public void the_segment_index_is_carried_through() =>
        Assert.Equal(7, new SessionScript(Saved()).NextSegment(index: 7)!.Index);

    [Fact]
    public void the_script_runs_out_once_every_segment_has_been_handed_over()
    {
        var script = new SessionScript(Saved());
        for (var i = 0; i < 3; i++)
            Assert.NotNull(script.NextSegment(index: i));
        Assert.Null(script.NextSegment(index: 3));
    }

    [Fact]
    public void a_spent_script_reports_it()
    {
        var script = new SessionScript(Saved());
        Assert.True(script.HasSegmentsLeft);
        for (var i = 0; i < 3; i++)
            script.NextSegment(index: i);
        Assert.False(script.HasSegmentsLeft);
    }

    // --- climax and fake-outs ---

    [Fact]
    public void the_climax_time_and_outcome_come_from_the_file()
    {
        var script = new SessionScript(Saved());
        Assert.Equal(180.0, script.ClimaxOffset);
        Assert.Equal("ruined", script.ClimaxOutcome);
    }

    [Fact]
    public void a_session_saved_without_a_climax_has_none()
    {
        var script = new SessionScript(Saved(s => s["climax"] = null));
        Assert.Null(script.ClimaxOffset);
        Assert.Null(script.ClimaxOutcome);
    }

    [Fact]
    public void fake_out_offsets_come_from_the_file() =>
        Assert.Equal([40.0, 120.0], new SessionScript(Saved()).FakeOffsets);

    // --- media ---

    // The pacing is part of what was saved, not just the beats.
    [Fact]
    public void media_gaps_are_the_recorded_ones()
    {
        var script = new SessionScript(Saved());

        Assert.Equal(30.0, script.NextMediaGap()!.Value, 9);   // 0 -> 30
        Assert.Equal(170.0, script.NextMediaGap()!.Value, 9);  // 30 -> end of session
    }

    [Fact]
    public void media_gaps_run_out_with_the_script()
    {
        var script = new SessionScript(Saved());
        script.NextMediaGap();
        script.NextMediaGap();
        Assert.Null(script.NextMediaGap());
    }

    [Fact]
    public void the_recorded_paths_are_handed_out_in_order()
    {
        var script = new SessionScript(Saved());
        Assert.Equal("a.png", script.NextMediaPath());
        Assert.Equal("b.png", script.NextMediaPath());
    }

    [Fact]
    public void a_script_without_paths_says_so()
    {
        var script = new SessionScript(Saved(s => s["media"] = new JsonArray
        {
            new JsonObject { ["at_sec"] = 0.0 },
            new JsonObject { ["at_sec"] = 30.0 },
        }));
        Assert.False(script.HasMediaPaths);
        Assert.Null(script.NextMediaPath());
    }

    // Replaying someone else's difficulty against your own library.
    [Fact]
    public void paths_can_be_ignored_on_purpose()
    {
        var script = new SessionScript(Saved(), ignorePaths: true);

        Assert.False(script.HasMediaPaths);
        Assert.Null(script.NextMediaPath());
        Assert.Equal(30.0, script.NextMediaGap()!.Value, 9);  // the pacing still applies
    }

    // --- custom patterns ---

    [Fact]
    public void custom_patterns_from_the_file_are_exposed()
    {
        var script = new SessionScript(Saved(s => s["custom_patterns"] = new JsonObject { ["Mine"] = new JsonArray { 1, -1 } }));
        var mine = Assert.Single(script.CustomPatterns);
        Assert.Equal("Mine", mine.Key);
        Assert.Equal([1, -1], mine.Value);
    }

    [Fact]
    public void a_session_with_no_custom_patterns_exposes_none() =>
        Assert.Empty(new SessionScript(Saved()).CustomPatterns);
}
