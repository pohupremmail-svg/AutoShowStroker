using Gooner.Core.App;
using Gooner.Core.Localization;
using Gooner.Core.Settings;

namespace Gooner.Core.Characters;

// Which figure the app shows: any .vrm in res/characters, any in a folder of the user's own, or none.
// Picked in Settings > Look. Kept are the file's name - "own/<name>" for one from the user's folder -
// and that folder's path, which is the user's to see and to delete (Privacy & Data) and is never
// logged: like a media folder, it says where their collection lives. Until one is picked, and when
// the picked one is gone, the first by name.
public sealed class CharacterChoice : ISettingsOwner
{
    public const string None = "none";

    private const string OwnPrefix = "own/";

    public static string SettingsGroup => "Character";

    // file: empty means nothing picked yet, so the first one found. folder: empty means none.
    public static IReadOnlyDictionary<string, object> Defaults { get; } = new Dictionary<string, object>
    {
        ["file"] = "",
        ["folder"] = "",
        ["in_session"] = true,
    };

    private readonly ISettingsStore? _settings;
    private readonly string? _dir;
    private readonly IReadOnlyList<string> _bundled;
    private IReadOnlyList<string> _own = [];

    public CharacterChoice(ISettingsStore? settings, string? dir)
    {
        _settings = settings;
        _dir = dir;
        _bundled = ModelsIn(dir);
        var folder = settings?.GetString($"{SettingsGroup}/folder", (string)Defaults["folder"]) ?? "";
        Folder = folder.Length > 0 ? folder : null;
        _own = ModelsIn(Folder);
        InSession = settings?.GetBool($"{SettingsGroup}/in_session", (bool)Defaults["in_session"]) ?? (bool)Defaults["in_session"];
        var stored = settings?.GetString($"{SettingsGroup}/file", (string)Defaults["file"]) ?? "";
        File = stored == None || Values.Contains(stored) ? stored : First;
        // Still written down from the last start: the app went down while this one was on its way
        // in. It is loaded at every start, so it would take every start down with it - switched off.
        var loading = settings?.GetString(LoadingKey, "") ?? "";
        if (loading.Length == 0) return;
        settings!.Remove(LoadingKey);
        if (loading != File || File == None) return;
        Abandoned = Label(File);
        Choose(None);
    }

    private static string LoadingKey => $"{SettingsGroup}/loading";

    // The figure the last start went down with, by its name; switched off on this start, and said
    // so. Null on a start after a clean one.
    public string? Abandoned { get; }

    public static string AbandonedTitle => L10n.T("A character was switched off");

    public static string AbandonedText(string name) => L10n.F(
        "{0} took the app down the last time it was loaded, so it has been switched off and the start " +
        "screen shows the logo instead. Pick it again under Settings > Look to try once more.", name);

    // The picked figure is about to load: written down until it is safely on screen (LoadSettled).
    public void LoadStarting()
    {
        if (File != None) _settings?.SetValue(LoadingKey, File);
    }

    // On screen without taking the app down, or the app is closing while it loaded - which is not it
    // taking the app down either.
    public void LoadSettled() => _settings?.Remove(LoadingKey);

    // Whether the character comes through with each callout during a session (the telepathy frame).
    public bool InSession { get; set; }

    // The user's own folder of models, or null.
    public string? Folder { get; private set; }

    // A file name in res/characters, "own/<name>" for one in Folder, or None.
    public string File { get; private set; }

    // What a reset goes back to.
    public string First => Values.FirstOrDefault() ?? None;

    // The file to load, or null for no character.
    public string? Path => PathOf(File);

    // Each figure under its file's name - the bundled ones, then the user's own - and then none.
    public IReadOnlyList<LookOption> Options => OptionsOf(_own);

    // The list as it would be with that folder chosen, for Settings while it is still a draft.
    public IReadOnlyList<LookOption> OptionsFor(string? folder) => OptionsOf(ModelsIn(folder));

    public void Choose(string file)
    {
        if (file != None && !Values.Contains(file)) return;
        File = file;
        _settings?.SetValue($"{SettingsGroup}/file", file);
    }

    // A folder of the user's own models; null or empty forgets it. A model from the old folder that
    // is no longer on offer gives way to the first one.
    public void ChooseFolder(string? folder)
    {
        Folder = string.IsNullOrEmpty(folder) ? null : folder;
        _settings?.SetValue($"{SettingsGroup}/folder", Folder ?? "");
        _own = ModelsIn(Folder);
        if (File != None && !Values.Contains(File)) Choose(First);
    }

    private IEnumerable<string> Values => _bundled.Concat(_own.Select(name => OwnPrefix + name));

    private IReadOnlyList<LookOption> OptionsOf(IReadOnlyList<string> own) =>
    [
        .. _bundled.Select(name => new LookOption(name, Label(name))),
        .. own.Select(name => new LookOption(OwnPrefix + name, Label(name))),
        new LookOption(None, L10n.T("None")),
    ];

    private string? PathOf(string file)
    {
        if (file == None) return null;
        if (file.StartsWith(OwnPrefix, StringComparison.Ordinal))
            return Folder is null ? null : System.IO.Path.Combine(Folder, file[OwnPrefix.Length..]);
        return _dir is null ? null : System.IO.Path.Combine(_dir, file);
    }

    private static string Label(string file) => System.IO.Path.GetFileNameWithoutExtension(file);

    // Not a "*.vrm" pattern: on Windows that also matches *.vrma, the format's animations.
    private static IReadOnlyList<string> ModelsIn(string? dir) =>
        dir is not null && Directory.Exists(dir)
            ? [.. Directory.EnumerateFiles(dir)
                .Where(file => string.Equals(System.IO.Path.GetExtension(file), ".vrm", StringComparison.OrdinalIgnoreCase))
                .Select(file => System.IO.Path.GetFileName(file))
                .Order(StringComparer.Ordinal)]
            : [];
}
