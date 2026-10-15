using System.Text.Json.Nodes;
using Gooner.Core.Formatting;
using Gooner.Core.Sessions;

namespace Gooner.Parity.Tests;

// src/session_files.py vs Gooner.Core.Sessions.SessionFiles, case by case.
public class SessionFilesParity
{
    private const string Module = "session_files";

    public static IEnumerable<object[]> Validate() => Golden.Cases(Module, "validate");
    public static IEnumerable<object[]> Saving() => Golden.Cases(Module, "to_saved_session");
    public static IEnumerable<object[]> Describing() => Golden.Cases(Module, "describe");
    public static IEnumerable<object[]> Reading() => Golden.Cases(Module, "read_session_file");

    [Theory]
    [MemberData(nameof(Validate))]
    public void validate_session(string name, string json)
    {
        var c = JsonNode.Parse(json)!;
        var actual = SessionFiles.ValidateSession(c["input"]!.AsObject());
        var expected = c["expected"]!.AsArray().Select(p => (string)p!).ToList();
        if (Golden.Allowed(Module + ".validate", name) is { } reason)
        {
            // A listed deviation still has to *be* a deviation - once the two agree, the entry is
            // stale and should go.
            Assert.False(expected.SequenceEqual(actual), $"'{name}' is allowlisted but no longer differs: {reason}");
            return;
        }
        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(Saving))]
    public void to_saved_session(string name, string json)
    {
        var c = JsonNode.Parse(json)!;
        var patterns = c["custom_patterns"]!.AsObject().ToDictionary(
            kv => kv.Key, kv => kv.Value!.AsArray().Select(s => s!.GetValue<int>()).ToArray());

        var saved = SessionFiles.ToSavedSession(Timeline(c["timeline"]!), patterns, "x", DateTime.Now);
        saved.Remove("app_version");
        saved.Remove("saved_at");

        Assert.True(Golden.Equivalent(c["expected"], saved), $"{name}:\nexpected {c["expected"]!.ToJsonString()}\nactual   {saved.ToJsonString()}");
    }

    [Theory]
    [MemberData(nameof(Describing))]
    public void describe(string name, string json)
    {
        var c = JsonNode.Parse(json)!;
        var expected = (string)c["expected"]!;
        var actual = SessionFiles.Describe(c["input"]!.AsObject());
        Assert.True(expected == actual, $"{name}:\nexpected {expected}\nactual   {actual}");
    }

    [Theory]
    [MemberData(nameof(Reading))]
    public void read_session_file(string name, string json)
    {
        var c = JsonNode.Parse(json)!;
        var path = Path.Combine(Path.GetTempPath(), $"gooner-parity-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, (string)c["text"]!);
        try
        {
            if (c["expected"]!["refused"] is { } refused)
            {
                var error = Assert.Throws<UnsupportedSessionFileException>(() => SessionFiles.ReadSessionFile(path));
                Assert.Equal((string)refused!, error.Message);
            }
            else
            {
                Assert.True(Golden.Equivalent(c["expected"]!["ok"], SessionFiles.ReadSessionFile(path)), name);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void format_clock()
    {
        foreach (var pair in Golden.Load(Module)["format_clock"]!.AsArray())
            Assert.Equal((string)pair![1]!, Clock.FormatClock(pair[0]!.GetValue<double>()));
    }

    [Fact]
    public void format_duration()
    {
        foreach (var pair in Golden.Load(Module)["format_duration"]!.AsArray())
            Assert.Equal((string)pair![1]!, Clock.FormatDuration(pair[0]!.GetValue<double>()));
    }

    private static SessionTimeline Timeline(JsonNode t)
    {
        static double? D(JsonNode? n) => n?.GetValue<double>();
        return new SessionTimeline(
            D(t["started_at"]),
            D(t["ended_at"]),
            D(t["climax_at"]),
            (string?)t["climax_outcome"],
            t["fake_climaxes"]!.AsArray().Select(n => n!.GetValue<double>()).ToList(),
            t["segments"]!.AsArray().Select(s => new RecordedSegment(
                (string)s!["kind"]!,
                (string?)s["pattern"],
                D(s["freq"]),
                s["start"]!.GetValue<double>(),
                s["end"]!.GetValue<double>(),
                s["media"]!.AsArray().Select(m => new RecordedMedium(
                    (string)m!["path"]!, m["start"]!.GetValue<double>(), m["end"]!.GetValue<double>(), (bool)m["carried_over"]!)).ToList(),
                D(s["planned_sec"]))).ToList());
    }
}
