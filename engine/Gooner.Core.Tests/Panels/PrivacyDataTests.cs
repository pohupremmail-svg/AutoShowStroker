using System.Text.Json.Nodes;
using Gooner.Core.App;
using Gooner.Core.Callouts;
using Gooner.Core.Data;
using Gooner.Core.Logging;
using Gooner.Core.Panels;
using Gooner.Core.Sessions;
using Gooner.Core.Settings;
using Gooner.Core.Tests.Support;
using Gooner.Core.Timing;

namespace Gooner.Core.Tests.Panels;

// Port of tests/test_privacy_data_dialog.py: where the app keeps things, and deleting any of it,
// per category. The tests about the device plugin come with it (step 3.13); the ones that click a
// widget call what the click calls.
public sealed class PrivacyDataTests : IDisposable
{
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-privacy-");
    private readonly UserDataStore _data;
    private readonly JsonSettingsStore _settings;
    private readonly GoonerApp _app;
    private readonly PrivacyData _privacy;

    public PrivacyDataTests()
    {
        var callouts = Path.Combine(_tmp.FullName, "callouts");
        Directory.CreateDirectory(Path.Combine(callouts, "en"));
        var phrases = new JsonObject();
        foreach (var key in CalloutHandler.TriggerKeys) phrases[key] = new JsonArray($"en {key} phrase");
        File.WriteAllText(Path.Combine(callouts, "en", "flirty.json"), phrases.ToJsonString());
        _data = new UserDataStore(Path.Combine(_tmp.FullName, "data"));
        _settings = new JsonSettingsStore(_data);
        _app = new GoonerApp(new FakeStage(), new FrameClock(1_800_000_000.0), new GoonerAppOptions
        {
            Settings = _settings,
            DataStore = _data,
            CalloutDir = callouts,
            Random = new ScriptedRandom(1),
        });
        _privacy = new PrivacyData(_app);
    }

    public void Dispose()
    {
        AppLog.Configure(false, null);
        _tmp.Delete(recursive: true);
    }

    private void Populate()
    {
        _app.Score.History = [new JsonObject { ["total_dur_sec"] = 60 }, new JsonObject { ["total_dur_sec"] = 30 }];
        _data.Save("session_history", new JsonArray([.. _app.Score.History.Select(e => e.DeepClone())]));
        _app.Beat.AddOrUpdateCustomPattern("Mine", [1, -1]);
        _data.Save("last_selected_folders", new JsonArray(_tmp.FullName));
    }

    // --- disclosure ---

    [Fact]
    public void dialog_names_the_real_data_directory() => Assert.Contains(_data.BaseDir, _privacy.LocationsText);

    [Fact]
    public void dialog_names_where_settings_live() => Assert.Contains(_settings.Location, _privacy.LocationsText);

    [Fact]
    public void the_locations_read_as_the_dialog_wrote_them() =>
        Assert.Equal($"Data files:\n{_data.BaseDir}\n\nSettings:\n{_settings.Location}", _privacy.LocationsText);

    // Created on demand: nothing has been saved yet on a fresh install, and opening Explorer on a
    // path that does not exist just fails silently.
    [Fact]
    public void open_data_folder_opens_the_store_directory()
    {
        Assert.False(Directory.Exists(_data.BaseDir));

        Assert.Equal(_data.BaseDir, _privacy.DataFolderToOpen());
        Assert.True(Directory.Exists(_data.BaseDir));
    }

    // --- counts ---

    [Fact]
    public void counts_reflect_what_is_actually_stored()
    {
        Populate();

        var counts = _privacy.CategoryCounts();

        Assert.Equal(2, counts["session_history"]);
        Assert.Equal(1, counts["custom_patterns"]);
        Assert.Equal(1, counts["last_selected_folders"]);
    }

    [Fact]
    public void counts_are_zero_on_a_clean_profile() =>
        Assert.All(_privacy.CategoryCounts().Where(c => c.Key != "settings"), c => Assert.Equal(0, c.Value));

    [Fact]
    public void every_category_has_a_count() =>
        Assert.Equal(PrivacyData.Categories.Select(c => c.Key), _privacy.CategoryCounts().Keys);

