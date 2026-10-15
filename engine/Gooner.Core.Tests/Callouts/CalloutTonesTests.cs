using System.Text.Json.Nodes;
using Gooner.Core.Callouts;
using Gooner.Core.Data;
using Gooner.Core.Randomness;
using Gooner.Core.Settings;
using Gooner.Core.Tests.Support;

namespace Gooner.Core.Tests.Callouts;

// Port of tests/test_callout_tones.py. Tone as a second axis alongside language:
// res/callouts/<lang>/<tone>.json, several tones active at once, mixed by the handler.
public sealed class CalloutTonesTests : IDisposable
{
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-tones-");
    private readonly string _toneDir;
    private readonly CalloutHandler _handler;

    public CalloutTonesTests()
    {
        _toneDir = Path.Combine(_tmp.FullName, "callouts");
        foreach (var lang in new[] { "en", "de" })
            foreach (var tone in new[] { "flirty", "shy", "degrading" })
                WriteTone(_toneDir, lang, tone);
        _handler = Handler(_toneDir);
    }

    public void Dispose() => _tmp.Delete(recursive: true);

    private static CalloutHandler Handler(string dir, ISettingsStore? settings = null, IRandomSource? random = null) =>
        new(dir, settings, random: random ?? new ScriptedRandom());

    private static void WriteTone(string root, string lang, string tone, int phrasesPerKey = 1)
    {
        var data = new JsonObject();
        foreach (var key in CalloutHandler.TriggerKeys)
            data[key] = new JsonArray([.. Enumerable.Range(0, phrasesPerKey).Select(i => (JsonNode)$"{lang} {tone} {key} {i}")]);
        Directory.CreateDirectory(Path.Combine(root, lang));
        File.WriteAllText(Path.Combine(root, lang, tone + ".json"), data.ToJsonString());
    }

    // What selecting would say, without the tease timer in the way.
    private static List<string?> Drain(CalloutHandler handler, string category, int draws = 1)
    {
        handler.ActiveCallout = true;
        handler.TalkingChance = 1.0;
        var seen = new List<string?>();
        for (var i = 0; i < draws; i++)
        {
            handler.IsTeasing = false;
            seen.Add(handler.PickPhrase(category));
        }
        return seen;
    }

    private static List<string?> Times(string phrase, int count) => [.. Enumerable.Repeat<string?>(phrase, count)];

    // --- discovery ---

    [Fact]
    public void tones_are_discovered_from_the_directory_layout() =>
        Assert.Equal(new HashSet<string> { "flirty", "shy", "degrading" }, _handler.AvailableTones.ToHashSet());

    [Fact]
    public void languages_are_still_discovered_one_level_up() =>
        Assert.Equal(new HashSet<string> { "en", "de" }, _handler.AvailableLanguages.ToHashSet());

    [Fact]
    public void available_tones_follow_the_declared_order_not_the_filesystem()
    {
        var declared = CalloutHandler.ToneLabels.Keys.Where(_handler.AvailableTones.Contains);
        Assert.Equal(declared, _handler.AvailableTones);
    }

    [Fact]
    public void a_tone_nobody_declared_a_label_for_is_still_offered()
    {
        WriteTone(_toneDir, "en", "bratty");

        var handler = Handler(_toneDir);

        Assert.Contains("bratty", handler.AvailableTones);
        Assert.Equal("Bratty", CalloutHandler.ToneLabel("bratty"));
    }

    [Fact]
    public void tones_for_reports_what_a_single_language_actually_ships()
    {
        WriteTone(_toneDir, "en", "shouty");

        var handler = Handler(_toneDir);

        Assert.Contains("shouty", handler.TonesFor("en"));
        Assert.DoesNotContain("shouty", handler.TonesFor("de"));
    }

    [Fact]
    public void an_unreadable_tone_file_does_not_take_the_language_down()
    {
        File.WriteAllText(Path.Combine(_toneDir, "en", "shy.json"), "{not json");

        var handler = Handler(_toneDir);

        Assert.Contains("en", handler.AvailableLanguages);
        Assert.Equal(new HashSet<string> { "flirty", "degrading" }, handler.TonesFor("en").ToHashSet());
    }

    // --- selection ---

    [Fact]
    public void the_default_selection_is_the_default_tone_alone()
    {
        Assert.Equal((string[])CalloutHandler.Defaults["selected_tones"], _handler.SelectedTones);
        Assert.Equal([CalloutHandler.DefaultTone], _handler.SelectedTones);
    }

