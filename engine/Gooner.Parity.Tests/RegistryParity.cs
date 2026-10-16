using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gooner.Core.Data;
using Gooner.Core.Migration;
using Gooner.Core.Settings;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Gooner.Parity.Tests;

// The settings import against the real thing. The generator had a real PyQt6 QSettings write the
// values the app writes into a throwaway registry key and dumped them byte for byte; here those
// exact bytes go into a throwaway key of this test's own, the engine's reader reads them, the import
// converts them, and the result is held against what QSettings read back from the same bytes - the
// value, and what the Python read sides make of it.
[SupportedOSPlatform("windows")]
public sealed class RegistryParity : IDisposable
{
    private const string Module = "registry";
    private const string Section = Module + ".values";
    private const double Fallback = -999;
    private readonly string _parent = $@"Software\Gooner.Parity.Tests-{Guid.NewGuid():N}";
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-parity-registry-");

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_parent, throwOnMissingSubKey: false);
        _tmp.Delete(recursive: true);
    }

    public static IEnumerable<object[]> Values() => Golden.Cases(Module, "values");

    [Theory]
    [MemberData(nameof(Values))]
    public void value(string name, string json)
    {
        var c = JsonNode.Parse(json)!;
        var key = (string)c["key"]!;
        var path = _parent + @"\GoonerApp";
        WriteRaw(path, key, (string)c["kind"]!, Convert.FromBase64String((string)c["bytes"]!));

        var read = Assert.Single(new WindowsLegacySettings(path).ReadAll());
        Assert.Equal(key, read.Key);
        if (!QSettingsRegistry.TryRead(read.Kind, read.Data, out var value))
        {
            // A Qt type in a QDataStream blob - QSettings' way of keeping a list with an empty line.
            Assert.NotNull(Golden.Allowed(Section, "qt_serialised"));
            Assert.Equal("Binary", (string)c["kind"]!);
            return;
        }
        Assert.True(Golden.Equivalent(c["value"], value), $"{name}: QSettings read {c["value"]?.ToJsonString() ?? "None"}, the import {value?.ToJsonString() ?? "null"}");

        // What the import does not carry - an @Invalid() - is not read back either.
        if (value is null) return;
        var store = new JsonSettingsStore(new UserDataStore(_tmp.FullName));
        store.SetValue("k", value);
        if (c["reads"] is { } reads)
        {
            Number(name, reads["double"], store.GetDouble("k", Fallback));
            Integer(name, reads["int"], store);
            Assert.Equal((bool)reads["bool_default_false"]!, store.GetBool("k", false));
            Assert.Equal((bool)reads["bool_default_true"]!, store.GetBool("k", true));
            Assert.Equal((string)reads["string"]!, store.GetString("k", ""));
        }
        else
            Assert.Equal(c["value"]!.AsArray().Select(v => (string)v!), store.GetStringList("k")!);
    }

    [Fact]
    public void the_dead_keys_are_the_python_apps() =>
        Assert.Equal(Golden.Load(Module)["dead_keys"]!.AsArray().Select(k => (string)k!), ProfileMigration.LegacyDeadKeys);

    // Each allowlisted rule has to keep applying to something, or it is stale.
    [Theory]
    [InlineData("qt_serialised")]
    [InlineData("unreadable_numbers")]
    [InlineData("beyond_int32")]
    public void every_allowlisted_rule_still_has_cases(string rule)
    {
        Assert.NotNull(Golden.Allowed(Section, rule));
        var cases = Golden.Load(Module)["values"]!.AsArray().Select(c => c!).ToList();
        var applies = rule switch
        {
            "qt_serialised" => cases.Exists(c => (string)c["kind"]! == "Binary" && c["value"] is JsonArray),
            "unreadable_numbers" => cases.Exists(c => c["reads"]?["double"] is JsonValue v && v.GetValueKind() == JsonValueKind.String),
            _ => cases.Exists(c => c["reads"]?["int"] is JsonValue v && v.GetValueKind() == JsonValueKind.Number
                && Math.Abs(v.GetValue<double>()) > int.MaxValue),
        };
        Assert.True(applies, $"'{rule}' is allowlisted but no case needs it any more.");
    }

    private static void Number(string name, JsonNode? expected, double actual)
    {
        if (expected is JsonValue v && v.GetValueKind() == JsonValueKind.String)
        {
            // Python raised; the port falls back on purpose.
            Assert.NotNull(Golden.Allowed(Section, "unreadable_numbers"));
            Assert.True(actual == Fallback, $"{name}: Python raised, the port read {actual} instead of the default");
            return;
        }
        Assert.True(expected!.GetValue<double>() == actual, $"{name}: expected {expected}, actual {actual}");
    }

    private static void Integer(string name, JsonNode? expected, JsonSettingsStore store)
    {
        var actual = store.GetInt("k", (int)Fallback);
        if (expected is JsonValue v && v.GetValueKind() == JsonValueKind.Number && Math.Abs(v.GetValue<double>()) > int.MaxValue)
        {
            Assert.NotNull(Golden.Allowed(Section, "beyond_int32"));
            Assert.True(actual == (int)Fallback, $"{name}: beyond 32 bits, the port read {actual} instead of the default");
            return;
        }
        Number(name, expected, actual);
    }

    // The bytes as they were, under the kind they were - which RegistryKey.SetValue cannot do: it
    // writes its own encoding of a typed value, and the point is to read Qt's.
    private static void WriteRaw(string path, string key, string kind, byte[] data)
    {
        var cut = key.LastIndexOf('/');
        using var holder = Registry.CurrentUser.CreateSubKey(cut < 0 ? path : path + "\\" + key[..cut].Replace('/', '\\'));
        var status = NativeMethods.RegSetValueEx(holder.Handle, key[(cut + 1)..], 0, KindCode(kind), data, data.Length);
        Assert.Equal(0, status);
    }

    private static int KindCode(string kind) => kind switch
    {
        "None" => 0,
        "Sz" => 1,
        "ExpandSz" => 2,
        "Binary" => 3,
        "DWord" => 4,
        "MultiSz" => 7,
        "QWord" => 11,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "a kind the generator does not write"),
    };

    private static class NativeMethods
    {
        [DllImport("advapi32.dll", EntryPoint = "RegSetValueExW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int RegSetValueEx(SafeRegistryHandle key, string name, int reserved, int type, byte[] data, int size);
    }
}
