using System.Text.Json.Nodes;
using Gooner.Core.Callouts;
using Gooner.Core.Data;
using Gooner.Core.Migration;
using Gooner.Core.Settings;
using Gooner.Core.Tests.Support;

namespace Gooner.Core.Tests.Callouts;

// Port of tests/test_callout_handler.py. Where Python waits on a Qt signal, these listen to the
// event; where it monkeypatches random, they script the source; the tease timer is a clock the
// host ticks. custom_phrase_files_migrate_out_of_settings runs the startup import
// (ProfileMigration) where Python's handler migrated on its first load.
public sealed class CalloutHandlerTests : IDisposable
{
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-callouts-");
    private readonly ScriptedRandom _random = new();
    private readonly FakeTime _time = new();
    private readonly string _calloutDir;
    private readonly CalloutHandler _handler;

    public CalloutHandlerTests()
    {
        _calloutDir = TmpPath("callouts");
        foreach (var lang in new[] { "en", "de" })
        {
            var phrases = new JsonObject();
            foreach (var key in CalloutHandler.TriggerKeys)
                phrases[key] = new JsonArray($"{lang} {key} phrase");
            Directory.CreateDirectory(Path.Combine(_calloutDir, lang));
            File.WriteAllText(Path.Combine(_calloutDir, lang, "flirty.json"), phrases.ToJsonString());
        }
        _handler = Handler(_calloutDir);
    }

    public void Dispose() => _tmp.Delete(recursive: true);

    private string TmpPath(params string[] parts) => Path.Combine([_tmp.FullName, .. parts]);

    private CalloutHandler Handler(string calloutDir, ISettingsStore? settings = null, UserDataStore? dataStore = null) =>
        new(calloutDir, settings, dataStore, _random, _time);

    private JsonSettingsStore Settings(string name = "settings") => new(new UserDataStore(TmpPath(name)));

    // What the handler says while `act` runs.
    private static List<string> Said(CalloutHandler handler, Action act)
    {
        var said = new List<string>();
        void Listen(string tease) => said.Add(tease);
        handler.TeaseShown += Listen;
        try
        {
            act();
        }
        finally
        {
            handler.TeaseShown -= Listen;
        }
        return said;
    }

    private string WriteCustomFile(string name, JsonNode data)
    {
        var path = TmpPath(name);
        File.WriteAllText(path, data.ToJsonString());
        return path;
    }

    [Fact]
    public void loads_available_languages() =>
        Assert.Equal(new HashSet<string> { "en", "de" }, _handler.AvailableLanguages.ToHashSet());

    [Fact]
    public void defaults_to_english() => Assert.Equal("en", _handler.Lang);

    [Fact]
    public void defaults_dict_matches_init_defaults()
    {
        Assert.Equal(CalloutHandler.Defaults["active_callout"], _handler.ActiveCallout);
        Assert.Equal(CalloutHandler.Defaults["talking_chance"], _handler.TalkingChance);
        Assert.Equal(CalloutHandler.Defaults["lang"], _handler.Lang);
        Assert.Equal((string[])CalloutHandler.Defaults["selected_tones"], _handler.SelectedTones);
    }

    [Fact]
    public void set_lang_switches_language()
    {
        _handler.SetLang("de");
        Assert.Equal("de", _handler.Lang);
    }

    [Fact]
    public void set_lang_ignores_unknown_language()
    {
        _handler.SetLang("fr");
        Assert.Equal("en", _handler.Lang);
    }

    [Fact]
    public void select_and_output_sentence_emits_when_active()
    {
        _handler.ActiveCallout = true;
        _handler.TalkingChance = 1.0;

        var said = Said(_handler, () => _handler.SelectAndOutputSentence("session_started"));

        Assert.Equal(["en session_started phrase"], said);
        Assert.True(_handler.IsTeasing);
    }

    [Fact]
    public void select_and_output_sentence_noop_when_inactive()
    {
        _handler.ActiveCallout = false;
        Assert.Empty(Said(_handler, () => _handler.SelectAndOutputSentence("session_started")));
    }

    [Fact]
    public void select_and_output_sentence_noop_while_already_teasing()
    {
        _handler.ActiveCallout = true;
        _handler.TalkingChance = 1.0;
        _handler.IsTeasing = true;
        Assert.Empty(Said(_handler, () => _handler.SelectAndOutputSentence("session_started")));
    }

