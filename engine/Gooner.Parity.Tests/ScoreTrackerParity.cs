using System.Text.Json.Nodes;
using Gooner.Core.Data;
using Gooner.Core.Scoring;

namespace Gooner.Parity.Tests;

// src/ScoreTracker.py vs Gooner.Core.Scoring.ScoreTracker: the same event scripts on a set clock,
// every probe and the history file they leave behind compared.
public class ScoreTrackerParity
{
    private const string Module = "score_tracker";

    public static IEnumerable<object[]> Runs() => Golden.Cases(Module, "run");

    [Theory]
    [MemberData(nameof(Runs))]
    public void run(string name, string json)
    {
        var c = JsonNode.Parse(json)!;
        var actual = Run(c["history"], c["steps"]!.AsArray());
        Assert.True(Golden.Equivalent(c["expected"], actual), $"{name}:\nexpected {c["expected"]!.ToJsonString()}\nactual   {actual.ToJsonString()}");
    }

    [Fact]
    public void format_metric_value()
    {
        foreach (var row in Golden.Load(Module)["format_metric_value"]!.AsArray())
        {
            var metric = (string)row![0]!;
            var value = row[1]!.GetValue<double>();
            Assert.True((string)row[2]! == ScoreTracker.FormatMetricValue(metric, value), $"{metric} {value}");
        }
    }

    // Mirrors parity/generate.py's _run_tracker().
    private static JsonObject Run(JsonNode? history, JsonArray steps)
    {
        var dir = Directory.CreateTempSubdirectory("gooner-parity-score-");
        try
        {
            var store = new UserDataStore(dir.FullName);
            if (history is not null)
                store.Save("session_history", history.DeepClone());
            var clock = new SetClock();
            var tracker = new ScoreTracker(store, clock);
            var probes = new JsonArray();
            foreach (var step in steps)
            {
                var s = step!.AsArray();
                clock.Now = s[0]!.GetValue<double>();
                var probe = Step(tracker, (string)s[1]!, s);
                if (probe.IsProbe)
                    probes.Add(probe.Value);
            }
            var saved = store.Load("session_history") as JsonArray;
            return new JsonObject
            {
                ["probes"] = probes,
                ["saved_history"] = saved is null ? null : new JsonArray([.. saved.Select(e => (JsonNode)WithoutEndedAt(e!.AsObject()))]),
            };
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static (bool IsProbe, JsonNode? Value) Step(ScoreTracker t, string call, JsonArray s)
    {
        switch (call)
        {
            case "session_started": t.SessionStarted(); break;
            case "session_ended": t.SessionEnded(); break;
            case "beat": t.Beat(); break;
            case "beat_changed": t.BeatChanged(s[2]!.GetValue<double>(), (string)s[3]!); break;
            case "beat_paused": t.BeatPaused(); break;
            case "beat_resumed": t.BeatResumed(); break;
            case "media_skipped": t.MediaSkipped(); break;
            case "media_repeated": t.MediaRepeated(); break;
            case "replay_started": t.ReplayStarted(); break;
            case "fake_climax_triggered": t.FakeClimaxTriggered(); break;
            case "fell_for_fake_climax": t.FellForFakeClimax(); break;
            case "edge_reached": t.EdgeReached(); break;
            case "climax_decided": t.ClimaxDecided((string?)s[2]); break;
            case "outcome_reported": t.OutcomeReported((string?)s[2]); break;
            case "live": return (true, Numbers(t.LiveMetrics()));
            case "chase":
                // Python reads a None best as "no best"; here that is a missing key.
                var bests = s[2]!.AsObject().Where(kv => kv.Value is not null)
                    .ToDictionary(kv => kv.Key, kv => kv.Value!.GetValue<double>());
                return (true, t.RecordChaseStatus(bests) is { } status
                    ? new JsonArray(status.Metric, status.Current, status.Best)
                    : null);
            case "bests": return (true, Numbers(t.GetAllTimeBests()));
            case "infos": return (true, t.DeliverInfos());
            case "records": return (true, Numbers(t.LastSessionNewRecords));
            case "history": return (true, new JsonArray([.. t.GetHistory().Select(e => (JsonNode)WithoutEndedAt(e))]));
            default: throw new ArgumentException($"Unknown step '{call}'.", nameof(call));
        }
        return (false, null);
    }

    private static JsonObject Numbers(IReadOnlyDictionary<string, double> values)
    {
        var o = new JsonObject();
        foreach (var (key, value) in values)
            o[key] = value;
        return o;
    }

    private static JsonObject WithoutEndedAt(JsonObject entry)
    {
        var copy = entry.DeepClone().AsObject();
        copy.Remove("ended_at");
        return copy;
    }

    private sealed class SetClock : TimeProvider
    {
        public double Now { get; set; }

        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddSeconds(Now);
    }
}
