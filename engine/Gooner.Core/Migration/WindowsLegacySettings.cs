using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace Gooner.Core.Migration;

// The Python app's settings where QSettings' native format keeps them on Windows: a key under
// HKEY_CURRENT_USER, a value per setting and a subkey per group - "BeatHandler/min_beat_freq" is the
// value min_beat_freq in the subkey BeatHandler. `subKey` is relative to HKEY_CURRENT_USER.
[SupportedOSPlatform("windows")]
public sealed class WindowsLegacySettings(string subKey) : ILegacySettings
{
    // QSettings("GoonerCock", "GoonerApp").
    public const string PythonAppKey = @"Software\GoonerCock\GoonerApp";

    public IReadOnlyList<LegacySetting> ReadAll()
    {
        using var root = Registry.CurrentUser.OpenSubKey(subKey);
        var found = new List<LegacySetting>();
        if (root is not null) Collect(root, "", found);
        return found;
    }

    public bool Remove(string key)
    {
        var cut = key.LastIndexOf('/');
        var group = cut < 0 ? subKey : subKey + "\\" + key[..cut].Replace('/', '\\');
        try
        {
            using var holder = Registry.CurrentUser.OpenSubKey(group, writable: true);
            holder?.DeleteValue(key[(cut + 1)..], throwOnMissingValue: false);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    public bool RemoveEmptyKeys()
    {
        try
        {
            using (var root = Registry.CurrentUser.OpenSubKey(subKey, writable: true))
            {
                if (root is null || !Prune(root)) return false;
            }
            Registry.CurrentUser.DeleteSubKey(subKey, throwOnMissingSubKey: false);
            // The organisation's key above it, once nothing else is in it - never higher than that.
            var cut = subKey.LastIndexOf('\\');
            if (cut > 0 && subKey.IndexOf('\\', StringComparison.Ordinal) < cut)
            {
                var parent = subKey[..cut];
                bool empty;
                using (var key = Registry.CurrentUser.OpenSubKey(parent)) empty = key is { ValueCount: 0, SubKeyCount: 0 };
                if (empty) Registry.CurrentUser.DeleteSubKey(parent, throwOnMissingSubKey: false);
            }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    private static void Collect(RegistryKey key, string prefix, List<LegacySetting> found)
    {
        foreach (var name in key.GetValueNames())
        {
            if (name.Length == 0) continue;  // the key's default value, which QSettings never writes
            var data = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            found.Add(new LegacySetting(prefix + name, KindOf(key.GetValueKind(name)), data));
        }
        foreach (var name in key.GetSubKeyNames())
        {
            using var group = key.OpenSubKey(name);
            if (group is not null) Collect(group, prefix + name + "/", found);
        }
    }

    // Drops the empty groups under `key`, deepest first; whether `key` is empty afterwards.
    private static bool Prune(RegistryKey key)
    {
        foreach (var name in key.GetSubKeyNames())
        {
            bool empty;
            using (var group = key.OpenSubKey(name, writable: true)) empty = group is null || Prune(group);
            if (empty) key.DeleteSubKey(name, throwOnMissingSubKey: false);
        }
        return key.ValueCount == 0 && key.SubKeyCount == 0;
    }

    private static RegistryKind KindOf(RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.None => RegistryKind.None,
        RegistryValueKind.String => RegistryKind.Sz,
        RegistryValueKind.ExpandString => RegistryKind.ExpandSz,
        RegistryValueKind.MultiString => RegistryKind.MultiSz,
        RegistryValueKind.DWord => RegistryKind.DWord,
        RegistryValueKind.QWord => RegistryKind.QWord,
        RegistryValueKind.Binary => RegistryKind.Binary,
        _ => RegistryKind.Unknown,
    };
}