    [Fact]
    public void select_and_output_sentence_respects_talking_chance()
    {
        _handler.ActiveCallout = true;
        _handler.TalkingChance = 0.0;
        _random.Uniform = () => 0.5;
        Assert.Empty(Said(_handler, () => _handler.SelectAndOutputSentence("session_started")));
    }

    [Fact]
    public void force_output_sentence_ignores_active_callout_flag()
    {
        _handler.ActiveCallout = false;
        Assert.Equal(["en session_started phrase"], Said(_handler, () => _handler.ForceOutputSentence("session_started")));
    }

    [Fact]
    public void force_output_sentence_ignores_talking_chance()
    {
        _handler.ActiveCallout = true;
        _handler.TalkingChance = 0.0;
        Assert.Single(Said(_handler, () => _handler.ForceOutputSentence("session_started")));
    }

    [Fact]
    public void force_output_sentence_overrides_in_progress_tease()
    {
        _handler.ActiveCallout = true;
        _handler.IsTeasing = true;
        Assert.Single(Said(_handler, () => _handler.ForceOutputSentence("session_started")));
        Assert.True(_handler.IsTeasing);
    }

    [Fact]
    public void force_output_sentence_missing_category_does_not_raise() =>
        Assert.Empty(Said(_handler, () => _handler.ForceOutputSentence("does_not_exist")));

    [Fact]
    public void missing_category_does_not_raise()
    {
        _handler.ActiveCallout = true;
        _handler.TalkingChance = 1.0;
        _handler.CalloutData["en"]["flirty"]["session_started"] = [];
        _handler.SelectAndOutputSentence("session_started");
    }

    [Fact]
    public void tease_timer_hides_after_timeout()
    {
        _handler.ActiveCallout = true;
        _handler.TalkingChance = 1.0;
        _handler.TeaseTime = TimeSpan.FromMilliseconds(50);
        var hidden = 0;
        _handler.TeaseHidden += () => hidden++;

        _handler.SelectAndOutputSentence("session_started");
        _time.Now += 0.049;
        _handler.Tick();
        Assert.Equal(0, hidden);
        _time.Now += 0.002;
        _handler.Tick();

        Assert.Equal(1, hidden);
        Assert.False(_handler.IsTeasing);
    }

    [Fact]
    public void beat_change_general_picks_faster_when_freq_increases()
    {
        _handler.ActiveCallout = true;
        _handler.TalkingChance = 1.0;
        _handler.CurFreq = 1.0;
        _random.Uniform = () => 0.9;

        var said = Said(_handler, () => _handler.BeatChangeGeneral(2.0, "Standard Beat"));

        Assert.Equal(["en beat_change_faster phrase"], said);
    }

    [Fact]
    public void beat_change_general_picks_slower_when_freq_decreases()
    {
        _handler.ActiveCallout = true;
        _handler.TalkingChance = 1.0;
        _handler.CurFreq = 2.0;
        _random.Uniform = () => 0.9;

        var said = Said(_handler, () => _handler.BeatChangeGeneral(1.0, "Standard Beat"));

        Assert.Equal(["en beat_change_slower phrase"], said);
    }

    // --- the start of a session (the engine's own) ---
    //
    // The Python app rolls the talking chance for its greeting like for any line, and counts the
    // session's first rhythm as a change of it - so when the greeting lost its roll, the first
    // thing said could be "faster". The engine always greets while callouts are on, and says
    // nothing for the first rhythm.

    [Fact]
    public void a_session_always_opens_with_a_greeting_while_callouts_are_on()
    {
        _handler.ActiveCallout = true;
        _handler.TalkingChance = 0.0;
        _random.Uniform = () => 0.99;

        Assert.Equal(["en session_started phrase"], Said(_handler, _handler.SessionStarted));
    }

    [Fact]
    public void there_is_no_greeting_while_callouts_are_off()
    {
        _handler.ActiveCallout = false;

        Assert.Empty(Said(_handler, _handler.SessionStarted));
    }

