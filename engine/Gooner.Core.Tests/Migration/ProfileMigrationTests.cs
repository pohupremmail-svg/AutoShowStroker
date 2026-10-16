using System.Security;
using System.Text.Json.Nodes;
using Gooner.Core.Data;
using Gooner.Core.Migration;
using Gooner.Core.Settings;
using Gooner.Core.Tests.Support;

namespace Gooner.Core.Tests.Migration;

// Carrying an install of the Python app over. The tests named like the ones in
// tests/test_user_data.py port them: there the four data files that once lived in the registry
// migrate lazily, on the first load of each, and dead keys are pruned at every launch. Here both
// happen in the one import at startup, which also moves every other setting into settings.json -
// the engine keeps no settings in the registry at all.
public sealed class ProfileMigrationTests : IDisposable
{
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-migration-");
    private readonly UserDataStore _store;
    private readonly FakeLegacySettings _registry = new();

    public ProfileMigrationTests() => _store = new UserDataStore(Path.Combine(_tmp.FullName, "appdata"));

    public void Dispose() => _tmp.Delete(recursive: true);

    private MigrationReport Run() => ProfileMigration.Run(_store, _registry, roamingDir: null);

    private JsonSettingsStore Settings() => new(_store);

    private static bool Same(string expected, JsonNode? actual) => JsonNode.DeepEquals(JsonNode.Parse(expected), actual);

    // A write that fails the way a lock or a full disk would: a directory where the temp file goes.
    private void BlockWrites(string name) => Directory.CreateDirectory(Path.ChangeExtension(_store.PathFor(name), ".json.tmp"));

    // --- migration out of QSettings ---

    [Fact]
    public void migrates_a_legacy_registry_value_into_a_file()
    {
        _registry.Set("ScoreTracker/session_history", """[{"ended_at": "2026-07-18 19:18", "total_num_beat": 102}]""");

        Run();

        Assert.True(Same("""[{"ended_at": "2026-07-18 19:18", "total_num_beat": 102}]""", _store.Load("session_history")));
    }

    [Fact]
    public void migration_removes_the_legacy_key()
    {
        _registry.Set("ScoreTracker/session_history", """[{"total_num_beat": 1}]""");

        Run();

        Assert.False(_registry.Contains("ScoreTracker/session_history"));
    }

    // Python's test of the same name holds the other settings in the registry while one data file
    // moves out. Here they move too - into settings.json, unchanged, and not into the data file.
    [Fact]
    public void migration_leaves_other_settings_unchanged()
    {
        _registry.Set("ScoreTracker/session_history", """[{"total_num_beat": 1}]""").Set("BeatHandler/min_beat_freq", 2.0);

        Run();

        Assert.Equal(2.0, Settings().GetDouble("BeatHandler/min_beat_freq", 0));
        Assert.True(Same("""[{"total_num_beat": 1}]""", _store.Load("session_history")));
        Assert.False(Settings().Contains("ScoreTracker/session_history"));
    }

    [Fact]
    public void file_wins_once_migrated()
    {
        _store.Save("session_history", JsonNode.Parse("""[{"from": "file"}]"""));
        _registry.Set("ScoreTracker/session_history", """[{"from": "registry"}]""");

        Run();

        Assert.True(Same("""[{"from": "file"}]""", _store.Load("session_history")));
    }

    [Fact]
    public void migration_is_idempotent()
    {
        _registry.Set("ScoreTracker/session_history", """[{"total_num_beat": 7}]""").Set("GoonerApp/vid_loudness", 0.5);

        Run();
        Run();

        Assert.True(Same("""[{"total_num_beat": 7}]""", _store.Load("session_history")));
        Assert.Equal(0.5, Settings().GetDouble("GoonerApp/vid_loudness", 0));
    }

    [Fact]
    public void no_migration_when_legacy_key_absent()
    {
        var report = Run();

        Assert.Null(_store.Load("session_history"));
        Assert.False(File.Exists(_store.PathFor("session_history")));
        Assert.False(File.Exists(_store.PathFor(JsonSettingsStore.FileName)));
        Assert.Empty(report.ImportedSettings);
    }

