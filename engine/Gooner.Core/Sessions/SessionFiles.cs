using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gooner.Core.Beat;
using Gooner.Core.Data;
using Gooner.Core.Formatting;
using Gooner.Core.Logging;

namespace Gooner.Core.Sessions;

// The file is not a session this build can replay - missing, corrupt, invalid, or newer.
public sealed class UnsupportedSessionFileException : Exception
{
    public UnsupportedSessionFileException() { }

    public UnsupportedSessionFileException(string message) : base(message) { }

    public UnsupportedSessionFileException(string message, Exception inner) : base(message, inner) { }
}

// Port of src/session_files.py - reading and writing saved sessions.
//
// A saved session stays a JsonObject here, as it is a dict on the Python side: the validator has
// to see wrongly typed values as they are in the file, and its messages are compared word for
// word with the Python ones. What the planner reads is typed (SessionScript).
//
// Every time in the file is seconds from the start of the session, never a wall clock. And note
// what this writes: media file paths - the one thing the app otherwise keeps off disk entirely.
// It happens because the user pressed Save; it is disclosed and deletable under Privacy & Data,
// and StripPaths exists so a session can be handed to someone else without the sender's account
// name, folder layout and file names.
public static class SessionFiles
{
    private const string Log = "session_files";

    // Bumped whenever the shape changes incompatibly. ReadSessionFile refuses anything newer
    // rather than half-reading it into a session that then behaves oddly.
    public const int FormatVersion = 1;

    public const string SavedSessionsKey = "saved_sessions";

    // Far below the session history's cap on purpose: a history entry is a handful of numbers, a
    // saved session carries every segment and every medium it showed.
    public const int MaxSavedSessions = 50;

    public static readonly IReadOnlyList<string> SegmentKinds = ["beat", "pause", "finale"];
    public static readonly IReadOnlyList<string> ClimaxOutcomes = ["real", "ruined", "denied"];