    [Fact]
    public void the_first_rhythm_of_a_session_is_not_a_change()
    {
        _handler.ActiveCallout = true;
        _handler.TalkingChance = 1.0;
        _random.Uniform = () => 0.9;
        _handler.SessionStarted();
        _time.Now += 600;
        _handler.Tick();

        Assert.Empty(Said(_handler, () => _handler.BeatChangeGeneral(2.0, "Standard Beat")));
        Assert.Equal(2.0, _handler.CurFreq);
        Assert.Equal(["en beat_change_faster phrase"], Said(_handler, () => _handler.BeatChangeGeneral(3.0, "Quick Swing")));
    }

    // Not "slower" because the last session ended faster: each session's first rhythm only sets
    // the tempo the next change is held against.
    [Fact]
    public void every_session_starts_its_comparison_afresh()
    {
        _handler.ActiveCallout = true;
        _handler.TalkingChance = 1.0;
        _random.Uniform = () => 0.9;
        _handler.SessionStarted();
        _handler.BeatChangeGeneral(2.0, "Standard Beat");
        _handler.BeatChangeGeneral(5.0, "Quick Swing");
        _time.Now += 600;
        _handler.Tick();

        _handler.SessionStarted();
        _time.Now += 600;
        _handler.Tick();

        Assert.Empty(Said(_handler, () => _handler.BeatChangeGeneral(1.0, "Standard Beat")));
        Assert.Equal(1.0, _handler.CurFreq);
    }

    // --- the start screen's line (the engine's own) ---

    // Under the name on the start screen: a main_menu line of the language and tones picked, so
    // the app greets in the voice it will speak in. Shown whether callouts are on or not - it is
    // the app's face, not a callout.
    [Fact]
    public void the_title_line_is_a_main_menu_line_in_the_language_picked()
    {
        Assert.False(_handler.ActiveCallout);
        Assert.Equal("en main_menu phrase", _handler.TitleLine());

        _handler.SetLang("de");

        Assert.Equal("de main_menu phrase", _handler.TitleLine());
    }

    // The start screen draws a new line when the voice changes, and only then.
    [Fact]
    public void a_change_of_language_or_tones_is_announced_and_nothing_else_is()
    {
        var changes = 0;
        _handler.VoiceChanged += () => changes++;

        _handler.SetLang("de");
        _handler.SetLang("de");
        _handler.SetLang("xx");
        _handler.SetTones(["flirty"]);
        _handler.SetTones(["flirty"]);

        Assert.Equal(1, changes);
    }

    [Fact]
    public void a_new_mix_of_tones_is_announced()
    {
        var changes = 0;
        _handler.VoiceChanged += () => changes++;

        _handler.SetTones([]);

        Assert.Equal(1, changes);
    }

    [Fact]
    public void there_is_no_title_line_without_main_menu_lines()
    {
        _handler.CalloutData["en"]["flirty"]["main_menu"] = [];

        Assert.Null(_handler.TitleLine());
    }

    // --- directory-loading edge cases ---

    [Fact]
    public void empty_callout_dir_does_not_crash_and_disables_callouts()
    {
        Directory.CreateDirectory(TmpPath("empty"));
        var handler = Handler(TmpPath("empty"));

        Assert.Empty(handler.AvailableLanguages);
        Assert.Empty(handler.CalloutData);
        // active_callout defaults false anyway, but even if turned on, there is no data to select
        // from - selecting must not raise.
        handler.ActiveCallout = true;
        handler.TalkingChance = 1.0;
        handler.SelectAndOutputSentence("session_started");
    }

    [Fact]
    public void invalid_json_file_is_skipped_but_language_stays_listed()
    {
        Directory.CreateDirectory(TmpPath("broken", "en"));
        File.WriteAllText(TmpPath("broken", "en", "flirty.json"), "{not valid json");

        var handler = Handler(TmpPath("broken"));

        Assert.Equal(["en"], handler.AvailableLanguages);
        var only = Assert.Single(handler.CalloutData);
        Assert.Equal("en", only.Key);
        Assert.Empty(only.Value);
        // The language falls back to "en" but there is no data loaded for it - selecting a
        // sentence must degrade gracefully rather than raise.
        handler.ActiveCallout = true;
        handler.TalkingChance = 1.0;
        handler.SelectAndOutputSentence("session_started");
    }

