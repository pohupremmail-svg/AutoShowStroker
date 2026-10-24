using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gooner.Core.Climax;
using Gooner.Core.Data;
using Gooner.Core.Localization;
using Gooner.Core.Logging;
using Gooner.Core.Randomness;
using Gooner.Core.Sessions;
using Gooner.Core.Settings;

namespace Gooner.Core.Callouts;

public sealed class PhraseFileException : Exception
{
    public PhraseFileException() { }

    public PhraseFileException(string message) : base(message) { }

    public PhraseFileException(string message, Exception inner) : base(message, inner) { }
}

// A phrase file the user added: which language it speaks, and which tone - null for "any tone",
// which is also how files added before tones existed keep behaving.
public sealed record CustomPhraseFile(string Path, string Lang, string? Tone);

// The text callouts. Port of src/CalloutHandler.py.
//
// Lines come from <calloutDir>/<lang>/<tone>.json, keyed by trigger event. Language and tone are
// two independent axes: one language is active, any number of tones are, and a tone is drawn
// before a line from it, so every ticked tone gets an equal voice however many lines someone wrote
// for it. Both axes are discovered from the folder layout; ToneLabels only supplies display names
// and order. An unusable tone selection falls back to DefaultTone, never to "every tone on disk" -
// that is what keeps the degrading tones strictly opt-in.
//
// No timer in here: a shown tease ends TeaseTime later on the TimeProvider, and the host calls
// Tick() every frame to let that happen. Python's QTimer repeats and resends the hide every
// 7 seconds; only the first of those ever did anything.
// A line on screen with what it answers and the tone it was drawn from: what a character needs to
// react to it.
public sealed record Callout(string Text, string Category, string? Tone);

public sealed class CalloutHandler : ICalloutVoice, ISettingsOwner
{
    public static string SettingsGroup => "CalloutHandler";
    private const string Log = "CalloutHandler";
    private const string CustomFilesName = "custom_phrase_files";

    public static readonly IReadOnlyList<string> TriggerKeys =
    [
        "beat_change_general",
        "beat_change_faster",
        "beat_change_slower",
        "pause_start",
        "pause_end",
        "media_skipped",
        "media_repeated",
        "session_started",
        "climax_real",
        "climax_ruined",
        "climax_denied",
        "fake_climax_reveal",
        // The user pressing "I Came" at a fake climax. Deliberately does *not* give the joke away -
        // the reveal is still on its own timer and lands seconds later.
        "fake_climax_fell_for",
        // The user pressing "I reached my Edge" - the one moment they ask the app for mercy.
        "edge_reached",
        // Not a trigger: the line under the name on the start screen, in the voice the app will
        // speak in (TitleLine). Nothing in a session says it.
        TitleKey,
    ];

    public const string TitleKey = "main_menu";

    // Ordered soft to harsh, with the detached one last - the order the settings offer them in.
    public static readonly IReadOnlyDictionary<string, string> ToneLabels = new OrderedDictionary<string, string>
    {
        ["flirty"] = L10n.N("Flirty"),
        ["shy"] = L10n.N("Shy"),
        ["bratty"] = L10n.N("Bratty"),
        ["drunk"] = L10n.N("Drunk"),
        ["girlfriend"] = L10n.N("Girlfriend Experience"),
        ["nurturing"] = L10n.N("Nurturing"),
        ["dominant"] = L10n.N("Dominant"),
        ["sadistic"] = L10n.N("Sadistic"),
        ["degrading"] = L10n.N("Degrading"),
        ["hard_degrading"] = L10n.N("Degrading (Hard)"),
        ["clinical"] = L10n.N("Clinical"),
    };

    // What the app said before there was a tone axis, so it stays the default for everyone who
    // upgrades - and the fallback whenever a selection turns out to be unusable.
    public const string DefaultTone = "flirty";

    // Single source of truth for the fallbacks, and for "Reset to defaults".
    public static IReadOnlyDictionary<string, object> Defaults { get; } = new Dictionary<string, object>
    {
        ["active_callout"] = false,
        ["talking_chance"] = 0.5,
        ["lang"] = "en",
        ["selected_tones"] = new[] { DefaultTone },
    };

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly ISettingsStore? _settings;
    private readonly UserDataStore? _dataStore;
    private readonly IRandomSource _random;
    private readonly TimeProvider _time;
    private readonly List<string> _availableLanguages = [];
    private List<CustomPhraseFile> _customPhraseFiles = [];
    private double? _teaseEndsAt;
    // Set by SessionStarted: the next beat change is the session's first rhythm, not a change.
    private bool _firstRhythm;

