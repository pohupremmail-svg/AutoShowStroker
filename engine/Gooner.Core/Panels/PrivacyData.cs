using System.Text.Json.Nodes;
using Gooner.Core.App;
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
        new("session_history", "Session history", "every recorded session and your personal records"),
        new("saved_sessions", "Saved sessions",
            "the sessions you saved to replay - the one thing here that stores the paths of your media files"),
        new("achievements", "Achievements", "which achievements you have unlocked, and when"),
        new("custom_patterns", "Custom rhythm patterns", "the patterns you built in the pattern editor"),
        new("custom_phrase_files", "Custom phrase files", "the callout files you added"),
        new("last_selected_folders", "Last used media folders", "the folder paths the picker remembers"),
        new("diagnostic_log", "Diagnostic log", "the opt-in log file, if you turned it on"),
        new("settings", "All settings", "every slider, toggle and the selected language"),
    ];

    // Plain-language names for AppLog.Levels - the stored value stays the level name.
    public static IReadOnlyDictionary<string, string> LevelLabels { get; } = new Dictionary<string, string>
    {
        ["INFO"] = "Everything (recommended)",
        ["WARNING"] = "Problems only",
        ["ERROR"] = "Errors only",
    };

    private readonly GoonerApp _app;
    private readonly List<string> _selected = [];

    public PrivacyData(GoonerApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        _app = app;
    }

    // --- disclosure ---

    public string LocationsText =>
        $"Data files:\n{_app.DataStore?.BaseDir}\n\nSettings:\n{_app.Settings?.Location}";

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
            AppLog.Error(Log, "Could not create the data folder: {0}", e.Message);
            return null;
        }
        return dir;
    }

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
            // Files, not lines: rotated backups count too, and reading them to count lines just
            // to label a checkbox would be silly.
            ["diagnostic_log"] = store is null ? 0 : AppLog.LogFilePaths(store.BaseDir).Count,
            ["settings"] = _app.Settings?.Keys.Count ?? 0,
        };
    }

    // What a category's box says: its name and how much of it there is.
    public string LabelFor(string key)
    {
        var count = CategoryCounts()[key];
        var stored = count == 0 ? "nothing stored" : $"{count} stored";
        return $"{Categories.Single(c => c.Key == key).Label} ({stored})";
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
        var listed = string.Join("\n", SelectedKeys.Select(key => $"  - {Categories.Single(c => c.Key == key).Label}"));
        return $"This permanently deletes:\n\n{listed}\n\nThis cannot be undone.";
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
                case "diagnostic_log":
                    if (_app.DataStore is { } store) AppLog.DeleteLogFiles(store.BaseDir);
                    break;
                case "settings":
                    _app.Settings?.Clear();
                    break;
            }
        }
    }
}
