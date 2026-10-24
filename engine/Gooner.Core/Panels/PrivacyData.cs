using System.Text.Json.Nodes;
using Gooner.Core.App;
using Gooner.Core.Localization;
using Gooner.Core.Logging;
using Gooner.Core.Sessions;

namespace Gooner.Core.Panels;

public sealed record PrivacyCategory(string Key, string Label, string Description);

// Port of src/PrivacyDataDialog.py without its widgets: says where the app keeps things, and lets
// the user delete any of it.
//
// The Guide's Privacy tab promises nothing leaves the machine. That promise is only half of it: a
// user who wants their traces gone also has to be able to find them and remove them, and a
// portable program has no uninstaller. Deletion is per category rather than one big button - a
// user clearing the folder paths before handing the laptop over should not have to lose their
// session history too.
//
// The diagnostic log's switch and level live here rather than in Settings: it is the one thing in
// the app that writes extra data about the user, so it belongs where that data is disclosed and
// deleted. Both apply at once - there is no Save button here, and Open folder and Delete act on
// click as well.
public sealed class PrivacyData
{
    private const string Log = "PrivacyDataDialog";

    // The key doubles as the data-store file name for the JSON categories; the diagnostic log and
    // the settings are handled on their own.
    public static IReadOnlyList<PrivacyCategory> Categories { get; } =
    [
        new("session_history", L10n.N("Session history"), L10n.N("every recorded session and your personal records")),
        new("saved_sessions", L10n.N("Saved sessions"),
            L10n.N("the sessions you saved to replay - the one thing here that stores the paths of your media files")),
        new("achievements", L10n.N("Achievements"), L10n.N("which achievements you have unlocked, and when")),
        new("custom_patterns", L10n.N("Custom rhythm patterns"), L10n.N("the patterns you built in the pattern editor")),
        new("custom_phrase_files", L10n.N("Custom phrase files"), L10n.N("the callout files you added")),
        new("last_selected_folders", L10n.N("Last used media folders"), L10n.N("the folder paths the picker remembers")),
        new("character_folder", L10n.N("Folder of your own characters"), L10n.N("where your own .vrm models are")),
        new("diagnostic_log", L10n.N("Diagnostic log"), L10n.N("the opt-in log file, if you turned it on")),
        new("graphics_cache", L10n.N("Graphics cache"),
            L10n.N("compiled shaders, so that the app starts without stutters - made again at the next start")),
        new("settings", L10n.N("All settings"), L10n.N("every slider, toggle and the selected language")),
    ];

    // Plain-language names for AppLog.Levels - the stored value stays the level name.
    public static IReadOnlyDictionary<string, string> LevelLabels { get; } = new Dictionary<string, string>
    {
        ["INFO"] = L10n.N("Everything (recommended)"),
        ["WARNING"] = L10n.N("Problems only"),
        ["ERROR"] = L10n.N("Errors only"),
    };

    private readonly GoonerApp _app;
    private readonly string? _graphicsCache;
    private readonly List<string> _selected = [];

    // `graphicsCache` is the folder the engine keeps its compiled shaders and pipelines in (Godot's
    // user folder, which holds nothing else of ours); null where there is none.
    public PrivacyData(GoonerApp app, string? graphicsCache = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        _app = app;
        // Godot names it with forward slashes; the app's own paths are written the system's way.
        _graphicsCache = graphicsCache?.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }

    // --- disclosure ---

    public string LocationsText => _graphicsCache is null
        ? L10n.F("Data files:\n{0}\n\nSettings:\n{1}", _app.DataStore?.BaseDir, _app.Settings?.Location)
        : L10n.F("Data files:\n{0}\n\nSettings:\n{1}\n\nGraphics cache:\n{2}", _app.DataStore?.BaseDir, _app.Settings?.Location, _graphicsCache);