    public CalloutHandler(
        string calloutDir,
        ISettingsStore? settings = null,
        UserDataStore? dataStore = null,
        IRandomSource? random = null,
        TimeProvider? time = null)
    {
        CalloutDir = calloutDir;
        _settings = settings;
        _dataStore = dataStore;
        _random = random ?? new SystemRandomSource();
        _time = time ?? TimeProvider.System;
        Lang = (string)Defaults["lang"];

        LoadAvailableLanguages();
        ActiveCallout = (bool)Defaults["active_callout"];
        TalkingChance = (double)Defaults["talking_chance"];
        SelectedTones = [.. (string[])Defaults["selected_tones"]];

        if (_settings is not null)
        {
            ActiveCallout = _settings.GetBool(Key("active_callout"), ActiveCallout);
            SetLang(_settings.GetString(Key("selected_lang"), Lang));
            TalkingChance = _settings.GetDouble(Key("talking_chance"), TalkingChance);
            SetTones(_settings.GetStringList(Key("selected_tones")) ?? SelectedTones);
        }

        // Which phrase files the user added is their own data, so it lives in a data file rather
        // than the settings.
        if (_dataStore is not null)
        {
            _customPhraseFiles = LoadCustomPhraseFiles();
            ApplyStoredCustomFiles();
        }
    }

    public event Action<string>? TeaseShown;
    public event Action? TeaseHidden;

    // Each line as it goes on screen, with its trigger and tone - beside TeaseShown, which the HUD
    // and the Python side's tests only need the words of.
    public event Action<Callout>? CalloutSpoken;
    private string _lastCategory = "";
    private string? _lastTone;

    public string CalloutDir { get; }
    public TimeSpan TeaseTime { get; set; } = TimeSpan.FromSeconds(7);
    public bool IsTeasing { get; internal set; }
    public string Lang { get; private set; }
    public bool ActiveCallout { get; set; }
    public double TalkingChance { get; set; }
    public IReadOnlyList<string> SelectedTones { get; private set; }
    public double CurFreq { get; internal set; }
    public IReadOnlyList<string> AvailableLanguages => _availableLanguages;
    public IReadOnlyList<CustomPhraseFile> CustomPhraseFiles => _customPhraseFiles;

    // lang -> tone -> trigger key -> phrases.
    internal Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>> CalloutData { get; private set; } = [];

    private double Now => (_time.GetUtcNow() - DateTimeOffset.UnixEpoch).TotalSeconds;

    private static string Key(string name) => $"{SettingsGroup}/{name}";

    // --- loading ---

    private void LoadAvailableLanguages()
    {
        _availableLanguages.Clear();
        CalloutData = [];

        if (!Directory.Exists(CalloutDir))
        {
            // Never stop the app from starting over it: no folder means no callouts.
            AppLog.Error(Log, "No callout directory at {0} - callouts are disabled", CalloutDir);
            return;
        }

        foreach (var langDir in Directory.GetDirectories(CalloutDir).Order(StringComparer.OrdinalIgnoreCase))
        {
            var langCode = Path.GetFileName(langDir);
            var tones = new Dictionary<string, Dictionary<string, List<string>>>();

            foreach (var jsonFile in Directory.GetFiles(langDir, "*.json").Order(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (JsonNode.Parse(File.ReadAllText(jsonFile, StrictUtf8)) is JsonObject data)
                        tones[Path.GetFileNameWithoutExtension(jsonFile)] = Categories(data);
                    else
                        AppLog.Warning(Log, "Could not load the callout file {0}/{1}: not an object", langCode, Path.GetFileName(jsonFile));
                }
                catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or DecoderFallbackException)
                {
                    // One broken tone must not cost the user the whole language.
                    AppLog.Warning(Log, "Could not load the callout file {0}/{1}: {2}", langCode, Path.GetFileName(jsonFile), AppLog.Describe(e));
                }
            }

            _availableLanguages.Add(langCode);
            CalloutData[langCode] = tones;
        }

