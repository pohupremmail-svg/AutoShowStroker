namespace Gooner.Core.Settings;

// An object that reads settings: it names the group its keys are stored under ("Group/name"), and
// what each of them is when unset - in exactly one place, which "Reset to defaults" reads too. No
// key is claimed by two owners (SettingsOwnersTests holds every owner to that).
public interface ISettingsOwner
{
    static abstract string SettingsGroup { get; }

    static abstract IReadOnlyDictionary<string, object> Defaults { get; }
}
