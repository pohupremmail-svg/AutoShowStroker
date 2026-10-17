using Gooner.Core.App;
using Gooner.Core.Settings;

namespace Gooner.Core.Localization;

// Which language the app's own words are in - the start screen, the panels, the settings, the guide.
// A setting of its own, apart from the language the callouts speak in: the two are picked
// separately. English until another is picked, whatever language Windows is in (the user's call);
// one this build does not ship reads as English.
public sealed class UiLanguage : ISettingsOwner
{
    public static string SettingsGroup => "Ui";

    public static IReadOnlyDictionary<string, object> Defaults { get; } = new Dictionary<string, object>
    {
        ["language"] = "en",
    };

    private readonly ISettingsStore? _settings;
    private readonly string? _localeDir;

    public UiLanguage(ISettingsStore? settings, string? localeDir)
    {
        _settings = settings;
        _localeDir = localeDir;
        Available = Catalogue.Available(localeDir);
        var fallback = (string)Defaults["language"];
        var stored = settings?.GetString($"{SettingsGroup}/language", fallback) ?? fallback;
        Language = Available.Contains(stored) ? stored : fallback;
    }

    // A code: en, de, fr.
    public string Language { get; private set; }

    public IReadOnlyList<string> Available { get; }

    // Every language on offer, each under its own name.
    public IReadOnlyList<LookOption> Options => [.. Available.Select(code => new LookOption(code, Catalogue.NativeName(code)))];

    // Makes it the language the app speaks in.
    public void Apply() => L10n.Use(Catalogue.Load(_localeDir, Language));

    // Kept and switched to; a language not on offer changes nothing.
    public void Choose(string code)
    {
        if (!Available.Contains(code)) return;
        _settings?.SetValue($"{SettingsGroup}/language", code);
        var changed = code != Language || L10n.Language != code;
        Language = code;
        if (changed) Apply();
    }
}
