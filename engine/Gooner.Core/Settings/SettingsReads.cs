using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gooner.Core.Logging;
using Gooner.Core.Sessions;

namespace Gooner.Core.Settings;

// Typed reads over a settings store, converting the way the Python read sides do: float(value),
// int(float(value)) and QSettings' type=bool. QSettings hands most values back as text, so
// that is what the registry import carries over, and those values have to read as what they were.
//
// One deliberate difference: a value that cannot be read falls back to the default with a warning,
// where Python raises out of the constructor and the app does not start. The value stays in the
// file for a human to look at - and the warning names the key only, never the value, because a
// setting can be a media folder path.
public static class SettingsReads
{
    private const string Log = "settings";

    public static double GetDouble(this ISettingsStore store, string key, double fallback) =>
        TryDouble(Read(store, key), out var value) ? value : Unreadable(store, key, fallback);

    public static int GetInt(this ISettingsStore store, string key, int fallback)
    {
        // int(float(x)): cut towards zero, and only what is a finite number in range.
        if (TryDouble(Read(store, key), out var value) && double.IsFinite(value)
            && Math.Truncate(value) is var whole && whole is >= int.MinValue and <= int.MaxValue)
            return (int)whole;
        return Unreadable(store, key, fallback);
    }

    public static bool GetBool(this ISettingsStore store, string key, bool fallback)
    {
        var node = Read(store, key);
        if (node is null) return fallback;
        if (node is not JsonValue value) return Unreadable(store, key, fallback);
        switch (value.GetValueKind())
        {
            case JsonValueKind.True: return true;
            case JsonValueKind.False: return false;
            case JsonValueKind.Number: return PyJson.Number(value) != 0;
            case JsonValueKind.String:
                // Qt reads text as false only when it is empty, "0" or "false" in any case.
                var text = (string)value!;
                return !(text.Length == 0 || text == "0" || text.Equals("false", StringComparison.OrdinalIgnoreCase));
            default: return Unreadable(store, key, fallback);
        }
    }

    public static string GetString(this ISettingsStore store, string key, string fallback) =>
        Read(store, key) switch
        {
            null => fallback,
            JsonValue value when value.GetValueKind() == JsonValueKind.String => (string)value!,
            JsonValue value => value.ToJsonString(),
            _ => Unreadable(store, key, fallback),
        };

    // The names stored, or null when nothing is. A bare string is a list of one: QSettings writes a
    // one-element list to an ini file that way, so an import can hand one over like that.
    public static IReadOnlyList<string>? GetStringList(this ISettingsStore store, string key) =>
        Read(store, key) switch
        {
            null => null,
            JsonArray items => [.. items.Select(item => item is JsonValue v && v.GetValueKind() == JsonValueKind.String ? (string)v! : item?.ToJsonString() ?? "")],
            JsonValue value when value.GetValueKind() == JsonValueKind.String => [(string)value!],
            _ => Unreadable<IReadOnlyList<string>?>(store, key, null),
        };

    private static JsonNode? Read(ISettingsStore store, string key)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store.Value(key);
    }

    // float(x) on what QSettings hands back: a number, a bool (float(True) is 1.0), or text that
    // spells a number - whitespace around it allowed, as Python allows it.
    private static bool TryDouble(JsonNode? node, out double value)
    {
        value = 0;
        if (node is not JsonValue v) return false;
        switch (v.GetValueKind())
        {
            case JsonValueKind.Number:
                value = PyJson.Number(v);
                return true;
            case JsonValueKind.True:
                value = 1;
                return true;
            case JsonValueKind.False:
                return true;
            case JsonValueKind.String:
                return double.TryParse((string)v!, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            default:
                return false;
        }
    }

    private static T Unreadable<T>(ISettingsStore store, string key, T fallback)
    {
        if (store.Contains(key))
            AppLog.Warning(Log, "The setting {0} could not be read - using the default", key);
        return fallback;
    }
}
