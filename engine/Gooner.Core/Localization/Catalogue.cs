using System.Text.Json;
using System.Text.Json.Nodes;
using Gooner.Core.Logging;

namespace Gooner.Core.Localization;

// One language's translations: res/locales/<code>/*.json, every file of the folder together, each an
// object of English text -> translation. How the texts are split over the files is up to whoever
// writes them; the app reads them as one. English has no folder - it is the code itself.
//
// Like the phrase files, a file that cannot be read costs only itself, with a warning: a typo in the
// French guide must not take the French settings down with it.
public sealed class Catalogue
{
    private const string Log = "Localization";

    private static readonly Dictionary<string, string> NativeNames = new()
    {
        ["en"] = "English",
        ["de"] = "Deutsch",
        ["fr"] = "Français",
    };

    private readonly IReadOnlyDictionary<string, string> _entries;
    private readonly Func<string, string?>? _lookup;

    public Catalogue(string code, IReadOnlyDictionary<string, string> entries)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(entries);
        Code = code;
        _entries = entries;
    }

    private Catalogue(string code, Func<string, string?> lookup)
    {
        Code = code;
        _entries = new Dictionary<string, string>();
        _lookup = lookup;
    }

    public static Catalogue English { get; } = new("en", new Dictionary<string, string>());

    // Brackets every text it is asked for, so a test can see which texts went through a catalogue
    // and which stay English whatever the language.
    public static Catalogue Pseudo { get; } = new("qps", text => $"⟦{text}⟧");

    public string Code { get; }

    public int Count => _entries.Count;

    public string? Lookup(string english) =>
        _lookup is not null ? _lookup(english) : _entries.TryGetValue(english, out var translation) ? translation : null;

    // CLDR's rule for "one": exactly one in English and German, none or one in French.
    public bool TakesSingular(long count) => Code == "fr" ? count is 0 or 1 : count == 1;

    // A language under its own name, so someone who cannot read the one the app is in finds theirs.
    public static string NativeName(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return NativeNames.GetValueOrDefault(code, code);
    }

    // English, then every folder of res/locales that holds a file, in folder order.
    public static IReadOnlyList<string> Available(string? localeDir)
    {
        var languages = new List<string> { English.Code };
        if (localeDir is null || !Directory.Exists(localeDir)) return languages;
        languages.AddRange(Directory.GetDirectories(localeDir)
            .Where(dir => Directory.GetFiles(dir, "*.json").Length > 0)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(code => code != English.Code)
            .Order(StringComparer.Ordinal));
        return languages;
    }

    // English for "en", and for a language there is no folder of.
    public static Catalogue Load(string? localeDir, string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (code == English.Code || localeDir is null) return English;
        var dir = Path.Combine(localeDir, code);
        if (!Directory.Exists(dir)) return English;
        var entries = new Dictionary<string, string>();
        // In name order, so which of two translations of one text wins does not depend on the disk.
        foreach (var file in Directory.GetFiles(dir, "*.json").Order(StringComparer.Ordinal))
        {
            List<KeyValuePair<string, JsonNode?>> parsed;
            try
            {
                if (JsonNode.Parse(File.ReadAllText(file)) is not JsonObject obj)
                {
                    AppLog.Warning(Log, "The translation file {0}/{1} is not an object of texts; skipped.", code, Path.GetFileName(file));
                    continue;
                }
                // Read out here: an object is only built once it is first read, and a text written
                // twice in one file throws then.
                parsed = [.. obj];
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                AppLog.Warning(Log, "Could not read the translation file {0}/{1}: {2}", code, Path.GetFileName(file), e.Message);
                continue;
            }
            foreach (var (english, value) in parsed)
            {
                if (value is JsonValue v && v.GetValueKind() == JsonValueKind.String && ((string)v!).Length > 0)
                    entries.TryAdd(english, (string)v!);
            }
        }
        return new Catalogue(code, entries);
    }
}