    [Fact]
    public void only_the_selected_tone_is_drawn_from()
    {
        _handler.SetTones(["shy"]);

        Assert.Equal(Times("en shy session_started 0", 10), Drain(_handler, "session_started", draws: 10));
    }

    [Fact]
    public void selecting_several_tones_mixes_them()
    {
        _handler.SetTones(["flirty", "shy"]);

        var seen = Drain(_handler, "session_started", draws: 60).ToHashSet();

        Assert.Equal(new HashSet<string?> { "en flirty session_started 0", "en shy session_started 0" }, seen);
    }

    [Fact]
    public void an_unticked_tone_never_speaks()
    {
        _handler.SetTones(["flirty", "shy"]);

        Assert.DoesNotContain(Drain(_handler, "session_started", draws: 60), phrase => phrase!.Contains("degrading", StringComparison.Ordinal));
    }

    // A tone with 50 lines must not drown out one with a single line - the user ticked two tones
    // and expects to hear both, not a 50:1 split.
    [Fact]
    public void every_selected_tone_gets_an_equal_voice_regardless_of_file_size()
    {
        var root = Path.Combine(_tmp.FullName, "sized");
        WriteTone(root, "en", "flirty", phrasesPerKey: 1);
        WriteTone(root, "en", "shy", phrasesPerKey: 50);
        var handler = Handler(root, random: new SystemRandomSource(new Random(0)));
        handler.SetTones(["flirty", "shy"]);

        var seen = Drain(handler, "session_started", draws: 200);
        var flirty = seen.Count(phrase => phrase!.StartsWith("en flirty", StringComparison.Ordinal));

        Assert.True(flirty > 50, $"{flirty}");  // ~100 when weighted per tone, ~4 when weighted per phrase
    }

    [Fact]
    public void set_tones_drops_tones_that_do_not_exist()
    {
        _handler.SetTones(["shy", "nonexistent"]);

        Assert.Equal(["shy"], _handler.SelectedTones);
    }

    // Never silently promote whatever happens to be on disk - degrading is opt-in.
    [Fact]
    public void a_selection_with_nothing_usable_falls_back_to_the_default_tone()
    {
        _handler.SetTones(["nonexistent"]);

        Assert.Equal(Times("en flirty session_started 0", 10), Drain(_handler, "session_started", draws: 10));
    }

    [Fact]
    public void switching_language_keeps_the_tone_selection()
    {
        _handler.SetTones(["shy"]);
        _handler.SetLang("de");

        Assert.Equal(["shy"], _handler.SelectedTones);
        Assert.Equal(Times("de shy session_started 0", 5), Drain(_handler, "session_started", draws: 5));
    }

    [Fact]
    public void a_tone_missing_for_the_current_language_is_skipped_not_crashed()
    {
        WriteTone(_toneDir, "en", "shouty");
        var handler = Handler(_toneDir);
        handler.SetTones(["shouty", "shy"]);
        handler.SetLang("de");

        Assert.Equal(Times("de shy session_started 0", 5), Drain(handler, "session_started", draws: 5));
    }

    [Fact]
    public void force_output_sentence_mixes_the_same_way()
    {
        _handler.SetTones(["shy"]);
        var said = new List<string>();
        _handler.TeaseShown += said.Add;

        _handler.ForceOutputSentence("climax_real");

        Assert.Equal(["en shy climax_real 0"], said);
    }

    [Fact]
    public void an_empty_category_does_not_raise()
    {
        _handler.CalloutData["en"]["flirty"]["session_started"] = [];

        Assert.Equal([null], Drain(_handler, "session_started"));
    }

    // --- persistence ---

    [Fact]
    public void the_tone_selection_survives_a_restart()
    {
        var settings = new JsonSettingsStore(new UserDataStore(Path.Combine(_tmp.FullName, "appdata")));
        var first = Handler(_toneDir, settings);
        first.SetTones(["shy", "degrading"]);
        settings.SetValue("CalloutHandler/selected_tones", new JsonArray([.. first.SelectedTones.Select(t => (JsonNode)t)]));

        var second = Handler(_toneDir, settings);

        Assert.Equal(["shy", "degrading"], second.SelectedTones);
    }

    // QSettings hands a one-element list back as a bare string; the registry import passes that on.
    [Fact]
    public void a_single_stored_tone_comes_back_as_a_list()
    {
        var settings = new JsonSettingsStore(new UserDataStore(Path.Combine(_tmp.FullName, "appdata")));
        settings.SetValue("CalloutHandler/selected_tones", "shy");

        Assert.Equal(["shy"], Handler(_toneDir, settings).SelectedTones);
    }
}
