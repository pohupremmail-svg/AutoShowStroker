using System.Text.Json.Nodes;
using Gooner.Core.Callouts;
using Gooner.Core.Data;
using Gooner.Core.Randomness;

namespace Gooner.Parity.Tests;

// src/CalloutHandler.py vs Gooner.Core.Callouts.CalloutHandler: the shipped files read as the same
// languages, tones and line counts; the same scripted dice say the same lines; phrase files load,
// refuse and unload the same way.
public class CalloutsParity
{
    private const string Module = "callouts";

    public static IEnumerable<object[]> Drawing() => Golden.Cases(Module, "draws");
    public static IEnumerable<object[]> PhraseFiles() => Golden.Cases(Module, "custom_files");

    [Fact]
    public void structure()
    {
        var expected = Golden.Load(Module)["structure"]!;
        var handler = new CalloutHandler(Golden.RepoPath("res/callouts"));
        var counts = new JsonObject();
        foreach (var (lang, tones) in handler.CalloutData)
        {
            var perTone = new JsonObject();
            foreach (var (tone, categories) in tones)
            {
                var perKey = new JsonObject();
                foreach (var (key, phrases) in categories)
                    perKey[key] = phrases.Count;
                perTone[tone] = perKey;
            }
            counts[lang] = perTone;
        }
        var labels = new JsonObject();
        foreach (var (tone, _) in expected["labels"]!.AsObject())
            labels[tone] = CalloutHandler.ToneLabel(tone);
        var actual = new JsonObject { ["snapshot"] = Snapshot(handler), ["counts"] = counts, ["labels"] = labels };

        Assert.True(Golden.Equivalent(expected, actual), $"expected {expected.ToJsonString()}\nactual   {actual.ToJsonString()}");
    }