    [Fact]
    public void a_category_reads_its_label_and_count()
    {
        Populate();

        Assert.Equal("Session history (2 stored)", _privacy.LabelFor("session_history"));
        Assert.Equal("Saved sessions (nothing stored)", _privacy.LabelFor("saved_sessions"));
    }

    // --- deleting ---

    [Fact]
    public void clearing_session_history_empties_it_in_memory_and_on_disk()
    {
        Populate();

        _privacy.ClearCategories(["session_history"]);

        Assert.Empty(_app.Score.GetHistory());
        Assert.False(File.Exists(_data.PathFor("session_history")));
    }

    [Fact]
    public void clearing_custom_patterns_removes_them_everywhere()
    {
        Populate();

        _privacy.ClearCategories(["custom_patterns"]);

        Assert.DoesNotContain("Mine", _app.Beat.AvailableBeatPatterns.Keys);
        Assert.DoesNotContain("Mine", _app.Beat.SelectedBeatPatterns);
        Assert.False(File.Exists(_data.PathFor("custom_patterns")));
        // The built-ins must survive - clearing custom data is not a rhythm reset.
        Assert.Contains("Standard Beat", _app.Beat.AvailableBeatPatterns.Keys);
    }

    [Fact]
    public void clearing_custom_phrase_files_forgets_them()
    {
        var phraseFile = Path.Combine(_tmp.FullName, "extra.json");
        File.WriteAllText(phraseFile, """{"session_started": ["hello"]}""");
        _app.Callouts!.LoadCustomFile(phraseFile, "en");
        Assert.NotEmpty(_app.Callouts.CustomPhraseFiles);

        _privacy.ClearCategories(["custom_phrase_files"]);

        Assert.Empty(_app.Callouts.CustomPhraseFiles);
        Assert.False(File.Exists(_data.PathFor("custom_phrase_files")));
        Assert.DoesNotContain(_app.Callouts.CalloutData["en"].Values, tone => tone.GetValueOrDefault("session_started", []).Contains("hello"));
    }

    [Fact]
    public void clearing_media_folders_removes_the_stored_paths()
    {
        Populate();

        _privacy.ClearCategories(["last_selected_folders"]);

        Assert.False(File.Exists(_data.PathFor("last_selected_folders")));
        Assert.Null(_data.Load("last_selected_folders"));
    }

    // The folder of the user's own character models says where their collection lives, as a media
    // folder does: disclosed here and deletable.
    [Fact]
    public void the_own_character_folder_is_disclosed_and_forgotten()
    {
        var own = Directory.CreateDirectory(Path.Combine(_tmp.FullName, "models")).FullName;
        Assert.Equal(0, _privacy.CategoryCounts()["character_folder"]);
        _app.Character.ChooseFolder(own);

        Assert.Equal(1, _privacy.CategoryCounts()["character_folder"]);
        _privacy.ClearCategories(["character_folder"]);

        Assert.Null(_app.Character.Folder);
        Assert.Equal(0, _privacy.CategoryCounts()["character_folder"]);
        Assert.Equal("", _settings.GetString("Character/folder", "x"));
    }

    // Godot's compiled shaders and pipelines, kept so that a start does not stall compiling them
    // again (the user's call): named, counted and deletable like everything else the app leaves.
    [Fact]
    public void the_graphics_cache_is_disclosed_and_deleted()
    {
        var cache = Directory.CreateDirectory(Path.Combine(_tmp.FullName, "godot")).FullName;
        Directory.CreateDirectory(Path.Combine(cache, "shader_cache", "CanvasShaderRD"));
        File.WriteAllText(Path.Combine(cache, "shader_cache", "CanvasShaderRD", "a.cache"), "a");
        Directory.CreateDirectory(Path.Combine(cache, "vulkan"));
        File.WriteAllText(Path.Combine(cache, "vulkan", "pipelines.cache"), "b");
        var privacy = new PrivacyData(_app, cache);

        Assert.Contains(cache, privacy.LocationsText);
        Assert.Equal(2, privacy.CategoryCounts()["graphics_cache"]);
        privacy.ClearCategories(["graphics_cache"]);

        Assert.Equal(0, privacy.CategoryCounts()["graphics_cache"]);
        Assert.False(File.Exists(Path.Combine(cache, "vulkan", "pipelines.cache")));
    }

