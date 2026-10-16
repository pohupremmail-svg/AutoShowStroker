using Gooner.Core.Data;
using Gooner.Core.Logging;

namespace Gooner.Core.Migration;

// What ProfileMigration did, held until the log is configured. Names keys and data files, never a
// value: a setting can be a media folder path.
public sealed class MigrationReport
{
    private const string Log = "user_data";

    public RoamingMove Roaming { get; init; }

    // The settings that arrived in settings.json, in the registry's order.
    public IReadOnlyList<string> ImportedSettings => Imported;

    // The data files written from a registry value.
    public IReadOnlyList<string> MovedData => Moved;

    // What stays in the registry: values that cannot be read, or whose copy did not arrive.
    public IReadOnlyList<string> Kept => KeptKeys;

    public bool SettingsWriteFailed { get; internal set; }

    // Whether the Python app's key is gone - nothing left in it.
    public bool RegistryKeyRemoved { get; internal set; }

    // Why the registry could not be read at all, if it could not.
    public string? RegistryError { get; internal set; }

    internal List<string> Imported { get; } = [];

    internal List<string> Moved { get; } = [];

    internal List<string> KeptKeys { get; } = [];

    public void WriteToLog()
    {
        if (Roaming.Moved > 0)
            AppLog.Info(Log, "Moved {0} data file(s) out of {1}", Roaming.Moved, Roaming.From);
        if (Roaming.FailedFile is not null)
            AppLog.Warning(Log, "Could not move {0} out of the roaming profile: {1}", Roaming.FailedFile, Roaming.Error);
        if (RegistryError is not null)
            AppLog.Warning(Log, "Could not read the settings in the registry: {0}", RegistryError);
        if (Imported.Count > 0)
            AppLog.Info(Log, "Imported {0} setting(s) from the registry", Imported.Count);
        foreach (var name in Moved)
            AppLog.Info(Log, "Moved {0} out of the registry", name);
        if (SettingsWriteFailed)
            AppLog.Error(Log, "Could not write the settings imported from the registry - they stay there until the next start");
        foreach (var key in KeptKeys.Distinct())
            AppLog.Warning(Log, "Left {0} in the registry: it could not be carried over", key);
        if (RegistryKeyRemoved)
            AppLog.Info(Log, "Removed the registry key the settings were kept in");
    }
}
