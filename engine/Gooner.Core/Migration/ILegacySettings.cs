namespace Gooner.Core.Migration;

// The registry key the Python app's QSettings kept its settings in. Read once at startup, emptied
// value by value as each one arrives in settings.json, and removed once nothing is left in it - a
// portable app has no uninstaller to do that later.
public interface ILegacySettings
{
    // Every value, named the way QSettings names it ("Group/name"); empty when there is no key.
    IReadOnlyList<LegacySetting> ReadAll();

    // Whether the value is gone. A value that cannot be removed now is tried again next start.
    bool Remove(string key);

    // Drops whatever is left empty - groups, then the key itself and the organisation's key above
    // it - and says whether it removed the key: false while values are left, or when there was none.
    bool RemoveEmptyKeys();
}

// One value as the registry holds it: the kind, and the data as .NET reads it (string, string[],
// int, long or byte[]).
public sealed record LegacySetting(string Key, RegistryKind Kind, object? Data);

// The registry's value kinds (REG_SZ, REG_MULTI_SZ, ...), mirrored rather than taken from
// Microsoft.Win32: there they are Windows-only, and the conversion is plain logic that runs - and is
// tested - anywhere. Only the reader itself is Windows'.
public enum RegistryKind
{
    Unknown,
    None,
    Sz,
    ExpandSz,
    MultiSz,
    DWord,
    QWord,
    Binary,
}