    private static readonly Dictionary<string, string> OutcomeLabels = new()
    {
        ["real"] = "Climax",
        ["ruined"] = "Ruined",
        ["denied"] = "Denied",
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // --- saving ---

    public static JsonObject ToSavedSession(
        SessionTimeline timeline,
        IReadOnlyDictionary<string, int[]>? customPatterns,
        string appVersion,
        DateTime savedAt)
    {
        var start = timeline.StartedAt ?? 0.0;
        var recorded = timeline.Segments;

        var segments = new JsonArray();
        for (var index = 0; index < recorded.Count; index++)
        {
            var data = recorded[index];
            segments.Add(new JsonObject
            {
                ["kind"] = data.Kind,
                ["pattern"] = data.Pattern,
                ["freq"] = data.Freq,
                ["duration_sec"] = LengthToReplay(data, isLast: index == recorded.Count - 1),
            });
        }

        JsonObject? climax = null;
        if (timeline.ClimaxAt is { } climaxAt)
            climax = new JsonObject { ["at_sec"] = climaxAt - start, ["outcome"] = timeline.ClimaxOutcome };

        return new JsonObject
        {
            ["format"] = FormatVersion,
            ["app_version"] = appVersion,
            ["saved_at"] = savedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            ["duration_sec"] = (timeline.EndedAt ?? start) - start,
            ["segments"] = segments,
            ["custom_patterns"] = PatternsUsed(timeline, customPatterns ?? new Dictionary<string, int[]>()),
            ["climax"] = climax,
            ["fake_climaxes"] = new JsonArray(timeline.FakeClimaxes.Select(at => (JsonNode?)(at - start)).ToArray()),
            ["media"] = MediaScript(timeline, start),
        };
    }

    // The planned length, not the measured one - except for the last segment. A segment runs to
    // the first note past its planned end, so what it measured is always a little more than what
    // it was planned to be; handing the measured figure back to the planner makes the replay
    // overshoot a second time. The last segment is the exception: the climax holds it open, or
    // the user stopped partway through it, so there what it actually ran is the truth.
    private static double LengthToReplay(RecordedSegment data, bool isLast)
    {
        var measured = data.End - data.Start;
        return isLast ? measured : data.PlannedSec ?? measured;
    }

    // Only the custom rhythms this session actually played. Without the definitions a replay on
    // another machine has nothing to play; with *all* of them it would quietly hand over the
    // user's whole pattern library.
    private static JsonObject PatternsUsed(SessionTimeline timeline, IReadOnlyDictionary<string, int[]> customPatterns)
    {
        var used = timeline.Segments.Where(s => s.Pattern is not null).Select(s => s.Pattern!).ToHashSet();
        var result = new JsonObject();
        foreach (var (name, steps) in customPatterns)
            if (used.Contains(name))
                result[name] = new JsonArray(steps.Select(step => (JsonNode?)step).ToArray());
        return result;
    }

    // Each medium once, in the order it came up. The timeline lists a medium again under every
    // segment it spanned, which is right for the explorer and wrong here.
    private static JsonArray MediaScript(SessionTimeline timeline, double start)
    {
        var script = new JsonArray();
        foreach (var data in timeline.Segments)
            foreach (var entry in data.Media)
                if (!entry.CarriedOver)
                    script.Add(new JsonObject { ["at_sec"] = entry.Start - start, ["path"] = entry.Path });
        return script;
    }

    // A copy with every media path removed, keeping the timings: it replays the session's
    // difficulty and pacing against whatever collection the other person has.
    public static JsonObject StripPaths(JsonObject saved)
    {
        var stripped = saved.DeepClone().AsObject();
        var media = new JsonArray();
        if (saved["media"] is JsonArray entries)
            foreach (var entry in entries)
                media.Add(new JsonObject { ["at_sec"] = entry?["at_sec"]?.DeepClone() });
        stripped["media"] = media;
        return stripped;
    }

    public static bool HasPaths(JsonObject saved) =>
        saved["media"] is JsonArray media && media.Any(entry => entry is JsonObject obj && obj.ContainsKey("path"));

    // --- validating ---

    // Everything wrong with a session, in plain language; empty means it will play. A session is
    // not only ever written by the app - the format is documented so an LLM can compose one -
    // and without this the mistakes would surface during the session. Collects rather than
    // stopping at the first problem: somebody iterating on a generated file wants the whole list.
    public static List<string> ValidateSession(JsonObject saved)
    {
        if (saved["segments"] is not JsonArray segments || segments.Count == 0)
            return ["The session has no segments."];

        var problems = new List<string>();
        var durationNode = saved["duration_sec"];
        double duration;
        if (!PyJson.IsNumber(durationNode) || PyJson.Number(durationNode) <= 0)
        {
            problems.Add("duration_sec has to be a positive number of seconds.");
            duration = double.PositiveInfinity;
        }
        else
        {
            duration = PyJson.Number(durationNode);
        }

        var known = BeatPatterns.BuiltIn.Keys.ToHashSet();
        known.UnionWith(ValidatedPatterns(saved, problems));
        for (var index = 0; index < segments.Count; index++)
            problems.AddRange(SegmentProblems(segments[index], index + 1, known));

        problems.AddRange(ClimaxProblems(saved["climax"], duration));
        problems.AddRange(MomentProblems(Or(saved["fake_climaxes"]), duration, "fake_climaxes"));
        problems.AddRange(MediaProblems(Or(saved["media"]), duration));
        return problems;
    }

    // `value or []`
    private static JsonNode Or(JsonNode? node) => PyJson.Truthy(node) ? node! : new JsonArray();

    private static IEnumerable<string> ValidatedPatterns(JsonObject saved, List<string> problems)
    {
        var node = saved["custom_patterns"];
        if (!PyJson.Truthy(node)) return [];
        if (node is not JsonObject custom)
        {
            problems.Add("custom_patterns has to be an object of name -> list of steps.");
            return [];
        }
        foreach (var (name, steps) in custom)
        {
            if (steps is not JsonArray list || list.Count == 0)
            {
                problems.Add($"The rhythm '{name}' has no steps.");
            }
            else if (!list.All(step => PyJson.IsInteger(step) && PyJson.Number(step) != 0 && Math.Abs(PyJson.Number(step)) <= 4))
            {
                // Same rule the pattern editor enforces: 1-4 for a beat, negative for a silent
                // step of the same length, and never 0.
                problems.Add($"The rhythm '{name}' has steps outside 1..4 (negative for silence, never 0).");
            }
        }
        // Every name counts as known, playable or not - the broken one is reported above.
        return custom.Select(kv => kv.Key);
    }

    private static List<string> SegmentProblems(JsonNode? node, int index, HashSet<string> knownPatterns)
    {
        var where = $"Segment {index}";
        if (node is not JsonObject segment) return [$"{where} is not an object."];

        var problems = new List<string>();
        var kind = segment["kind"];
        var kindText = kind is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;
        if (kindText is null || !SegmentKinds.Contains(kindText))
            problems.Add($"{where} has kind '{PyJson.Str(kind)}' - expected one of {string.Join(", ", SegmentKinds)}.");

        var duration = segment["duration_sec"];
        if (!PyJson.IsNumber(duration) || PyJson.Number(duration) <= 0)
            problems.Add($"{where} needs a positive duration_sec.");

        var pattern = segment["pattern"];
        var freq = segment["freq"];
        if (kindText == "pause")
        {
            if (pattern is not null || freq is not null)
                problems.Add($"{where} is a pause, so it carries no pattern and no freq.");
            return problems;
        }

        if (!PyJson.IsNumber(freq) || PyJson.Number(freq) <= 0)
            problems.Add($"{where} needs a freq in beats per second.");
        if (pattern is null)
        {
            problems.Add($"{where} needs a pattern name.");
        }
        else
        {
            var name = pattern is JsonValue p && p.GetValueKind() == JsonValueKind.String ? p.GetValue<string>() : null;
            if (name is null || !knownPatterns.Contains(name))
                problems.Add($"{where} plays '{PyJson.Str(pattern)}', which is neither built in nor defined in custom_patterns.");
        }
        return problems;
    }

    private static List<string> ClimaxProblems(JsonNode? node, double duration)
    {
        if (node is null) return []; // stopped before one - see Describe
        if (node is not JsonObject climax) return ["climax has to be an object with at_sec and outcome."];

        var problems = new List<string>();
        var at = climax["at_sec"];
        if (!PyJson.IsNumber(at) || PyJson.Number(at) < 0)
            problems.Add("The climax needs an at_sec in seconds from the start.");
        else if (PyJson.Number(at) > duration)
            problems.Add($"The climax is at {PyJson.F0(PyJson.Number(at))}s but the session is only {PyJson.F0(duration)}s long, so it would never arrive.");

        var outcome = climax["outcome"];
        var outcomeText = outcome is JsonValue o && o.GetValueKind() == JsonValueKind.String ? o.GetValue<string>() : null;
        if (outcomeText is null || !ClimaxOutcomes.Contains(outcomeText))
            problems.Add($"The climax outcome '{PyJson.Str(outcome)}' is not one of {string.Join(", ", ClimaxOutcomes)}.");
        return problems;
    }

    private static List<string> MomentProblems(JsonNode moments, double duration, string field)
    {
        if (moments is not JsonArray list) return [$"{field} has to be a list of seconds."];
        foreach (var moment in list)
            if (!PyJson.IsNumber(moment) || PyJson.Number(moment) < 0 || PyJson.Number(moment) > duration)
                return [$"{field} contains {PyJson.Str(moment)}, which is outside the session."];
        return [];
    }

    private static List<string> MediaProblems(JsonNode media, double duration)
    {
        if (media is not JsonArray list) return ["media has to be a list."];
        var previous = -1.0;
        foreach (var entry in list)
        {
            if (entry is not JsonObject obj || !PyJson.IsNumber(obj["at_sec"]))
                return ["Every media entry needs an at_sec in seconds from the start."];
            var at = PyJson.Number(obj["at_sec"]);
            if (at < previous) return ["The media moments have to run forwards - one of them goes backwards."];
            if (at > duration) return [$"A medium is shown at {PyJson.F0(at)}s, past the end of the session."];
            previous = at;
        }
        return [];
    }

    // --- reading and writing files ---

    // Loads a saved session or throws UnsupportedSessionFileException - one exception for every
    // way this can fail, because the caller shows the same "cannot be replayed" either way and
    // the specifics belong in the log.
    public static JsonObject ReadSessionFile(string path)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(File.ReadAllText(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Warning(Log, "Could not read the session file: {0}", e.Message);
            throw new UnsupportedSessionFileException("The file could not be read.", e);
        }

        // A format that is not a number is refused as "not a session" (Python raises a TypeError
        // there instead; recorded in parity/allowlist.json).
        if (node is not JsonObject data || !data.ContainsKey("format") || !data.ContainsKey("segments") || !PyJson.IsNumber(data["format"]))
            throw new UnsupportedSessionFileException("That is not a saved session.");
        if (PyJson.Number(data["format"]) > FormatVersion)
            throw new UnsupportedSessionFileException("That session was saved by a newer version of GoonerApp.");

        var problems = ValidateSession(data);
        if (problems.Count > 0)
        {
            AppLog.Warning(Log, "Refused a session file with {0} problem(s)", problems.Count);
            throw new UnsupportedSessionFileException(
                "That session cannot be played:\n\n" + string.Join("\n", problems.Select(p => "- " + p)));
        }
        return data;
    }

    // Exports one session to a file the user picked; returns whether it landed and never throws -
    // a read-only stick should get a "could not write that" box, not a crash.
    public static bool WriteSessionFile(string path, JsonObject saved)
    {
        try
        {
            File.WriteAllText(path, saved.ToJsonString(WriteOptions));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Error(Log, "Could not write the session file: {0}", e.Message);
            return false;
        }
    }

    // One line for the saved-sessions list.
    public static string Describe(JsonObject saved)
    {
        var duration = PyJson.Truthy(saved["duration_sec"]) && PyJson.IsNumber(saved["duration_sec"]) ? PyJson.Number(saved["duration_sec"]) : 0.0;
        var parts = new List<string>
        {
            saved["saved_at"] is { } savedAt ? PyJson.Str(savedAt) : "?",
            Clock.FormatClock(duration),
        };
        if (PyJson.Truthy(saved["climax"]))
        {
            var outcome = saved["climax"]!["outcome"];
            var key = outcome is JsonValue o && o.GetValueKind() == JsonValueKind.String ? o.GetValue<string>() : null;
            parts.Add(key is not null && OutcomeLabels.TryGetValue(key, out var label) ? label : PyJson.Str(outcome));
        }
        else
        {
            // Worth calling out, because it replays differently: the recording plays out and the
            // session then carries on and draws a climax of its own.
            parts.Add("Stopped early");
        }
        var fakes = saved["fake_climaxes"] is JsonArray fakeList ? fakeList.Count : 0;
        if (fakes > 0) parts.Add($"{fakes} fake-out{(fakes > 1 ? "s" : "")}");
        if (!HasPaths(saved)) parts.Add("no media paths");
        return string.Join("  -  ", parts);
    }

    // --- the shelf of saved sessions ---

    // Every saved session, oldest first.
    public static JsonArray LoadSavedSessions(UserDataStore dataStore)
    {
        var stored = dataStore.Load(SavedSessionsKey);
        if (stored is null) return [];
        if (stored is not JsonArray list)
        {
            AppLog.Warning(Log, "The saved sessions file did not contain a list - ignoring it.");
            return [];
        }
        return list;
    }

    // Puts one session on the shelf, dropping the oldest once it is full.
    public static bool StoreSession(UserDataStore dataStore, JsonObject saved, int maxSaved = MaxSavedSessions)
    {
        var sessions = LoadSavedSessions(dataStore).Select(s => s?.DeepClone()).ToList();
        sessions.Add(saved.DeepClone());
        return dataStore.Save(SavedSessionsKey, new JsonArray(sessions.Skip(Math.Max(0, sessions.Count - maxSaved)).ToArray()));
    }

    // Removes one session from the shelf; returns whether there was one at that index.
    public static bool DeleteSavedSession(UserDataStore dataStore, int index)
    {
        var sessions = LoadSavedSessions(dataStore);
        if (index < 0 || index >= sessions.Count) return false;
        sessions.RemoveAt(index);
        dataStore.Save(SavedSessionsKey, sessions);
        return true;
    }

    // The media the session showed, in order. Empty when the paths were stripped.
    public static List<string> RecordedPaths(JsonObject saved) =>
        saved["media"] is JsonArray media
            ? media.OfType<JsonObject>().Where(e => e["path"] is not null).Select(e => PyJson.Str(e["path"])).ToList()
            : [];

    // Recorded media that is no longer where it was, so the caller can offer to replay against
    // another library instead of showing a session of empty frames.
    public static List<string> MissingPaths(JsonObject saved) =>
        RecordedPaths(saved).Where(path => !File.Exists(path) && !Directory.Exists(path)).ToList();
}
