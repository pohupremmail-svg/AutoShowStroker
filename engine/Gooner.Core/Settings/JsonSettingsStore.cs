using System.Text.Json.Nodes;
using Gooner.Core.Data;
using Gooner.Core.Logging;

namespace Gooner.Core.Settings;

// The settings as one settings.json in the data directory, beside the session history. Not the
// registry: only Windows has one, and the Python app's registry key is imported once and then
// removed (phase 4). Every change is written straight through, atomically, by the UserDataStore -
// a settings dialog writes a few dozen small values, and none of them may be lost to a crash.
//
// Takes the data store as a parameter, so tests never touch the real data directory.
public sealed class JsonSettingsStore : ISettingsStore
{
    public const string FileName = "settings";
    private const string Log = "settings";

    private readonly UserDataStore _data;
    private readonly JsonObject _values;

    public JsonSettingsStore(UserDataStore data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
        switch (data.Load(FileName))
        {
            case null:
                _values = [];
                break;
            case JsonObject stored:
                _values = stored.DeepClone().AsObject();
                break;
            default:
                AppLog.Warning(Log, "The settings file did not contain a mapping - using the defaults.");
                _values = [];
                break;
        }
    }

    public IReadOnlyList<string> Keys => [.. _values.Select(kv => kv.Key)];

    public JsonNode? Value(string key) => _values[key];

    public bool Contains(string key) => _values.ContainsKey(key);

    public void SetValue(string key, JsonNode? value)
    {
        _values[key] = value?.Parent is null ? value : value.DeepClone();
        _data.Save(FileName, _values);
    }

    public bool Remove(string key)
    {
        if (!_values.Remove(key)) return false;
        _data.Save(FileName, _values);
        return true;
    }
}