    [Fact]
    public void without_a_graphics_cache_there_is_none_to_count_or_delete()
    {
        Assert.Equal(0, _privacy.CategoryCounts()["graphics_cache"]);
        _privacy.ClearCategories(["graphics_cache"]);
        var missing = new PrivacyData(_app, Path.Combine(_tmp.FullName, "never-made"));
        Assert.Equal(0, missing.CategoryCounts()["graphics_cache"]);
        missing.ClearCategories(["graphics_cache"]);
    }

    [Fact]
    public void clearing_settings_wipes_them_but_keeps_the_data_files()
    {
        Populate();
        _settings.SetValue("GoonerApp/min_dur", 2.5);

        _privacy.ClearCategories(["settings"]);

        Assert.Null(_settings.Value("GoonerApp/min_dur"));
        Assert.True(File.Exists(_data.PathFor("session_history")));
    }

    [Fact]
    public void clearing_one_category_leaves_the_others_alone()
    {
        Populate();

        _privacy.ClearCategories(["session_history"]);

        Assert.True(File.Exists(_data.PathFor("custom_patterns")));
        Assert.True(File.Exists(_data.PathFor("last_selected_folders")));
    }

    [Fact]
    public void delete_button_clears_every_ticked_category()
    {
        Populate();
        _privacy.Select("session_history", true);
        _privacy.Select("last_selected_folders", true);

        Assert.Equal(["session_history", "last_selected_folders"], _privacy.SelectedKeys);
        _privacy.DeleteSelected();

        Assert.False(File.Exists(_data.PathFor("session_history")));
        Assert.False(File.Exists(_data.PathFor("last_selected_folders")));
        Assert.True(File.Exists(_data.PathFor("custom_patterns")));
        // And the boxes are unticked again.
        Assert.Empty(_privacy.SelectedKeys);
    }

    [Fact]
    public void delete_button_is_disabled_until_something_is_ticked()
    {
        Assert.False(_privacy.CanDelete);

        _privacy.Select("session_history", true);
        Assert.True(_privacy.CanDelete);

        _privacy.Select("session_history", false);
        Assert.False(_privacy.CanDelete);
    }

    [Fact]
    public void the_prompt_lists_what_it_is_about_to_delete()
    {
        _privacy.Select("session_history", true);
        _privacy.Select("settings", true);

        Assert.Equal("This permanently deletes:\n\n  - Session history\n  - All settings\n\nThis cannot be undone.", _privacy.ConfirmationText());
    }

    [Fact]
    public void counts_refresh_after_a_deletion()
    {
        Populate();
        _privacy.Select("session_history", true);

        _privacy.DeleteSelected();

        Assert.Equal(0, _privacy.CategoryCounts()["session_history"]);
    }