    [Fact]
    public void corrupt_legacy_value_is_not_migrated_and_does_not_raise()
    {
        _registry.Set("ScoreTracker/session_history", "{not valid json");

        var report = Run();

        Assert.Null(_store.Load("session_history"));
        // Unparseable legacy data is left alone rather than silently destroyed - deleting it would
        // throw away the only copy of something we simply failed to read.
        Assert.True(_registry.Contains("ScoreTracker/session_history"));
        Assert.Contains("ScoreTracker/session_history", report.Kept);
        Assert.False(_registry.KeyRemoved);
    }

    [Fact]
    public void migration_keeps_the_registry_copy_when_the_write_fails()
    {
        _registry.Set("ScoreTracker/session_history", """["kept"]""");
        BlockWrites("session_history");

        Run();

        // The registry value is the only surviving copy - dropping it here would lose the data.
        Assert.True(_registry.Contains("ScoreTracker/session_history"));
    }

    // Every data file the Python app once kept in the registry, under the name it has now.
    [Theory]
    [InlineData("ScoreTracker/session_history", "session_history", """[{"total_dur_sec": 42.0}]""")]
    [InlineData("BeatHandler/custom_patterns", "custom_patterns", """{"Legacy": [1, -1]}""")]
    [InlineData("CalloutHandler/custom_phrase_files", "custom_phrase_files", """[{"path": "C:/x/legacy.json", "lang": "en"}]""")]
    [InlineData("GoonerApp/last_selected_folders", "last_selected_folders", """["C:/x/a"]""")]
    public void each_legacy_data_value_becomes_its_file(string key, string name, string payload)
    {
        _registry.Set(key, payload);

        var report = Run();

        Assert.True(Same(payload, _store.Load(name)));
        Assert.False(_registry.Contains(key));
        Assert.False(Settings().Contains(key));
        Assert.Contains(name, report.MovedData);
    }

    [Fact]
    public void a_corrupt_data_file_does_not_cost_the_registry_copy()
    {
        // The local file cannot be read; the registry copy is the one left. It is set aside as
        // .corrupt, as any unreadable data file is, and the registry copy takes its place.
        Directory.CreateDirectory(_store.BaseDir);
        File.WriteAllText(_store.PathFor("session_history"), "{not valid json");
        _registry.Set("ScoreTracker/session_history", """[{"total_num_beat": 3}]""");

        Run();

        Assert.True(Same("""[{"total_num_beat": 3}]""", _store.Load("session_history")));
        Assert.True(File.Exists(Path.ChangeExtension(_store.PathFor("session_history"), ".json.corrupt")));
    }

    [Fact]
    public void empty_legacy_data_holds_nothing_and_is_dropped()
    {
        _registry.Set("ScoreTracker/session_history", "");

        Run();

        Assert.False(_registry.Contains("ScoreTracker/session_history"));
        Assert.Null(_store.Load("session_history"));
    }

    // --- dead registry keys from removed features ---

    [Fact]
    public void prune_removes_the_reverted_tts_keys()
    {
        _registry.Set("TTSHandler/enabled", true).Set("TTSHandler/voice_id", "some-voice").Set("TTSHandler/volume", 0.8);

        Run();

        Assert.Empty(_registry.Keys);
        Assert.Empty(Settings().Keys);
    }

    [Fact]
    public void prune_removes_the_superseded_loudness_key()
    {
        _registry.Set("GoonerApp/loudness", 1.0);

        Run();

        Assert.False(_registry.Contains("GoonerApp/loudness"));
        Assert.False(Settings().Contains("GoonerApp/loudness"));
    }

    [Fact]
    public void prune_removes_the_chances_the_session_plan_replaced()
    {
        _registry.Set("BeatHandler/beat_change_chance", 0.1).Set("ClimaxHandler/climax_chance", 0.15);

        Run();

        Assert.Empty(_registry.Keys);
        Assert.Empty(Settings().Keys);
    }

