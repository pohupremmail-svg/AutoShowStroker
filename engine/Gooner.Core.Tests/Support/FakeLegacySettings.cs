using System.Globalization;
using Gooner.Core.Migration;

namespace Gooner.Core.Tests.Support;

// The Python app's registry key, in memory: values stored the way QSettings' native format stores
// what Python hands it - text, a float and a bool as REG_SZ ("0.25", "true"), an int as REG_DWORD,
// a list of strings as REG_MULTI_SZ.
internal sealed class FakeLegacySettings : ILegacySettings
{
    private readonly List<LegacySetting> _values = [];

    public List<string> Removed { get; } = [];

    // Whether RemoveEmptyKeys found nothing left and took the key away.
    public bool KeyRemoved { get; private set; }

    // Thrown by ReadAll, as a registry the user may not read would.
    public Exception? ReadFails { get; set; }

    public FakeLegacySettings Set(string key, string text) => Raw(key, RegistryKind.Sz, text);

    public FakeLegacySettings Set(string key, bool value) => Set(key, value ? "true" : "false");

    public FakeLegacySettings Set(string key, double value) => Set(key, value.ToString("R", CultureInfo.InvariantCulture));

    public FakeLegacySettings SetInt(string key, int value) => Raw(key, RegistryKind.DWord, value);

    public FakeLegacySettings SetList(string key, params string[] items) => Raw(key, RegistryKind.MultiSz, items);

    public FakeLegacySettings Raw(string key, RegistryKind kind, object? data)
    {
        _values.RemoveAll(v => v.Key == key);
        _values.Add(new LegacySetting(key, kind, data));
        return this;
    }

    public bool Contains(string key) => _values.Exists(v => v.Key == key);

    public IReadOnlyList<string> Keys => [.. _values.Select(v => v.Key)];

    public IReadOnlyList<LegacySetting> ReadAll()
    {
        if (ReadFails is not null) throw ReadFails;
        return [.. _values];
    }

    public bool Remove(string key)
    {
        _values.RemoveAll(v => v.Key == key);
        Removed.Add(key);
        return true;
    }

    public bool RemoveEmptyKeys()
    {
        KeyRemoved = _values.Count == 0;
        return KeyRemoved;
    }
}
