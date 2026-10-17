using Gooner.Core.App;
using Gooner.Core.Localization;
using Gooner.Core.Settings;

namespace Gooner.Core.Characters;

// Which figure stands on the start screen: any .vrm in res/characters, or none. Users drop their own
// into that folder of their installation and pick it in Settings > Look. Only the file's name is
// kept - the folder is the app's own. Until one is picked, and when the picked one is gone, the
// first by name.
public sealed class CharacterChoice : ISettingsOwner
{
    public const string None = "none";

    public static string SettingsGroup => "Character";

    // Empty: nothing picked yet, so the first one found.
    public static IReadOnlyDictionary<string, object> Defaults { get; } = new Dictionary<string, object>
    {
        ["file"] = "",
    };

    private readonly ISettingsStore? _settings;
    private readonly string? _dir;
    private readonly IReadOnlyList<string> _files;

    public CharacterChoice(ISettingsStore? settings, string? dir)
    {
        _settings = settings;
        _dir = dir;
        _files = dir is not null && Directory.Exists(dir)
            ? [.. Directory.EnumerateFiles(dir)
                // Not a "*.vrm" pattern: on Windows that also matches *.vrma, the format's animations.
                .Where(file => string.Equals(System.IO.Path.GetExtension(file), ".vrm", StringComparison.OrdinalIgnoreCase))
                .Select(file => System.IO.Path.GetFileName(file))
                .Order(StringComparer.Ordinal)]
            : [];
        var stored = settings?.GetString($"{SettingsGroup}/file", (string)Defaults["file"]) ?? "";
        File = stored == None || _files.Contains(stored) ? stored : First;
    }

    // A file name in the folder, or None.
    public string File { get; private set; }

    // What a reset goes back to.
    public string First => _files.Count > 0 ? _files[0] : None;

    // The file to load, or null for no character.
    public string? Path => File == None || _dir is null ? null : System.IO.Path.Combine(_dir, File);

    // Each figure under its file's name, then none.
    public IReadOnlyList<LookOption> Options =>
        [.. _files.Select(file => new LookOption(file, System.IO.Path.GetFileNameWithoutExtension(file))), new LookOption(None, L10n.T("None"))];

    public void Choose(string file)
    {
        if (file != None && !_files.Contains(file)) return;
        File = file;
        _settings?.SetValue($"{SettingsGroup}/file", file);
    }
}
