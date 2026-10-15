using System.Text.Json.Nodes;
using Gooner.Core.Beat;
using Gooner.Core.Sessions;

namespace Gooner.Parity.Tests;

// src/SessionRecorder.py vs Gooner.Core.Sessions.SessionRecorder: the same recordings stitched
// into timelines, and the last one turned into a saved session - the whole way from what played
// to what lands in the file.
public class SessionRecorderParity
{
    private const string Module = "session_recorder";

    public static IEnumerable<object[]> Recordings() => Golden.Cases(Module, "record");

    [Theory]
    [MemberData(nameof(Recordings))]
    public void record(string name, string json)
    {
        var c = JsonNode.Parse(json)!;
        var actual = Record(c["steps"]!.AsArray());
        Assert.True(Golden.Equivalent(c["expected"], actual), $"{name}:\nexpected {c["expected"]!.ToJsonString()}\nactual   {actual.ToJsonString()}");
    }

    // Mirrors parity/generate.py's _record().
    private static JsonObject Record(JsonArray steps)
    {
        static double? D(JsonNode? n) => n?.GetValue<double>();

        var recorder = new SessionRecorder();
        var timelines = new List<SessionTimeline>();
        foreach (var step in steps)
        {
            var s = step!.AsArray();
            var at = D(s[^1]);
            switch ((string)s[0]!)
            {
                case "timeline": timelines.Add(recorder.Timeline(now: at)); break;
                case "segment":
                    recorder.SegmentStarted(new Segment((string)s[1]!, s[2]!.GetValue<double>(), D(s[3]), (string?)s[4], s[5]!.GetValue<int>()), at);
                    break;
                case "session_started": recorder.SessionStarted(at); break;
                case "session_ended": recorder.SessionEnded(at); break;
                case "media_shown": recorder.MediaShown((string)s[1]!, at); break;
                case "fake_climax_recorded": recorder.FakeClimaxRecorded(at); break;
                case "climax_recorded": recorder.ClimaxRecorded((string?)s[1], at); break;
                default: throw new ArgumentException($"Unknown step '{s[0]}'.", nameof(steps));
            }
        }

        var saved = SessionFiles.ToSavedSession(timelines[^1], new Dictionary<string, int[]> { ["My Rhythm"] = [1, 0, 1] }, "x", DateTime.Now);
        saved.Remove("app_version");
        saved.Remove("saved_at");
        return new JsonObject
        {
            ["timelines"] = new JsonArray([.. timelines.Select(t => (JsonNode)ToJson(t))]),
            ["saved"] = saved,
        };
    }

    // The timeline in the dict shape Python hands over.
    private static JsonObject ToJson(SessionTimeline t) => new()
    {
        ["started_at"] = t.StartedAt,
        ["ended_at"] = t.EndedAt,
        ["climax_at"] = t.ClimaxAt,
        ["climax_outcome"] = t.ClimaxOutcome,
        ["fake_climaxes"] = new JsonArray([.. t.FakeClimaxes.Select(f => (JsonNode)f)]),
        ["segments"] = new JsonArray([.. t.Segments.Select(s => (JsonNode)new JsonObject
        {
            ["kind"] = s.Kind,
            ["pattern"] = s.Pattern,
            ["freq"] = s.Freq,
            ["start"] = s.Start,
            ["end"] = s.End,
            ["planned_sec"] = s.PlannedSec,
            ["media"] = new JsonArray([.. s.Media.Select(m => (JsonNode)new JsonObject
            {
                ["path"] = m.Path,
                ["start"] = m.Start,
                ["end"] = m.End,
                ["carried_over"] = m.CarriedOver,
            })]),
        })]),
    };
}
