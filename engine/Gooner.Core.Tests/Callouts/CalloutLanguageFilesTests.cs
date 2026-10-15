using System.Text.Json;
using System.Text.Json.Nodes;
using Gooner.Core.Callouts;
using Gooner.Core.Tests.Support;

namespace Gooner.Core.Tests.Callouts;

// Port of tests/test_callout_language_files.py: the schema of the shipped phrase files,
// res/callouts/<lang>/<tone>.json. A missing or typo'd trigger key doesn't raise - the app just
// goes quiet for that event, which is nearly impossible to notice by playing alone. Both apps read
// the same files, so this guards them for both; it outlives the Python suite.
public class CalloutLanguageFilesTests
{
    private static readonly string CalloutDir = Repo.PathTo("res/callouts");

    private static IEnumerable<string> LanguageDirs() =>
        Directory.GetDirectories(CalloutDir).Order(StringComparer.Ordinal);

    public static IEnumerable<object[]> ToneFiles() =>
        LanguageDirs()
            .SelectMany(dir => Directory.GetFiles(dir, "*.json").Order(StringComparer.Ordinal))
            .Select(path => new object[] { FileId(path) });

    private static string FileId(string path) => $"{Path.GetFileName(Path.GetDirectoryName(path))}/{Path.GetFileNameWithoutExtension(path)}";

    private static string PathOf(string fileId) => Path.Combine(CalloutDir, fileId + ".json");

    private static JsonObject Read(string fileId) => JsonNode.Parse(File.ReadAllText(PathOf(fileId)))!.AsObject();

    [Fact]
    public void at_least_one_language_exists() => Assert.NotEmpty(LanguageDirs());

    // A leftover res/callouts/en.json from the pre-tone layout would simply be ignored.
    [Fact]
    public void no_phrase_file_sits_outside_a_language_folder() =>
        Assert.Empty(Directory.GetFiles(CalloutDir, "*.json"));

    [Theory]
    [MemberData(nameof(ToneFiles))]
    public void phrase_file_is_valid_json(string file)
    {
        var parsed = JsonDocument.Parse(File.ReadAllText(PathOf(file)));
        Assert.NotNull(parsed);
    }

    [Theory]
    [MemberData(nameof(ToneFiles))]
    public void phrase_file_has_all_required_keys(string file)
    {
        var missing = CalloutHandler.TriggerKeys.Except(Read(file).Select(kv => kv.Key)).Order(StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, $"{file} is missing required trigger keys: {string.Join(", ", missing)}");
    }

    [Theory]
    [MemberData(nameof(ToneFiles))]
    public void phrase_file_has_no_unknown_keys(string file)
    {
        var unknown = Read(file).Select(kv => kv.Key).Except(CalloutHandler.TriggerKeys).Order(StringComparer.Ordinal).ToList();
        Assert.True(unknown.Count == 0, $"{file} has unknown trigger keys (typo?): {string.Join(", ", unknown)}");
    }

    [Theory]
    [MemberData(nameof(ToneFiles))]
    public void phrase_file_values_are_lists_of_strings(string file)
    {
        foreach (var (key, phrases) in Read(file))
        {
            Assert.True(phrases is JsonArray, $"{file}:{key} must be a list");
            var items = phrases!.AsArray();
            for (var i = 0; i < items.Count; i++)
                Assert.True(items[i] is JsonValue v && v.GetValueKind() == JsonValueKind.String, $"{file}:{key}[{i}] must be a string");
        }
    }

    // An empty array is worse than a missing tone: the tone is offered, ticked, and then
    // contributes nothing for that event while the user assumes it does.
    [Theory]
    [MemberData(nameof(ToneFiles))]
    public void phrase_file_has_no_empty_category(string file)
    {
        var empty = Read(file).Where(kv => kv.Value is JsonArray { Count: 0 }).Select(kv => kv.Key).ToList();
        Assert.True(empty.Count == 0, $"{file} has empty phrase lists for: {string.Join(", ", empty)}");
    }

    // Reported by key and position, never by the phrase itself.
    [Theory]
    [MemberData(nameof(ToneFiles))]
    public void phrase_file_has_no_duplicate_phrases(string file)
    {
        foreach (var (key, phrases) in Read(file))
        {
            var texts = phrases!.AsArray().Select(p => (string?)p).ToList();
            var repeated = texts.Select((text, i) => (text, i)).Where(p => texts.IndexOf(p.text) != p.i).Select(p => p.i).ToList();
            Assert.True(repeated.Count == 0, $"{file}:{key} repeats the phrases at {string.Join(", ", repeated)}");
        }
    }

    // A language may ship a subset of the tones, but the default tone is what an otherwise
    // unusable selection falls back to, so a language without it has nothing to fall back on.
    [Fact]
    public void every_language_ships_the_default_tone()
    {
        var missing = LanguageDirs().Where(dir => !File.Exists(Path.Combine(dir, CalloutHandler.DefaultTone + ".json")))
            .Select(Path.GetFileName).ToList();
        Assert.True(missing.Count == 0, $"These languages are missing the default tone {CalloutHandler.DefaultTone}: {string.Join(", ", missing)}");
    }

    // Not required by the code (unlabelled tones get a title-cased name), but the shipped set is
    // curated - a stray file here means a typo'd tone name.
    [Fact]
    public void every_shipped_tone_has_a_declared_label()
    {
        var shipped = ToneFiles().Select(f => ((string)f[0]).Split('/')[1]).ToHashSet();
        Assert.Equal(CalloutHandler.ToneLabels.Keys.ToHashSet(), shipped);
    }

    [Fact]
    public void the_default_tone_is_actually_shipped() =>
        Assert.Contains(CalloutHandler.DefaultTone, ToneFiles().Select(f => ((string)f[0]).Split('/')[1]));

    // Tones are mixed by drawing a tone and then a line from it, so a line copied into two tones is
    // heard about twice as often as its neighbours - and blurs the two tones it sits in.
    [Fact]
    public void no_phrase_is_shared_between_two_tones_of_a_language()
    {
        foreach (var langDir in LanguageDirs())
        {
            var owners = new Dictionary<(string Key, string Phrase), List<string>>();
            foreach (var path in Directory.GetFiles(langDir, "*.json").Order(StringComparer.Ordinal))
            {
                foreach (var (key, phrases) in JsonNode.Parse(File.ReadAllText(path))!.AsObject())
                {
                    foreach (var phrase in phrases!.AsArray())
                    {
                        var at = (key, (string)phrase!);
                        if (!owners.TryGetValue(at, out var tones))
                            owners[at] = tones = [];
                        tones.Add(Path.GetFileNameWithoutExtension(path));
                    }
                }
            }
            var shared = owners.Where(kv => kv.Value.Count > 1).Select(kv => $"{kv.Key.Key} in {string.Join("+", kv.Value)}").Take(5).ToList();
            Assert.True(shared.Count == 0, $"{Path.GetFileName(langDir)}: phrases appear in several tones: {string.Join("; ", shared)}");
        }
    }
}