    // --- custom phrase files ---

    [Fact]
    public void load_custom_file_merges_phrases_after_builtins()
    {
        var customPath = WriteCustomFile("custom_en.json", new JsonObject { ["session_started"] = new JsonArray("custom phrase") });

        _handler.LoadCustomFile(customPath, "en");

        Assert.Equal(["en session_started phrase", "custom phrase"], _handler.CalloutData["en"]["flirty"]["session_started"]);
    }

    [Fact]
    public void load_custom_file_raises_for_unknown_language()
    {
        var customPath = WriteCustomFile("custom.json", new JsonObject { ["session_started"] = new JsonArray("x") });
        Assert.Throws<PhraseFileException>(() => _handler.LoadCustomFile(customPath, "fr"));
    }

    [Fact]
    public void load_custom_file_raises_if_already_loaded()
    {
        var customPath = WriteCustomFile("custom.json", new JsonObject { ["session_started"] = new JsonArray("x") });
        _handler.LoadCustomFile(customPath, "en");
        Assert.Throws<PhraseFileException>(() => _handler.LoadCustomFile(customPath, "en"));
    }

    [Fact]
    public void load_custom_file_raises_for_missing_file() =>
        Assert.Throws<PhraseFileException>(() => _handler.LoadCustomFile(TmpPath("does_not_exist.json"), "en"));

    [Fact]
    public void load_custom_file_raises_for_invalid_json()
    {
        File.WriteAllText(TmpPath("bad.json"), "{not valid json");
        Assert.Throws<PhraseFileException>(() => _handler.LoadCustomFile(TmpPath("bad.json"), "en"));
    }

    [Fact]
    public void load_custom_file_raises_for_non_object_json()
    {
        File.WriteAllText(TmpPath("list.json"), "[1, 2, 3]");
        Assert.Throws<PhraseFileException>(() => _handler.LoadCustomFile(TmpPath("list.json"), "en"));
    }

    [Fact]
    public void load_custom_file_ignores_unknown_trigger_keys_and_bad_values()
    {
        var customPath = WriteCustomFile("custom.json", new JsonObject
        {
            ["session_started"] = new JsonArray("good phrase"),
            ["not_a_real_key"] = new JsonArray("ignored"),
            ["pause_start"] = "not a list",
            ["pause_end"] = new JsonArray(1, 2, 3),
        });

        _handler.LoadCustomFile(customPath, "en");

        var flirty = _handler.CalloutData["en"]["flirty"];
        Assert.Equal(["en session_started phrase", "good phrase"], flirty["session_started"]);
        Assert.False(flirty.ContainsKey("not_a_real_key"));
        Assert.Equal(["en pause_start phrase"], flirty["pause_start"]);
        Assert.Equal(["en pause_end phrase"], flirty["pause_end"]);
    }

    [Fact]
    public void unload_custom_file_removes_entry_and_stops_contributing()
    {
        var one = WriteCustomFile("custom_en_1.json", new JsonObject { ["session_started"] = new JsonArray("phrase one") });
        var two = WriteCustomFile("custom_en_2.json", new JsonObject { ["session_started"] = new JsonArray("phrase two") });
        _handler.LoadCustomFile(one, "en");
        _handler.LoadCustomFile(two, "en");

        _handler.UnloadCustomFile(one);

        Assert.Equal(["en session_started phrase", "phrase two"], _handler.CalloutData["en"]["flirty"]["session_started"]);
        Assert.Equal([two], _handler.CustomPhraseFiles.Select(e => e.Path));
    }

    [Fact]
    public void unload_nonexistent_file_is_a_noop() => _handler.UnloadCustomFile(TmpPath("does", "not", "exist.json"));

    [Fact]
    public void settings_less_handler_supports_load_and_unload()
    {
        var customPath = WriteCustomFile("custom.json", new JsonObject { ["session_started"] = new JsonArray("x") });
        _handler.LoadCustomFile(customPath, "en");
        _handler.UnloadCustomFile(customPath);
    }