        if (_availableLanguages.Count > 0 && (!CalloutData.ContainsKey(Lang) || !_availableLanguages.Contains(Lang)))
            SetLang(_availableLanguages[0]);
    }

    // A shipped tone file's categories. Python keeps whatever the file holds and would fail at the
    // moment a malformed category is drawn from; this keeps only lists of strings, the same rule
    // custom files have always had. The schema tests keep the shipped files clean either way.
    private static Dictionary<string, List<string>> Categories(JsonObject data)
    {
        var categories = new Dictionary<string, List<string>>();
        foreach (var (key, value) in data)
            if (Phrases(value) is { } phrases)
                categories[key] = phrases;
        return categories;
    }

    private static List<string>? Phrases(JsonNode? value) =>
        value is JsonArray items && items.All(i => i is JsonValue v && v.GetValueKind() == JsonValueKind.String)
            ? [.. items.Select(i => (string)i!)]
            : null;

    // The language or the mix of tones changed - the voice the app speaks in. The start screen
    // draws its line again then.
    public event Action? VoiceChanged;

    public void SetLang(string lang)
    {
        if (CalloutData.ContainsKey(lang) && _availableLanguages.Contains(lang))
        {
            var changed = Lang != lang;
            Lang = lang;
            if (changed) VoiceChanged?.Invoke();
        }
        else
        {
            AppLog.Warning(Log, "Language {0} is not available", PyJson.Quote(lang));
        }
    }

    // --- tones ---

    // Declared tones first, in ToneLabels order, then anything else alphabetically.
    private static List<string> OrderedTones(IEnumerable<string> tones)
    {
        var present = tones.ToHashSet();
        return [.. ToneLabels.Keys.Where(present.Contains), .. present.Where(t => !ToneLabels.ContainsKey(t)).Order(StringComparer.Ordinal)];
    }

    public static string ToneLabel(string tone)
    {
        ArgumentNullException.ThrowIfNull(tone);
        return ToneLabels.TryGetValue(tone, out var label) ? L10n.T(label) : PyTitle(tone.Replace('_', ' '));
    }

    // Every tone any installed language ships - the set the settings offer.
    public IReadOnlyList<string> AvailableTones => OrderedTones(CalloutData.Values.SelectMany(tones => tones.Keys));

    public IReadOnlyList<string> TonesFor(string lang) =>
        OrderedTones(CalloutData.TryGetValue(lang, out var tones) ? tones.Keys : []);

    // Replaces the active tone mix, dropping anything no language ships.
    public void SetTones(IEnumerable<string>? tones)
    {
        var requested = tones?.ToList() ?? [];
        var available = AvailableTones;
        var dropped = requested.Where(t => !available.Contains(t)).ToList();
        if (dropped.Count > 0)
            AppLog.Warning(Log, "Ignoring unavailable callout tones: {0}", string.Join(", ", dropped));
        var before = SelectedTones;
        SelectedTones = [.. requested.Where(available.Contains)];
        if (!before.SequenceEqual(SelectedTones)) VoiceChanged?.Invoke();
    }

    // The selected tones the current language has files for. Falls back to the default tone rather
    // than to "everything on disk": an empty or stale selection must never quietly switch a harsher
    // tone on.
    internal IReadOnlyList<string> UsableTones()
    {
        var available = TonesFor(Lang);
        var usable = SelectedTones.Where(available.Contains).ToList();
        if (usable.Count > 0) return usable;
        if (available.Contains(DefaultTone)) return [DefaultTone];
        return available;
    }

    // --- custom phrase files ---

    private static JsonObject ReadCustomFile(string path)
    {
        JsonNode? data;
        try
        {
            data = JsonNode.Parse(File.ReadAllText(path, StrictUtf8));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            throw new PhraseFileException(L10n.F("Couldn't read {0}: {1}", path, e.Message), e);
        }
        catch (JsonException e)
        {
            throw new PhraseFileException(L10n.F("{0} isn't valid JSON: {1}", path, e.Message), e);
        }
        return data as JsonObject
            ?? throw new PhraseFileException(L10n.F("{0} must contain a JSON object of trigger key -> phrase list.", path));
    }

    // Tolerant like the shipped files: anything malformed - an unknown or typo'd trigger key, a
    // value that is not a list of strings - silently does not contribute.
    private void MergePhrases(string lang, string tone, JsonObject data)
    {
        foreach (var (triggerKey, value) in data)
        {
            if (!TriggerKeys.Contains(triggerKey) || Phrases(value) is not { } phrases) continue;
            if (!CalloutData.TryGetValue(lang, out var tones))
                CalloutData[lang] = tones = [];
            if (!tones.TryGetValue(tone, out var categories))
                tones[tone] = categories = [];
            if (!categories.TryGetValue(triggerKey, out var existing))
                categories[triggerKey] = existing = [];
            existing.AddRange(phrases);
        }
        if (!_availableLanguages.Contains(lang))
            _availableLanguages.Add(lang);
    }

    // A null tone means "any tone": the file contributes to whichever tones the language has.
    private void MergeCustomPhrases(string lang, string? tone, JsonObject data)
    {
        IReadOnlyList<string> targets = string.IsNullOrEmpty(tone) ? (TonesFor(lang) is { Count: > 0 } all ? all : [DefaultTone]) : [tone];
        foreach (var target in targets)
            MergePhrases(lang, target, data);
    }

    private void ApplyStoredCustomFiles()
    {
        foreach (var entry in _customPhraseFiles)
        {
            try
            {
                MergeCustomPhrases(entry.Lang, entry.Tone, ReadCustomFile(entry.Path));
            }
            catch (PhraseFileException)
            {
                // The file name only: the full path says where the user keeps their things.
                AppLog.Warning(Log, "Skipping the custom callout file {0}", Path.GetFileName(entry.Path));
            }
        }
    }

    public void LoadCustomFile(string path, string lang, string? tone = null)
    {
        if (!_availableLanguages.Contains(lang))
            throw new PhraseFileException(L10n.F("Unknown language: {0}", PyJson.Quote(lang)));
        if (tone is not null && !AvailableTones.Contains(tone))
            throw new PhraseFileException(L10n.F("Unknown tone: {0}", PyJson.Quote(tone)));
        if (_customPhraseFiles.Any(entry => entry.Path == path))
            throw new PhraseFileException(L10n.T("That file is already loaded."));
        var data = ReadCustomFile(path);  // throws before anything changes
        MergeCustomPhrases(lang, tone, data);
        _customPhraseFiles.Add(new CustomPhraseFile(path, lang, tone));
        SaveCustomPhraseFiles();
    }

    public void UnloadCustomFile(string path)
    {
        _customPhraseFiles = [.. _customPhraseFiles.Where(entry => entry.Path != path)];
        LoadAvailableLanguages();  // back to the shipped files alone
        ApplyStoredCustomFiles();  // and whatever custom files remain on top
        SaveCustomPhraseFiles();
    }

    // Forgets every added phrase file, including the phrases already merged in.
    public void ClearCustomPhraseFiles()
    {
        _customPhraseFiles = [];
        LoadAvailableLanguages();
        _dataStore?.Delete(CustomFilesName);
    }

    // Entries a hand-edited or damaged file holds without a path or a language are skipped - Python
    // would stop at the first of them.
    private List<CustomPhraseFile> LoadCustomPhraseFiles()
    {
        var stored = _dataStore!.Load(CustomFilesName);
        if (stored is null) return [];
        if (stored is not JsonArray entries)
        {
            AppLog.Warning(Log, "The custom phrase file list is not a list - ignoring it.");
            return [];
        }
        var files = new List<CustomPhraseFile>();
        foreach (var entry in entries)
        {
            if (entry is JsonObject e && e["path"] is JsonValue p && p.GetValueKind() == JsonValueKind.String
                && e["lang"] is JsonValue l && l.GetValueKind() == JsonValueKind.String)
                files.Add(new CustomPhraseFile((string)p!, (string)l!, e["tone"] is JsonValue t && t.GetValueKind() == JsonValueKind.String ? (string)t! : null));
            else
                AppLog.Warning(Log, "Skipping an unreadable entry in the custom phrase file list.");
        }
        return files;
    }

    private void SaveCustomPhraseFiles() =>
        _dataStore?.Save(CustomFilesName, new JsonArray([.. _customPhraseFiles.Select(f => (JsonNode)new JsonObject
        {
            ["path"] = f.Path,
            ["lang"] = f.Lang,
            ["tone"] = f.Tone,
        })]));

    // --- triggers ---

    // A deliberate deviation from the Python app:
    // there the greeting is rolled against the talking chance like any line, and the session's
    // first rhythm counts as a change of tempo - from 0, or from wherever the last session ended -
    // so a session could open with "faster" instead of a greeting. Here a session always opens with
    // one while callouts are on, and its first rhythm only sets the tempo the next change is held
    // against.
    public void SessionStarted()
    {
        _firstRhythm = true;
        if (!ActiveCallout) return;
        if (PickPhrase("session_started") is { } greeting)
            ShowTease(greeting);
    }

    public void MediaSkipped() => SelectAndOutputSentence("media_skipped");

    public void MediaRepeated() => SelectAndOutputSentence("media_repeated");

    public void PauseEnded() => SelectAndOutputSentence("pause_end");

    public void PauseStarted() => SelectAndOutputSentence("pause_start");

    public void BeatChangeSlower() => SelectAndOutputSentence("beat_change_slower");

    public void BeatChangeFaster() => SelectAndOutputSentence("beat_change_faster");

    public void BeatChangeGeneral(double freq, string? pattern)
    {
        _ = pattern;
        if (_firstRhythm)
        {
            _firstRhythm = false;
            CurFreq = freq;
            return;
        }
        if (_random.NextDouble() < 0.5)
            SelectAndOutputSentence("beat_change_general");
        else if (CurFreq > freq)
            BeatChangeSlower();
        else if (CurFreq < freq)
            BeatChangeFaster();
        else
            SelectAndOutputSentence("beat_change_general");

        CurFreq = freq;
    }

    // The start screen's line: a main_menu phrase of the language and tones picked, or null when
    // there is none. Whether callouts are on does not matter - it is the app's face, not a callout.
    public string? TitleLine() => PickPhrase(TitleKey);

    // One random phrase for the category out of the active tone mix, or null. A tone is drawn
    // first and only then a line from it, so every ticked tone gets an equal voice - pooling the
    // lists instead would let a 250-line tone drown out a 100-line one.
    public string? PickPhrase(string category)
    {
        var tones = CalloutData.TryGetValue(Lang, out var t) ? t : [];
        var pools = new List<List<string>>();
        var poolTones = new List<string>();
        foreach (var tone in UsableTones())
        {
            if (tones.TryGetValue(tone, out var categories) && categories.TryGetValue(category, out var phrases) && phrases.Count > 0)
            {
                pools.Add(phrases);
                poolTones.Add(tone);
            }
        }

        if (pools.Count == 0)
        {
            AppLog.Warning(Log, "No {0} phrases available for language {1}", PyJson.Quote(category), PyJson.Quote(Lang));
            return null;
        }
        // The same two draws as the Python app makes; the tone is only read off the pool.
        var pool = _random.Choice(pools);
        _lastCategory = category;
        _lastTone = poolTones[pools.IndexOf(pool)];
        return _random.Choice(pool);
    }

    // Always right after the PickPhrase that drew it, so the category and tone are this line's.
    private void ShowTease(string tease)
    {
        TeaseShown?.Invoke(tease);
        CalloutSpoken?.Invoke(new Callout(tease, _lastCategory, _lastTone));
        IsTeasing = true;
        _teaseEndsAt = Now + TeaseTime.TotalSeconds;
    }

    public void SelectAndOutputSentence(string category)
    {
        if (!ActiveCallout) return;
        if (IsTeasing) return;
        if (_random.NextDouble() > TalkingChance) return;
        if (PickPhrase(category) is { } tease)
            ShowTease(tease);
    }

    // Says a phrase from the category unconditionally, past the ActiveCallout, TalkingChance and
    // IsTeasing gates - for scripted narrative beats (climax outcome, fake-climax reveal) that must
    // always show, not ambient flavour text.
    public void ForceOutputSentence(string category)
    {
        if (PickPhrase(category) is { } tease)
            ShowTease(tease);
    }

    // Called by the host every frame: ends a tease once its time is up.
    public void Tick()
    {
        if (_teaseEndsAt is not { } endsAt || Now < endsAt) return;
        _teaseEndsAt = null;
        TeaseHidden?.Invoke();
        IsTeasing = false;
    }

    // str.title(): a cased letter after another cased letter is lowered, any other one raised.
    private static string PyTitle(string text)
    {
        var builder = new StringBuilder(text.Length);
        var previousCased = false;
        foreach (var c in text)
        {
            var cased = char.IsUpper(c) || char.IsLower(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.TitlecaseLetter;
            builder.Append(!cased ? c : previousCased ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c));
            previousCased = cased;
        }
        return builder.ToString();
    }
}
