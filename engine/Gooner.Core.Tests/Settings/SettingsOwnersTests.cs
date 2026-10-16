using Gooner.Core.Beat;
using Gooner.Core.Callouts;
using Gooner.Core.Climax;
using Gooner.Core.Settings;

namespace Gooner.Core.Tests.Settings;

// Port of test_every_settings_owner_answers_for_its_own_defaults (tests/test_gooner_app.py).
// Each object that reads a setting says what it is when unset, and says it in exactly one place: a
// key listed by two owners is a value to hold in step, and a write to the wrong one would be
// silently ignored. Python lists the owners by hand; here every ISettingsOwner in Gooner.Core is
// found, so a new one cannot be forgotten. The app, the HUD and the player join in phase 2.
public class SettingsOwnersTests
{
    private static readonly Type[] Owners =
        [.. typeof(ISettingsOwner).Assembly.GetTypes().Where(t => t.IsClass && typeof(ISettingsOwner).IsAssignableFrom(t))];

    private static IReadOnlyDictionary<string, object> DefaultsOf(Type owner) =>
        (IReadOnlyDictionary<string, object>)owner.GetProperty(nameof(ISettingsOwner.Defaults))!.GetValue(null)!;

    private static string GroupOf(Type owner) =>
        (string)owner.GetProperty(nameof(ISettingsOwner.SettingsGroup))!.GetValue(null)!;

    // "max_beat_dur" -> "MaxBeatDur": the property a default belongs to.
    private static string PropertyFor(string key) =>
        string.Concat(key.Split('_').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    [Fact]
    public void the_handlers_that_read_settings_are_owners() =>
        Assert.Subset(Owners.ToHashSet(), new HashSet<Type> { typeof(BeatHandler), typeof(CalloutHandler), typeof(ClimaxHandler) });

    [Fact]
    public void every_settings_owner_answers_for_its_own_defaults()
    {
        var seen = new Dictionary<string, string>();
        foreach (var owner in Owners)
        {
            foreach (var key in DefaultsOf(owner).Keys)
            {
                Assert.False(seen.ContainsKey(key), $"{key} is claimed by {seen.GetValueOrDefault(key)} and {owner.Name}");
                seen[key] = owner.Name;
                Assert.True(owner.GetProperty(PropertyFor(key)) is not null, $"{owner.Name} defaults {key} but never reads it");
            }
        }
    }

    // Explicit rather than derived from the class name: the read sides all hardcode the same
    // literal, so a rename would silently orphan every saved value with nothing to grep for.
    [Fact]
    public void every_owner_names_its_settings_group()
    {
        foreach (var owner in Owners)
            Assert.False(string.IsNullOrWhiteSpace(GroupOf(owner)), owner.Name);
    }
}
