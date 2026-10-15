using System.Text.Json.Nodes;
using Gooner.Core.Beat;
using Gooner.Core.Randomness;
using Gooner.Core.Sessions;
using Gooner.Core.Timing;

namespace Gooner.Parity.Tests;

// src/BeatHandler.py vs Gooner.Core.Beat.BeatHandler: whole sessions on a set clock with the same
// dice, every event compared with the moment it happened - each note scheduled and heard, each
// segment, pause and plan change - plus what the lookahead predicts at chosen moments.
public class BeatHandlerParity
{
    private const string Module = "beat_handler";

    public static IEnumerable<object[]> Sessions() => Golden.Cases(Module, "sessions");

    [Theory]
    [MemberData(nameof(Sessions))]
    public void session(string name, string json)
    {
        var c = JsonNode.Parse(json)!;
        var expected = c["expected"]!.AsArray();
        var actual = Play(c["config"]!.AsObject(), c["steps"]!.AsArray(), c["draws"]!.AsArray());

        // Compared event by event, so a failure names the first moment the two part ways.
        for (var i = 0; i < Math.Min(expected.Count, actual.Count); i++)
            Assert.True(Golden.Equivalent(expected[i], actual[i]),
                $"{name}: event {i} differs\nexpected {expected[i]!.ToJsonString()}\nactual   {actual[i]!.ToJsonString()}");
        Assert.True(expected.Count == actual.Count, $"{name}: {expected.Count} events expected, {actual.Count} happened");
    }

    // Mirrors parity/generate.py's _play_beat().
    private static JsonArray Play(JsonObject config, JsonArray steps, JsonArray draws)
    {
        var clock = new SetClock { Now = steps[0]![1]!.GetValue<double>() };
        var handler = new BeatHandler(random: new Replayed(draws), clock: clock);
        foreach (var (key, value) in config)
            Set(handler, key, value);

        var log = new JsonArray();
        void Note(string what, params JsonNode?[] args) => log.Add(new JsonArray([handler.ClockNow, what, .. args]));

        handler.BeatPaused += () => Note("paused");
        handler.BeatResumed += () => Note("resumed");
        handler.BeatChanged += (freq, pattern) => Note("changed", freq, pattern);
        handler.BeatHit += () => Note("hit");
        handler.MeterUpdated += state => Note("meter", state);
        handler.SessionPlanned += (start, script) => Note("planned", start, script is not null);
        handler.PlanExtended += added => Note("extended", new JsonArray([.. added.Select(Segment)]));
        handler.SegmentStarted += s => Note("segment", Segment(s));
        handler.NoteScheduled += () => Note("scheduled", handler.NoteTimer.IntervalMs);

        foreach (var step in steps)
        {
            var s = step!.AsArray();
            clock.Now = s[1]!.GetValue<double>();
            handler.Advance();
            switch ((string)s[0]!)
            {
                case "start": handler.StartBeat(s.Count > 2 ? new SessionScript(s[2]!.DeepClone().AsObject()) : null); break;
                case "edge": Note("edge_given", handler.EdgeRelief()); break;
                case "finale": handler.SetFinaleAt(s[2]!.GetValue<double>()); break;
                case "hold": handler.HoldFinalSegment(); break;
                case "replan": handler.ReplanFromNextSegment(); break;
                case "set": Set(handler, (string)s[2]!, s[3]); break;
                case "stop": handler.Stop(); break;
                case "advance": break;
                case "probe":
                    Note("probe",
                        new JsonArray([.. handler.UpcomingBeats(s[2]!.GetValue<double>()).Select(b => (JsonNode)new JsonArray(b.Offset, b.Audible, b.Weight))]),
                        handler.PauseProgress(),
                        handler.IsPaused(),
                        Segment(handler.CurrentSegment),
                        new JsonArray([.. handler.PlannedSegments.Select(Segment)]),
                        handler.FinaleAt,
                        handler.RampCompleteAt);
                    break;
                default: throw new ArgumentException($"Unknown step '{s[0]}'.", nameof(steps));
            }
        }
        return log;
    }

    private static JsonNode? Segment(Segment? s) =>
        s is null ? null : new JsonArray(s.Kind, s.DurationSec, s.Freq, s.PatternName, s.Index);

    private static void Set(BeatHandler handler, string key, JsonNode? value)
    {
        double D() => value!.GetValue<double>();
        int I() => (int)value!.GetValue<double>();
        switch (key)
        {
            case "min_beat_dur": handler.MinBeatDur = D(); break;
            case "max_beat_dur": handler.MaxBeatDur = D(); break;
            case "min_beat_freq": handler.MinBeatFreq = D(); break;
            case "max_beat_freq": handler.MaxBeatFreq = D(); break;
            case "min_pause_dur": handler.MinPauseDur = I(); break;
            case "max_pause_dur": handler.MaxPauseDur = I(); break;
            case "pause_chance": handler.PauseChance = D(); break;
            case "edge_pause_dur": handler.EdgePauseDur = I(); break;
            case "ramping_active": handler.RampingActive = (bool)value!; break;
            case "min_ramp_duration": handler.MinRampDuration = D(); break;
            case "max_ramp_duration": handler.MaxRampDuration = D(); break;
            case "ramp_window_width": handler.RampWindowWidth = D(); break;
            case "selected_beat_patterns": handler.SelectedBeatPatterns = [.. value!.AsArray().Select(n => (string)n!)]; break;
            default: throw new ArgumentException($"Unknown setting '{key}'.", nameof(key));
        }
    }

    // The draws the Python side recorded, in order. Asking for an index below a different n than
    // Python did means the two have already parted ways - said here, at the draw.
    private sealed class Replayed(JsonArray draws) : IRandomSource
    {
        private readonly Queue<JsonArray> _draws = new(draws.Select(d => d!.AsArray()));

        public double NextDouble()
        {
            var draw = Next("u");
            return draw[1]!.GetValue<double>();
        }

        public int NextIndex(int count)
        {
            var draw = Next("i");
            var n = draw[1]!.GetValue<int>();
            if (n != count)
                throw new InvalidOperationException($"Python drew an index below {n} here, the port below {count}.");
            return draw[2]!.GetValue<int>();
        }

        private JsonArray Next(string kind)
        {
            if (_draws.Count == 0)
                throw new InvalidOperationException($"The port draws ({kind}) where Python had stopped drawing.");
            var draw = _draws.Dequeue();
            if ((string)draw[0]! != kind)
                throw new InvalidOperationException($"Python drew {draw.ToJsonString()} here, the port asked for '{kind}'.");
            return draw;
        }
    }

    private sealed class SetClock : ISessionClock
    {
        public double Now { get; set; }
    }
}
