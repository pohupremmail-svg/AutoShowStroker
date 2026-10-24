using System.Text.Json.Nodes;

namespace Gooner.Core.Settings;

// Where the app's settings live - the seam QSettings was on the Python side. Keys are
// "Group/name", the group being the owning object's SettingsGroup, exactly as the Python app
// writes them; that is what lets the registry import carry them over one to one.
//
// Values come back as stored. The typed reads (SettingsReads) convert them the way the Python
// read sides do.
public interface ISettingsStore
{
    JsonNode? Value(string key);

    bool Contains(string key);

    void SetValue(string key, JsonNode? value);

    // Whether there was anything to remove.
    bool Remove(string key);

    IReadOnlyList<string> Keys { get; }

    // Where the settings are, as Privacy & Data names it (QSettings.fileName()).
    string Location { get; }

    // Every setting gone - Delete my data > All settings.
    void Clear();
}