    [Fact]
    public void prune_keeps_live_settings()
    {
        _registry.Set("GoonerApp/vid_loudness", 0.5).Set("GoonerApp/last_seen_version", "0.7.1")
            .Set("BeatHandler/min_beat_freq", 2.0).Set("BeatHandler/pause_chance", 0.05)
            .Set("ClimaxHandler/fake_climax_chance", 0.05);

        Run();

        var settings = Settings();
        Assert.Equal(0.5, settings.GetDouble("GoonerApp/vid_loudness", 0));
        Assert.Equal("0.7.1", settings.GetString("GoonerApp/last_seen_version", ""));
        Assert.Equal(2.0, settings.GetDouble("BeatHandler/min_beat_freq", 0));
        // The two chances that survived the session plan - they say what the *next* segment does,
        // which the plan buffer decides ahead of time anyway.
        Assert.Equal(0.05, settings.GetDouble("BeatHandler/pause_chance", 0));
        Assert.Equal(0.05, settings.GetDouble("ClimaxHandler/fake_climax_chance", 0));
    }

    [Fact]
    public void prune_is_safe_when_nothing_is_there() => Run();

    [Fact]
    public void dead_keys_list_does_not_name_anything_still_read_by_the_app()
    {
        // Guard against someone adding a key here that production code still reads.
        Assert.DoesNotContain("GoonerApp/vid_loudness", ProfileMigration.LegacyDeadKeys);
        Assert.DoesNotContain("ScoreTracker/session_history", ProfileMigration.LegacyDeadKeys);
    }

    [Fact]
    public void the_dead_keys_are_the_python_apps()
    {
        // src/user_data.py LEGACY_DEAD_KEYS, in its order.
        Assert.Equal(["BeatHandler/beat_change_chance", "ClimaxHandler/climax_chance", "TTSHandler", "GoonerApp/loudness"],
            ProfileMigration.LegacyDeadKeys);
    }

    // --- the settings themselves ---

    [Fact]
    public void settings_read_as_the_python_app_read_them()
    {
        _registry.Set("BeatHandler/min_beat_freq", "0.25").Set("BeatHandler/ramping_active", false)
            .Set("CalloutHandler/selected_lang", "de").SetList("CalloutHandler/selected_tones", "flirty", "teasing")
            .SetInt("GoonerApp/some_count", 7);

        Run();

        var settings = Settings();
        Assert.Equal(0.25, settings.GetDouble("BeatHandler/min_beat_freq", 0));
        Assert.False(settings.GetBool("BeatHandler/ramping_active", true));
        Assert.Equal("de", settings.GetString("CalloutHandler/selected_lang", ""));
        Assert.Equal(["flirty", "teasing"], settings.GetStringList("CalloutHandler/selected_tones"));
        Assert.Equal(7, settings.GetInt("GoonerApp/some_count", 0));
    }

    [Fact]
    public void an_imported_setting_leaves_the_registry()
    {
        _registry.Set("BeatHandler/min_beat_freq", 2.0).Set("CalloutHandler/active_callout", true);

        var report = Run();

        Assert.Empty(_registry.Keys);
        Assert.Equal(["BeatHandler/min_beat_freq", "CalloutHandler/active_callout"], report.ImportedSettings);
    }

    [Fact]
    public void the_key_is_removed_once_it_is_empty()
    {
        _registry.Set("BeatHandler/min_beat_freq", 2.0);

        var report = Run();

        Assert.True(_registry.KeyRemoved);
        Assert.True(report.RegistryKeyRemoved);
    }

    [Fact]
    public void settings_stay_in_the_registry_when_the_settings_file_cannot_be_written()
    {
        _registry.Set("BeatHandler/min_beat_freq", 2.0).Set("CalloutHandler/active_callout", true);
        BlockWrites(JsonSettingsStore.FileName);

        var report = Run();

        Assert.True(report.SettingsWriteFailed);
        Assert.Equal(["BeatHandler/min_beat_freq", "CalloutHandler/active_callout"], _registry.Keys);
        Assert.False(_registry.KeyRemoved);
    }