    // A .corrupt or .tmp sibling holds the same data - leaving it behind would make "delete my
    // data" a lie.
    [Fact]
    public void deleting_also_removes_quarantined_and_temp_leftovers()
    {
        _data.Save("session_history", new JsonArray(new JsonObject { ["total_dur_sec"] = 1 }));
        var path = _data.PathFor("session_history");
        File.WriteAllText(Path.ChangeExtension(path, ".json.corrupt"), "[]");
        File.WriteAllText(Path.ChangeExtension(path, ".json.tmp"), "[]");

        _privacy.ClearCategories(["session_history"]);

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(Path.ChangeExtension(path, ".json.corrupt")));
        Assert.False(File.Exists(Path.ChangeExtension(path, ".json.tmp")));
    }

    // --- the diagnostic log is user data too ---

    [Fact]
    public void the_log_is_offered_as_its_own_category() =>
        Assert.Contains(PrivacyData.Categories, c => c.Key == "diagnostic_log");

    [Fact]
    public void the_log_count_reflects_the_files_on_disk()
    {
        _app.SetDiagnosticLog(true);
        AppLog.Info("Test", "a line");

        Assert.Equal(1, _privacy.CategoryCounts()["diagnostic_log"]);
    }

    [Fact]
    public void clearing_the_log_removes_it()
    {
        _app.SetDiagnosticLog(true);
        AppLog.Info("Test", "a line");

        _privacy.ClearCategories(["diagnostic_log"]);

        Assert.Empty(AppLog.LogFilePaths(_data.BaseDir));
    }

    [Fact]
    public void clearing_the_log_leaves_logging_working()
    {
        _app.SetDiagnosticLog(true);
        AppLog.Info("Test", "before");

        _privacy.ClearCategories(["diagnostic_log"]);
        AppLog.Info("Test", "after");

        var contents = File.ReadAllText(AppLog.LogFilePath(_data.BaseDir));
        Assert.Contains("after", contents);
        Assert.DoesNotContain("before", contents);
    }

    // --- the log is controlled here, next to where it is disclosed and deleted ---

    [Fact]
    public void the_log_toggle_lives_in_this_dialog() => Assert.Equal(_app.DiagnosticLog, _privacy.DiagnosticLog);

    // No Save button in this dialog - Open folder and Delete act at once, so this does too.
    [Fact]
    public void ticking_the_toggle_starts_logging_immediately()
    {
        _privacy.SetDiagnosticLog(true, _privacy.DiagnosticLogLevel);

        AppLog.Info("Test", "recorded right away");

        Assert.True(_app.DiagnosticLog);
        Assert.Contains("recorded right away", File.ReadAllText(AppLog.LogFilePath(_data.BaseDir)));
    }

    [Fact]
    public void unticking_the_toggle_stops_logging_immediately()
    {
        _privacy.SetDiagnosticLog(true, _privacy.DiagnosticLogLevel);
        _privacy.SetDiagnosticLog(false, _privacy.DiagnosticLogLevel);

        AppLog.Info("Test", "must not be recorded");

        Assert.False(_app.DiagnosticLog);
        // The file stays - switching off stops writing, it does not delete what was already
        // recorded (that is what the Delete section is for).
        Assert.DoesNotContain("must not be recorded", File.ReadAllText(AppLog.LogFilePath(_data.BaseDir)));
    }

    [Fact]
    public void the_level_dropdown_offers_exactly_the_supported_levels() =>
        Assert.Equal(AppLog.Levels, PrivacyData.LevelLabels.Keys);

    [Fact]
    public void the_level_dropdown_starts_on_the_saved_level()
    {
        _app.SetDiagnosticLog(_app.DiagnosticLog, "ERROR");

        Assert.Equal("ERROR", new PrivacyData(_app).DiagnosticLogLevel);
    }

    [Fact]
    public void choosing_a_level_applies_and_persists_it()
    {
        _privacy.SetDiagnosticLog(true, "WARNING");

        AppLog.Info("Test", "below the threshold");
        AppLog.Warning("Test", "at the threshold");

        var contents = File.ReadAllText(AppLog.LogFilePath(_data.BaseDir));
        Assert.DoesNotContain("below the threshold", contents);
        Assert.Contains("at the threshold", contents);
        Assert.Equal("WARNING", (string?)_settings.Value("GoonerApp/diagnostic_log_level"));
    }

    // --- saved sessions ---

    [Fact]
    public void saved_sessions_are_counted()
    {
        SessionFiles.StoreSession(_data, new JsonObject { ["format"] = 1, ["segments"] = new JsonArray(), ["media"] = new JsonArray() });

        Assert.Equal(1, _privacy.CategoryCounts()["saved_sessions"]);
    }

    [Fact]
    public void saved_sessions_can_be_deleted()
    {
        SessionFiles.StoreSession(_data, new JsonObject { ["format"] = 1, ["segments"] = new JsonArray(), ["media"] = new JsonArray() });

        _privacy.ClearCategories(["saved_sessions"]);

        Assert.Empty(SessionFiles.LoadSavedSessions(_data));
    }

    // This is the one category that carries paths into the data directory, and a user deciding
    // what to wipe before handing the laptop over needs to know which one that is.
    [Fact]
    public void the_saved_sessions_entry_says_it_holds_media_paths() =>
        Assert.Contains("path", PrivacyData.Categories.Single(c => c.Key == "saved_sessions").Description, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void achievements_are_counted_and_deletable()
    {
        _app.Achievements.MarkUnlocked("endurance_45", "2026-09-17 21:00");
        Assert.Equal(1, _privacy.CategoryCounts()["achievements"]);

        _privacy.ClearCategories(["achievements"]);

        Assert.Empty(_app.Achievements.UnlockedIds);
        Assert.Equal(0, _privacy.CategoryCounts()["achievements"]);
    }
}