    // The data folder, created first if nothing has been saved yet - opening Explorer on a path that
    // does not exist just fails silently. Null when it cannot be.
    public string? DataFolderToOpen()
    {
        if (_app.DataStore?.BaseDir is not { } dir) return null;
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Error(Log, "Could not create the data folder: {0}", AppLog.Describe(e));
            return null;
        }
        return dir;
    }

    // The one thing in the app that opens a connection while it runs, disclosed here as well as in
    // the settings: this is where the app says what it keeps and what it sends, and a promise that
    // nothing leaves the machine has to name its own exception. The switch stays in the settings,
    // beside the address and the test buttons it needs. Null without device output.
    public string? DeviceOutputNote => _app.Intiface is null ? null : L10n.T(
        "Device output (Settings > Device) is the one part of GoonerApp that opens a " +
        "network connection. It is off every time the app starts and has to be turned " +
        "on deliberately, it only ever goes to the address you type in yourself, and " +
        "it sends movement and stop commands and nothing else - no media, no paths, no " +
        "history. Deleting All settings below also disconnects it.");

    // --- the diagnostic log ---

    public bool DiagnosticLog => _app.DiagnosticLog;

    public string DiagnosticLogLevel => _app.DiagnosticLogLevel;

    public void SetDiagnosticLog(bool enabled, string level) => _app.SetDiagnosticLog(enabled, level);

    // --- counts ---

    public IReadOnlyDictionary<string, int> CategoryCounts()
    {
        var store = _app.DataStore;
        return new Dictionary<string, int>
        {
            ["session_history"] = _app.Score.GetHistory().Count,
            ["saved_sessions"] = store is null ? 0 : SessionFiles.LoadSavedSessions(store).Count,
            ["achievements"] = _app.Achievements.UnlockedIds.Count,
            ["custom_patterns"] = _app.Beat.CustomBeatPatterns.Count,
            ["custom_phrase_files"] = _app.Callouts?.CustomPhraseFiles.Count ?? 0,
            ["last_selected_folders"] = store?.Load("last_selected_folders") is JsonArray folders ? folders.Count : 0,
            ["character_folder"] = _app.Character.Folder is null ? 0 : 1,
            // Files, not lines: rotated backups count too, and reading them to count lines just
            // to label a checkbox would be silly.
            ["diagnostic_log"] = store is null ? 0 : AppLog.LogFilePaths(store.BaseDir).Count,
            ["graphics_cache"] = _graphicsCache is { } cache && Directory.Exists(cache)
                ? Directory.EnumerateFiles(cache, "*", SearchOption.AllDirectories).Count()
                : 0,
            ["settings"] = _app.Settings?.Keys.Count ?? 0,
        };
    }

    // What a category's box says: its name and how much of it there is.
    public string LabelFor(string key)
    {
        var count = CategoryCounts()[key];
        var stored = count == 0 ? L10n.T("nothing stored") : L10n.F("{0} stored", count);
        return L10n.F("{0} ({1})", L10n.T(Categories.Single(c => c.Key == key).Label), stored);
    }

    // --- deleting ---

    public IReadOnlyList<string> SelectedKeys => [.. Categories.Select(c => c.Key).Where(_selected.Contains)];

    public bool CanDelete => _selected.Count > 0;

    public void Select(string key, bool selected)
    {
        _selected.Remove(key);
        if (selected) _selected.Add(key);
    }

    public string ConfirmationText()
    {
        var listed = string.Join("\n", SelectedKeys.Select(key => $"  - {L10n.T(Categories.Single(c => c.Key == key).Label)}"));
        return L10n.F("This permanently deletes:\n\n{0}\n\nThis cannot be undone.", listed);
    }

    // What Delete selected... does once the user has said yes: the ticked categories go, and the
    // boxes are unticked again.
    public void DeleteSelected()
    {
        ClearCategories(SelectedKeys);
        _selected.Clear();
    }

    // Deletes the named categories, resetting the live objects as well as the files. The in-memory
    // reset is the part that matters: dropping session_history.json while ScoreTracker still holds
    // the list would just write it back at the next session end.
    public void ClearCategories(IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        AppLog.Info(Log, "Clearing user data: {0}", string.Join(", ", keys));
        foreach (var key in keys)
        {
            switch (key)
            {
                case "session_history":
                    _app.Score.ClearHistory();
                    break;
                case "saved_sessions":
                    _app.DataStore?.Delete(SessionFiles.SavedSessionsKey);
                    break;
                case "achievements":
                    // Cleared in memory too, or the next session end would write them back.
                    _app.Achievements.Clear();
                    break;
                case "custom_patterns":
                    _app.Beat.ClearCustomPatterns();
                    break;
                case "custom_phrase_files":
                    _app.Callouts?.ClearCustomPhraseFiles();
                    break;
                case "last_selected_folders":
                    _app.DataStore?.Delete("last_selected_folders");
                    break;
                case "character_folder":
                    _app.Character.ChooseFolder(null);
                    break;
                case "diagnostic_log":
                    if (_app.DataStore is { } store) AppLog.DeleteLogFiles(store.BaseDir);
                    break;
                case "graphics_cache":
                    DeleteGraphicsCache();
                    break;
                case "settings":
                    // Before the wipe, because disconnecting writes the cleared-out address back. A
                    // live connection outliving "delete all my settings" is not what deleting them
                    // is supposed to mean.
                    _app.Intiface?.ForgetSettings();
                    _app.Settings?.Clear();
                    break;
            }
        }
    }

    // What is in the folder, not the folder: Godot makes it again at every start anyway.
    private void DeleteGraphicsCache()
    {
        if (_graphicsCache is not { } cache || !Directory.Exists(cache)) return;
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(cache)) Directory.Delete(dir, recursive: true);
            foreach (var file in Directory.EnumerateFiles(cache)) File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Warning(Log, "Could not delete the graphics cache: {0}", AppLog.Describe(e));
        }
    }
}