    [Theory]
    [MemberData(nameof(Drawing))]
    public void draws(string name, string json)
    {
        var c = JsonNode.Parse(json)!;
        var dir = Directory.CreateTempSubdirectory("gooner-parity-callouts-");
        try
        {
            var root = Path.Combine(dir.FullName, "callouts");
            WriteSyntheticCallouts(root);
            var random = new Replayed(c["uniforms"]!.AsArray(), c["choices"]!.AsArray());
            var clock = new SetClock();
            var handler = new CalloutHandler(root, random: random, time: clock);
            var said = new JsonArray();
            handler.TeaseShown += tease => said.Add(tease);
            foreach (var step in c["steps"]!.AsArray())
            {
                var s = step!.AsArray();
                switch ((string)s[0]!)
                {
                    case "hide":
                        clock.Now += 1000;
                        handler.Tick();
                        break;
                    case "set" when (string)s[1]! == "active_callout": handler.ActiveCallout = (bool)s[2]!; break;
                    case "set" when (string)s[1]! == "talking_chance": handler.TalkingChance = s[2]!.GetValue<double>(); break;
                    case "set_tones": handler.SetTones(s[1]!.AsArray().Select(t => (string)t!)); break;
                    case "set_lang": handler.SetLang((string)s[1]!); break;
                    case "select_and_output_sentence": handler.SelectAndOutputSentence((string)s[1]!); break;
                    case "force_output_sentence": handler.ForceOutputSentence((string)s[1]!); break;
                    case "beat_change_general": handler.BeatChangeGeneral(s[1]!.GetValue<double>(), (string?)s[2]); break;
                    default: throw new ArgumentException($"Unknown step '{s[0]}'.", nameof(json));
                }
                said.Add(new JsonArray("is_teasing", handler.IsTeasing));
            }
            var actual = new JsonObject
            {
                ["said"] = said,
                ["snapshot"] = Snapshot(handler),
                ["left"] = new JsonObject { ["uniforms"] = random.UniformsLeft, ["choices"] = random.ChoicesLeft },
            };
            Assert.True(Golden.Equivalent(c["expected"], actual), $"{name}:\nexpected {c["expected"]!.ToJsonString()}\nactual   {actual.ToJsonString()}");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(PhraseFiles))]
    public void custom_files(string name, string json)
    {
        var c = JsonNode.Parse(json)!;
        var dir = Directory.CreateTempSubdirectory("gooner-parity-phrases-");
        try
        {
            var root = dir.FullName;
            WriteSyntheticCallouts(Path.Combine(root, "callouts"));
            foreach (var (fileName, data) in c["files"]!.AsObject())
                File.WriteAllText(Path.Combine(root, fileName), data!.ToJsonString());
            var store = new UserDataStore(Path.Combine(root, "appdata"));
            var handler = new CalloutHandler(Path.Combine(root, "callouts"), dataStore: store);
            var results = new JsonArray();
            foreach (var step in c["steps"]!.AsArray())
            {
                var s = step!.AsArray();
                try
                {
                    switch ((string)s[0]!)
                    {
                        case "load": handler.LoadCustomFile(Path.Combine(root, (string)s[1]!), (string)s[2]!, (string?)s[3]); break;
                        case "unload": handler.UnloadCustomFile(Path.Combine(root, (string)s[1]!)); break;
                        case "clear": handler.ClearCustomPhraseFiles(); break;
                        case "reopen": handler = new CalloutHandler(Path.Combine(root, "callouts"), dataStore: store); break;
                        default: throw new ArgumentException($"Unknown step '{s[0]}'.", nameof(json));
                    }
                    results.Add("ok");
                }
                catch (PhraseFileException error)
                {
                    var message = error.Message.Replace(root, "<dir>", StringComparison.Ordinal);
                    foreach (var marker in new[] { "Couldn't read", "isn't valid JSON" })
                        if (message.Contains(marker, StringComparison.Ordinal))
                            message = message[..(message.IndexOf(marker, StringComparison.Ordinal) + marker.Length)];
                    results.Add(message);
                }
            }

            JsonArray Anonymous(IEnumerable<JsonObject> entries) => new([.. entries.Select(e =>
            {
                var copy = e.DeepClone().AsObject();
                copy["path"] = ((string)copy["path"]!).Replace(root, "<dir>", StringComparison.Ordinal);
                return (JsonNode)copy;
            })]);

            var saved = store.Load("custom_phrase_files") as JsonArray;
            var actual = new JsonObject
            {
                ["results"] = results,
                ["data"] = Data(handler),
                ["snapshot"] = Snapshot(handler),
                ["custom_phrase_files"] = Anonymous(handler.CustomPhraseFiles.Select(f => new JsonObject { ["path"] = f.Path, ["lang"] = f.Lang, ["tone"] = f.Tone })),
                ["saved"] = saved is null ? null : Anonymous(saved.Select(e => e!.AsObject())),
            };
            Assert.True(Golden.Equivalent(c["expected"], actual), $"{name}:\nexpected {c["expected"]!.ToJsonString()}\nactual   {actual.ToJsonString()}");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    // Mirrors parity/generate.py's _write_synthetic_callouts().
    private static void WriteSyntheticCallouts(string root)
    {
        var sizes = new (string Lang, string Tone, int Size)[]
        {
            ("en", "flirty", 3), ("en", "shy", 1), ("en", "degrading", 2), ("en", "zzz_extra", 1),
            ("de", "flirty", 2), ("de", "shy", 2),
        };
        foreach (var (lang, tone, size) in sizes)
        {
            Directory.CreateDirectory(Path.Combine(root, lang));
            var data = new JsonObject();
            foreach (var key in CalloutHandler.TriggerKeys)
                data[key] = new JsonArray([.. Enumerable.Range(0, size).Select(i => (JsonNode)$"{lang} {tone} {key} {i}")]);
            File.WriteAllText(Path.Combine(root, lang, tone + ".json"), data.ToJsonString());
        }
        File.WriteAllText(Path.Combine(root, "de", "degrading.json"), "{broken");
    }

    // Mirrors parity/generate.py's _snapshot().
    private static JsonObject Snapshot(CalloutHandler handler)
    {
        static JsonArray List(IEnumerable<string> items) => new([.. items.Select(i => (JsonNode)i)]);
        var tonesFor = new JsonObject();
        foreach (var lang in handler.AvailableLanguages)
            tonesFor[lang] = List(handler.TonesFor(lang));
        return new JsonObject
        {
            ["lang"] = handler.Lang,
            ["available_languages"] = List(handler.AvailableLanguages),
            ["available_tones"] = List(handler.AvailableTones),
            ["tones_for"] = tonesFor,
            ["selected_tones"] = List(handler.SelectedTones),
            ["usable_tones"] = List(handler.UsableTones()),
        };
    }

    private static JsonObject Data(CalloutHandler handler)
    {
        var data = new JsonObject();
        foreach (var (lang, tones) in handler.CalloutData)
        {
            var perTone = new JsonObject();
            foreach (var (tone, categories) in tones)
            {
                var perKey = new JsonObject();
                foreach (var (key, phrases) in categories)
                    perKey[key] = new JsonArray([.. phrases.Select(p => (JsonNode)p)]);
                perTone[tone] = perKey;
            }
            data[lang] = perTone;
        }
        return data;
    }

    // The draws the Python side was scripted with, in the same order; a choice index is taken
    // modulo the length of what is chosen from, as there.
    private sealed class Replayed(JsonArray uniforms, JsonArray choices) : IRandomSource
    {
        private readonly Queue<double> _uniforms = new(uniforms.Select(u => u!.GetValue<double>()));
        private readonly Queue<int> _choices = new(choices.Select(i => i!.GetValue<int>()));

        public int UniformsLeft => _uniforms.Count;
        public int ChoicesLeft => _choices.Count;

        public double NextDouble() => _uniforms.Dequeue();

        public int NextIndex(int count) => _choices.Dequeue() % count;
    }

    private sealed class SetClock : TimeProvider
    {
        public double Now { get; set; } = 1_000_000;

        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddSeconds(Now);
    }
}
