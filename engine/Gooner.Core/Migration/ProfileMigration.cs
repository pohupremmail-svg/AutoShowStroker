using System.Security;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gooner.Core.Data;
using Gooner.Core.Settings;

namespace Gooner.Core.Migration;

// Carries an install of the Python app over, at every start until there is nothing left to carry:
// the data files out of the roaming profile (user_data.py migrate_legacy_location), then the
// registry - every setting into settings.json, the four data files that once lived there into
// their files (the lazy load(..., legacy_key) migrations), and the keys of removed features
// dropped (prune_legacy_registry_keys).
//
// The rule is user_data.py's throughout: a value leaves the registry only once its copy has been
// read back from the file, so a full disk or a lock costs nothing - the next start tries again -
// and what cannot be read is left where it is, since it is the only copy. Where the engine already
// has a newer copy (a settings.json value, a data file), the registry's is never going to be read
// and goes, so the key does not outlive the import.
//
// Runs before the stores are built, as a JsonSettingsStore reads its file once; before the log is
// configured, too, so it writes nothing down itself - GoonerApp logs the report.
public static class ProfileMigration
{
    // src/user_data.py LEGACY_DEAD_KEYS: registry keys no code reads any more. TTSHandler/* is left
    // over from the text-to-speech feature that was reverted; GoonerApp/loudness was superseded by
    // GoonerApp/vid_loudness; the two chances were replaced by the session plan.
    public static readonly IReadOnlyList<string> LegacyDeadKeys =
        ["BeatHandler/beat_change_chance", "ClimaxHandler/climax_chance", "TTSHandler", "GoonerApp/loudness"];

    // The data that lived in single registry values before it moved into files, by its file name.
    private static readonly Dictionary<string, string> LegacyData = new(StringComparer.Ordinal)
    {
        ["ScoreTracker/session_history"] = "session_history",
        ["BeatHandler/custom_patterns"] = "custom_patterns",
        ["CalloutHandler/custom_phrase_files"] = "custom_phrase_files",
        ["GoonerApp/last_selected_folders"] = "last_selected_folders",
    };

    public static MigrationReport Run(UserDataStore data, ILegacySettings? registry, string? roamingDir)
    {
        ArgumentNullException.ThrowIfNull(data);
        var report = new MigrationReport { Roaming = data.MigrateLegacyLocation(roamingDir) };
        if (registry is not null) Import(data, registry, report);
        return report;
    }

    private static void Import(UserDataStore data, ILegacySettings registry, MigrationReport report)
    {
        IReadOnlyList<LegacySetting> entries;
        try
        {
            entries = registry.ReadAll();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            report.RegistryError = e.Message;
            return;
        }

        // What JsonSettingsStore would read: an unreadable file is set aside, one that is not a
        // mapping is replaced - the store would start from nothing in both cases too.
        var settings = data.Load(JsonSettingsStore.FileName) as JsonObject ?? [];
        var leaving = new List<string>();
        var written = new List<string>();
        foreach (var entry in entries)
        {
            if (IsDead(entry.Key))
                leaving.Add(entry.Key);
            else if (LegacyData.TryGetValue(entry.Key, out var name))
                MoveData(data, entry, name, report, leaving);
            else if (!QSettingsRegistry.TryRead(entry.Kind, entry.Data, out var value))
                report.KeptKeys.Add(entry.Key);
            else if (value is null || settings.ContainsKey(entry.Key))
                leaving.Add(entry.Key);  // holds nothing, or settings.json has the newer copy
            else
            {
                settings[entry.Key] = value;
                written.Add(entry.Key);
            }
        }

        if (written.Count > 0)
        {
            var readBack = data.Save(JsonSettingsStore.FileName, settings) ? data.Load(JsonSettingsStore.FileName) as JsonObject : null;
            report.SettingsWriteFailed = readBack is null;
            foreach (var key in written)
            {
                if (readBack is not null && JsonNode.DeepEquals(readBack[key], settings[key]))
                {
                    leaving.Add(key);
                    report.Imported.Add(key);
                }
                else report.KeptKeys.Add(key);
            }
        }

        foreach (var key in leaving)
            if (!registry.Remove(key)) report.KeptKeys.Add(key);
        report.RegistryKeyRemoved = registry.RemoveEmptyKeys();
    }

    // QSettings.remove("TTSHandler") takes the whole group.
    private static bool IsDead(string key) =>
        LegacyDeadKeys.Any(dead => key == dead || key.StartsWith(dead + "/", StringComparison.Ordinal));

    // user_data.py _migrate_from_settings, for one data file.
    private static void MoveData(UserDataStore data, LegacySetting entry, string name, MigrationReport report, List<string> leaving)
    {
        // The file wins once it exists - read, not merely present: an unreadable one is set aside
        // by the read, and then the registry copy is the only one left.
        if (data.Load(name) is not null)
        {
            leaving.Add(entry.Key);
            return;
        }
        if (!QSettingsRegistry.TryRead(entry.Kind, entry.Data, out var raw))
        {
            report.KeptKeys.Add(entry.Key);
            return;
        }
        // Python's `if not raw`: nothing to carry.
        if (raw is null or JsonArray { Count: 0 } || raw is JsonValue empty && empty.GetValueKind() == JsonValueKind.String && ((string)empty!).Length == 0)
        {
            leaving.Add(entry.Key);
            return;
        }
        // The data was always written as JSON text; anything else is not something to guess at.
        if (raw is not JsonValue text || text.GetValueKind() != JsonValueKind.String)
        {
            report.KeptKeys.Add(entry.Key);
            return;
        }

        JsonNode? payload;
        try
        {
            payload = JsonNode.Parse((string)text!);
        }
        catch (JsonException)
        {
            // Left in place - it is the only copy, and deleting something we merely failed to read
            // would destroy it for good.
            report.KeptKeys.Add(entry.Key);
            return;
        }
        // Written and read back, or the registry copy stays the fallback.
        if (data.Save(name, payload) && JsonNode.DeepEquals(data.Load(name), payload))
        {
            leaving.Add(entry.Key);
            report.Moved.Add(name);
        }
        else report.KeptKeys.Add(entry.Key);
    }
}
