using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Gooner.Core.Achievements;
using Gooner.Core.App;
using Gooner.Core.Localization;
using Gooner.Core.Panels;
using Gooner.Core.Tests.Support;

namespace Gooner.Core.Tests.Localization;

// The shipped catalogues, res/locales/<language>/*.json, against the texts the app actually shows.
// A text without a translation would quietly stay English in the middle of a German window, and a
// translation of a text that has since changed would quietly stop being used - so both fail here,
// with the texts named.
//
// What there is to translate: every literal the code hands to L10n (SourceTexts reads them from
// Gooner.Core and the Godot scripts), plus what is translated as a whole where it is shown - the
// Guide block by block, every What's New entry, every achievement.
public partial class LocaleFilesTests
{
    private static readonly string LocaleDir = Repo.PathTo("res/locales");

    private static readonly Lazy<SourceTexts.Scan> Scan = new(() =>
        SourceTexts.Read(Repo.PathTo("engine/Gooner.Core"), Repo.PathTo("engine/godot/scripts")));

    private static readonly Lazy<HashSet<string>> Required = new(() =>
    [
        .. Scan.Value.Texts.Select(t => t.Value),
        .. Guide.Topics.SelectMany(topic => RichText.Blocks(topic.Html).Prepend(topic.Title)),
        .. Changelog.Entries.Select(entry => entry.Html),
        .. AchievementCatalogue.All.SelectMany(a => new[] { a.Name, a.Description, a.Track ?? "" }).Where(text => text.Length > 0),
    ]);

    public static IEnumerable<object[]> Languages() =>
        Directory.GetDirectories(LocaleDir).Order(StringComparer.Ordinal).Select(dir => new object[] { Path.GetFileName(dir) });

    private static Dictionary<string, (string Translation, string File)> Entries(string language)
    {
        var entries = new Dictionary<string, (string, string)>();
        foreach (var file in Directory.GetFiles(Path.Combine(LocaleDir, language), "*.json").Order(StringComparer.Ordinal))
            foreach (var (english, translation) in JsonNode.Parse(File.ReadAllText(file))!.AsObject())
                entries.TryAdd(english, ((string)translation!, Path.GetFileName(file)));
        return entries;
    }

    // The texts as they would be written into a catalogue, so a missing one can be copied across.
    private static readonly JsonSerializerOptions Readable = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string List(IEnumerable<string> texts)
    {
        var all = texts.Order(StringComparer.Ordinal).ToList();
        var shown = all.Take(40).Select(text => "  " + JsonSerializer.Serialize(text, Readable));
        return $"{all.Count}:\n{string.Join("\n", shown)}{(all.Count > 40 ? "\n  ..." : "")}";
    }

    [Fact]
    public void german_and_french_are_shipped()
    {
        Assert.Equal(["en", "de", "fr"], Catalogue.Available(LocaleDir));
    }

    [Fact]
    public void the_scan_finds_the_texts_of_both_sides()
    {
        Assert.Contains("Start Session", Required.Value);
        Assert.Contains("Save & Close Settings", Required.Value);
        Assert.Contains("Session over.\nNot today - no cumming for you.", Required.Value);
    }

    [Fact]
    public void every_text_is_handed_over_in_a_form_a_catalogue_can_hold() =>
        Assert.True(Scan.Value.Problems.Count == 0, string.Join("\n", Scan.Value.Problems));

    [Fact]
    public void no_file_sits_outside_a_language_folder() =>
        Assert.Empty(Directory.GetFiles(LocaleDir));

    [Theory]
    [MemberData(nameof(Languages))]
    public void every_file_is_an_object_of_translations(string language)
    {
        foreach (var file in Directory.GetFiles(Path.Combine(LocaleDir, language), "*.json"))
        {
            var parsed = JsonNode.Parse(File.ReadAllText(file));
            Assert.True(parsed is JsonObject, $"{language}/{Path.GetFileName(file)} must be an object of English text -> translation");
            foreach (var (english, translation) in parsed!.AsObject())
            {
                Assert.True(translation is JsonValue v && v.GetValueKind() == JsonValueKind.String && ((string)v!).Trim().Length > 0,
                    $"{language}/{Path.GetFileName(file)}: the translation of {english} must be a text");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void every_text_has_a_translation(string language)
    {
        var missing = Required.Value.Except(Entries(language).Keys).ToList();
        Assert.True(missing.Count == 0, $"{language} has no translation for {List(missing)}");
    }

    // Usually a text that was reworded in the code: the old translation is never asked for again.
    [Theory]
    [MemberData(nameof(Languages))]
    public void no_translation_is_left_over_from_a_text_that_is_gone(string language)
    {
        var stale = Entries(language).Keys.Except(Required.Value).ToList();
        Assert.True(stale.Count == 0, $"{language} translates texts the app no longer has - reworded or removed? {List(stale)}");
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void no_text_is_translated_twice(string language)
    {
        var seen = new Dictionary<string, string>();
        foreach (var file in Directory.GetFiles(Path.Combine(LocaleDir, language), "*.json").Order(StringComparer.Ordinal))
            foreach (var (english, _) in JsonNode.Parse(File.ReadAllText(file))!.AsObject())
                Assert.True(seen.TryAdd(english, Path.GetFileName(file)),
                    $"{language}: {english} is in both {seen.GetValueOrDefault(english)} and {Path.GetFileName(file)}");
    }

    // {0} is a number or a name the app fills in: a translation that drops it loses the number, and
    // one that adds {2} throws where it is filled in.
    [Theory]
    [MemberData(nameof(Languages))]
    public void a_translation_keeps_every_value_it_is_given(string language)
    {
        var wrong = Entries(language)
            .Where(e => !Placeholders(e.Key).SetEquals(Placeholders(e.Value.Translation)))
            .Select(e => $"{e.Key} -> {e.Value.Translation}").ToList();
        Assert.True(wrong.Count == 0, $"{language}: placeholders differ in {List(wrong)}");
    }

    // The Guide and What's New are markup: a lost </b> sets the rest of the page in bold.
    [Theory]
    [MemberData(nameof(Languages))]
    public void a_translation_keeps_its_markup(string language)
    {
        var wrong = Entries(language)
            .Where(e => !Tags(e.Key).SequenceEqual(Tags(e.Value.Translation)))
            .Select(e => e.Key).ToList();
        Assert.True(wrong.Count == 0, $"{language}: the tags differ in {List(wrong)}");
    }

    private static HashSet<string> Placeholders(string text) =>
        [.. PlaceholderPattern().Matches(text.Replace("{{", "", StringComparison.Ordinal)).Select(m => m.Groups[1].Value)];

    private static List<string> Tags(string text) =>
        [.. TagPattern().Matches(text).Select(m => m.Groups[1].Value.ToLowerInvariant()).Order(StringComparer.Ordinal)];

    [GeneratedRegex(@"\{(\d+)(?:,[^}:]*)?(?::[^}]*)?\}")]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex("<(/?[a-zA-Z0-9]+)[^>]*>")]
    private static partial Regex TagPattern();
}