    [Fact]
    public void a_setting_the_engine_already_has_is_not_overwritten()
    {
        // settings.json is the newer copy - the same rule as a local data file over the roaming one.
        // The registry's value is never going to be read again, so it goes.
        Settings().SetValue("BeatHandler/min_beat_freq", 3.0);
        _registry.Set("BeatHandler/min_beat_freq", 2.0).Set("BeatHandler/max_beat_freq", 4.0);

        var report = Run();

        Assert.Equal(3.0, Settings().GetDouble("BeatHandler/min_beat_freq", 0));
        Assert.Equal(4.0, Settings().GetDouble("BeatHandler/max_beat_freq", 0));
        Assert.Empty(_registry.Keys);
        Assert.Equal(["BeatHandler/max_beat_freq"], report.ImportedSettings);
    }

    [Fact]
    public void an_import_keeps_the_settings_already_there()
    {
        Settings().SetValue("GoonerApp/diagnostic_log", true);
        _registry.Set("BeatHandler/min_beat_freq", 2.0);

        Run();

        Assert.True(Settings().GetBool("GoonerApp/diagnostic_log", false));
        Assert.Equal(2.0, Settings().GetDouble("BeatHandler/min_beat_freq", 0));
    }

    [Fact]
    public void a_value_that_cannot_be_read_stays_in_the_registry()
    {
        _registry.Raw("CalloutHandler/selected_tones", RegistryKind.Binary,
            System.Text.Encoding.Unicode.GetBytes("@Variant(\0\0\0\u0009)")).Set("BeatHandler/min_beat_freq", 2.0);

        var report = Run();

        Assert.Equal(["CalloutHandler/selected_tones"], _registry.Keys);
        Assert.Equal(["CalloutHandler/selected_tones"], report.Kept);
        Assert.False(Settings().Contains("CalloutHandler/selected_tones"));
        Assert.False(_registry.KeyRemoved);
    }

    [Fact]
    public void an_invalid_value_holds_nothing_and_is_dropped()
    {
        _registry.Set("GoonerApp/last_seen_version", "@Invalid()");

        Run();

        Assert.Empty(_registry.Keys);
        Assert.False(Settings().Contains("GoonerApp/last_seen_version"));
    }

    [Fact]
    public void a_registry_that_cannot_be_read_costs_nothing()
    {
        _registry.Set("BeatHandler/min_beat_freq", 2.0);
        _registry.ReadFails = new SecurityException("denied");

        var report = Run();

        Assert.Equal("denied", report.RegistryError);
        Assert.Empty(Settings().Keys);
        Assert.Equal(["BeatHandler/min_beat_freq"], _registry.Keys);
    }

    [Fact]
    public void nothing_is_imported_without_a_registry()
    {
        var report = ProfileMigration.Run(_store, registry: null, roamingDir: null);

        Assert.Empty(report.ImportedSettings);
        Assert.False(report.RegistryKeyRemoved);
        Assert.False(Directory.Exists(_store.BaseDir));
    }

    // --- the roaming profile ---

    [Fact]
    public void the_roaming_files_move_before_the_registry_is_read()
    {
        // A roaming history is newer than a registry one - Python moved it out of the registry
        // first, into Roaming, and only later out of Roaming. Moved first, it wins over the
        // registry copy just as a local file does.
        var roaming = Directory.CreateDirectory(Path.Combine(_tmp.FullName, "Roaming", "GoonerApp")).FullName;
        File.WriteAllText(Path.Combine(roaming, "session_history.json"), """[{"from": "roaming"}]""");
        _registry.Set("ScoreTracker/session_history", """[{"from": "registry"}]""");

        var report = ProfileMigration.Run(_store, _registry, roaming);

        Assert.True(Same("""[{"from": "roaming"}]""", _store.Load("session_history")));
        Assert.Equal(1, report.Roaming.Moved);
        Assert.False(_registry.Contains("ScoreTracker/session_history"));
    }
}
