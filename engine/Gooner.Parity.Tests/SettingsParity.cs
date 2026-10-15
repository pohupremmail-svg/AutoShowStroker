using System.Text.Json.Nodes;
using Gooner.Core.Data;
using Gooner.Core.Settings;

namespace Gooner.Parity.Tests;

// What the Python read sides make of text out of QSettings - float(), int(float()), type=bool -
// against SettingsReads on the same text. That text is what the registry import carries over.
public class SettingsParity
{
    private const string Module = "settings";
    private const string Unreadable = "unreadable_numbers";
    private const double Fallback = -999;

    public static IEnumerable<object[]> Reads() => Golden.Cases(Module, "read");

    [Theory]
    [MemberData(nameof(Reads))]
    public void read(string name, string json)
    {
        var c = JsonNode.Parse(json)!;
        var expected = c["expected"]!;
        var dir = Directory.CreateTempSubdirectory("gooner-parity-settings-");
        try
        {
            var store = new JsonSettingsStore(new UserDataStore(dir.FullName));
            store.SetValue("k", (string)c["text"]!);

            Number(name, expected["double"], store.GetDouble("k", Fallback));
            Number(name, expected["int"], store.GetInt("k", (int)Fallback));
            Assert.Equal((bool)expected["bool_default_false"]!, store.GetBool("k", false));
            Assert.Equal((bool)expected["bool_default_true"]!, store.GetBool("k", true));
            Assert.Equal((string)expected["string"]!, store.GetString("k", ""));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    // The allowlisted rule has to keep applying to something, or it is stale.
    [Fact]
    public void the_unreadable_rule_still_has_cases()
    {
        var errors = Golden.Load(Module)["read"]!.AsArray()
            .Count(c => c!["expected"]!["double"] is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.String);
        Assert.NotNull(Golden.Allowed(Module + ".read", Unreadable));
        Assert.True(errors > 0, $"'{Unreadable}' is allowlisted but no case raises any more.");
    }

    private static void Number(string name, JsonNode? expected, double actual)
    {
        if (expected is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.String)
        {
            // Python raised. The port falls back on purpose - see the allowlist.
            Assert.NotNull(Golden.Allowed(Module + ".read", Unreadable));
            Assert.True(actual == Fallback, $"{name}: Python raised, the port read {actual} instead of the default");
            return;
        }
        Assert.True(expected!.GetValue<double>() == actual, $"{name}: expected {expected}, actual {actual}");
    }
}