    [Fact]
    public void merged_custom_phrase_is_selectable()
    {
        var customPath = WriteCustomFile("custom.json", new JsonObject { ["session_started"] = new JsonArray("custom phrase!") });
        _handler.LoadCustomFile(customPath, "en");
        _handler.ActiveCallout = true;
        _handler.TalkingChance = 1.0;
        _random.Choice = n => n - 1;

        var said = Said(_handler, () => _handler.SelectAndOutputSentence("session_started"));

        Assert.Equal(["custom phrase!"], said);
    }

    [Fact]
    public void load_custom_file_persists_and_reloads_across_instances()
    {
        var settings = Settings();
        var store = new UserDataStore(TmpPath("appdata"));
        var customPath = WriteCustomFile("custom.json", new JsonObject { ["session_started"] = new JsonArray("persisted phrase") });
        Handler(_calloutDir, settings, store).LoadCustomFile(customPath, "en");

        var second = Handler(_calloutDir, settings, store);

        Assert.Equal(["en session_started phrase", "persisted phrase"], second.CalloutData["en"]["flirty"]["session_started"]);
    }

    [Fact]
    public void custom_phrase_files_migrate_out_of_settings()
    {
        var customPath = WriteCustomFile("legacy.json", new JsonObject { ["session_started"] = new JsonArray("legacy phrase") });
        var registry = new FakeLegacySettings().Set("CalloutHandler/custom_phrase_files",
            new JsonArray(new JsonObject { ["path"] = customPath, ["lang"] = "en" }).ToJsonString());
        var store = new UserDataStore(TmpPath("appdata"));

        ProfileMigration.Run(store, registry, roamingDir: null);
        var handler = Handler(_calloutDir, new JsonSettingsStore(store), store);

        Assert.Equal(["en session_started phrase", "legacy phrase"], handler.CalloutData["en"]["flirty"]["session_started"]);
        Assert.True(File.Exists(store.PathFor("custom_phrase_files")));
        Assert.False(registry.Contains("CalloutHandler/custom_phrase_files"));
    }

    // --- a fresh install must not silently disable callouts ---

    [Fact]
    public void talking_chance_falls_back_to_the_default_on_a_fresh_profile() =>
        Assert.Equal(CalloutHandler.Defaults["talking_chance"], Handler(_calloutDir, Settings()).TalkingChance);

    [Fact]
    public void language_falls_back_to_the_default_on_a_fresh_profile() =>
        Assert.Equal(CalloutHandler.Defaults["lang"], Handler(_calloutDir, Settings()).Lang);

    // An explicit 0.0 is a real choice and must survive - only a *missing* key defaults.
    [Fact]
    public void stored_talking_chance_still_wins_over_the_default()
    {
        var settings = Settings();
        settings.SetValue("CalloutHandler/talking_chance", 0.0);

        Assert.Equal(0.0, Handler(_calloutDir, settings).TalkingChance);
    }

    // --- resource resolution, and SetLang's guard ---

    // Python's test_callout_dir_resolves_from_the_project_root_not_the_cwd. Gooner.Core does not
    // know where the app is installed - the host hands it the folder - so what is left to hold is
    // that the shipped folder is read as such.
    [Fact]
    public void the_shipped_callouts_are_found() =>
        Assert.NotEmpty(Handler(Repo.PathTo("res/callouts")).AvailableLanguages);

    // Used to be an assert in Python, which killed startup with no window and no message.
    [Fact]
    public void missing_callout_dir_disables_callouts_without_crashing()
    {
        var handler = Handler(TmpPath("does-not-exist"));

        Assert.Empty(handler.AvailableLanguages);
        Assert.Empty(handler.CalloutData);
        handler.ActiveCallout = true;
        handler.TalkingChance = 1.0;
        handler.SelectAndOutputSentence("session_started");
    }

    // SetLang once guarded on the old language instead of the new one, so the fallback for a
    // configured language without a file could never fire.
    [Fact]
    public void set_lang_falls_back_when_the_configured_language_has_no_file()
    {
        Directory.CreateDirectory(TmpPath("fr_only", "fr"));
        File.WriteAllText(TmpPath("fr_only", "fr", "flirty.json"), """{"session_started": ["salut"]}""");

        var handler = Handler(TmpPath("fr_only"));

        // "en" is the default but has no file here - the handler must land on what exists.
        Assert.Equal("fr", handler.Lang);
        handler.SetLang("de");
        Assert.Equal("fr", handler.Lang);
    }
}
