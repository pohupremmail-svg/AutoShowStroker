using System.Runtime.Versioning;
using Gooner.Core.Migration;
using Gooner.Core.Tests.Support;
using Microsoft.Win32;

namespace Gooner.Core.Tests.Migration;

// The reader against a real registry - a throwaway key of its own under HKEY_CURRENT_USER, laid out
// the way QSettings lays the Python app's out, and gone after every test. Never the app's key.
[SupportedOSPlatform("windows")]
public sealed class WindowsLegacySettingsTests : IDisposable
{
    private readonly string _parent = $@"Software\Gooner.Core.Tests-{Guid.NewGuid():N}";
    private readonly string _path;

    public WindowsLegacySettingsTests() => _path = _parent + @"\GoonerApp";

    public void Dispose()
    {
        if (OperatingSystem.IsWindows()) Registry.CurrentUser.DeleteSubKeyTree(_parent, throwOnMissingSubKey: false);
    }

    private static readonly string[] Tones = ["flirty", "teasing"];

    private WindowsLegacySettings Reader() => new(_path);

    private void Write(string group, string name, object data, RegistryValueKind kind)
    {
        using var key = Registry.CurrentUser.CreateSubKey(group.Length == 0 ? _path : _path + "\\" + group);
        key.SetValue(name, data, kind);
    }

    private static bool Exists(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key is not null;
    }

    [WindowsFact]
    public void the_python_apps_key_is_where_qsettings_puts_it() =>
        // QSettings("GoonerCock", "GoonerApp") in its native format.
        Assert.Equal(@"Software\GoonerCock\GoonerApp", WindowsLegacySettings.PythonAppKey);

    [WindowsFact]
    public void groups_read_the_way_qsettings_names_them()
    {
        Write("", "top", "a", RegistryValueKind.String);
        Write("BeatHandler", "min_beat_freq", "2", RegistryValueKind.String);
        Write(@"Outer\Inner", "deep", "b", RegistryValueKind.String);

        Assert.Equal(["top", "BeatHandler/min_beat_freq", "Outer/Inner/deep"], Reader().ReadAll().Select(s => s.Key));
    }

    [WindowsFact]
    public void each_kind_reads_as_the_conversion_expects_it()
    {
        Write("G", "sz", "0.25", RegistryValueKind.String);
        Write("G", "expand", "%TEMP%", RegistryValueKind.ExpandString);
        Write("G", "multi", Tones, RegistryValueKind.MultiString);
        Write("G", "dword", -3, RegistryValueKind.DWord);
        Write("G", "qword", 1L << 40, RegistryValueKind.QWord);
        Write("G", "binary", new byte[] { 0x61, 0, 0, 0, 0x62, 0 }, RegistryValueKind.Binary);

        var read = Reader().ReadAll().ToDictionary(s => s.Key);

        Assert.Equal(new LegacySetting("G/sz", RegistryKind.Sz, "0.25"), read["G/sz"]);
        // Never expanded: QSettings hands back what it wrote.
        Assert.Equal("%TEMP%", read["G/expand"].Data);
        Assert.Equal(RegistryKind.ExpandSz, read["G/expand"].Kind);
        Assert.Equal(RegistryKind.MultiSz, read["G/multi"].Kind);
        Assert.Equal(["flirty", "teasing"], (string[])read["G/multi"].Data!);
        Assert.Equal(new LegacySetting("G/dword", RegistryKind.DWord, -3), read["G/dword"]);
        Assert.Equal(new LegacySetting("G/qword", RegistryKind.QWord, 1L << 40), read["G/qword"]);
        Assert.Equal(RegistryKind.Binary, read["G/binary"].Kind);
        Assert.Equal([0x61, 0, 0, 0, 0x62, 0], (byte[])read["G/binary"].Data!);
    }

    [WindowsFact]
    public void the_default_value_is_not_a_setting()
    {
        Write("", "", "default", RegistryValueKind.String);
        Write("", "real", "x", RegistryValueKind.String);

        Assert.Equal(["real"], Reader().ReadAll().Select(s => s.Key));
    }

    [WindowsFact]
    public void a_missing_key_reads_as_nothing()
    {
        Assert.Empty(Reader().ReadAll());
        Assert.False(Reader().RemoveEmptyKeys());
    }

    [WindowsFact]
    public void remove_takes_one_value()
    {
        Write("BeatHandler", "a", "1", RegistryValueKind.String);
        Write("BeatHandler", "b", "2", RegistryValueKind.String);
        Write("", "top", "3", RegistryValueKind.String);

        Assert.True(Reader().Remove("BeatHandler/a"));
        Assert.True(Reader().Remove("top"));
        Assert.True(Reader().Remove("BeatHandler/missing"));

        Assert.Equal(["BeatHandler/b"], Reader().ReadAll().Select(s => s.Key));
    }

    [WindowsFact]
    public void a_key_with_values_left_stays()
    {
        Write("BeatHandler", "a", "1", RegistryValueKind.String);
        using (Registry.CurrentUser.CreateSubKey(_path + @"\ScoreTracker")) { }

        Assert.False(Reader().RemoveEmptyKeys());

        Assert.True(Exists(_path + @"\BeatHandler"));
        // An empty group goes regardless - what QSettings leaves behind once its last value is removed.
        Assert.False(Exists(_path + @"\ScoreTracker"));
    }

    [WindowsFact]
    public void an_empty_key_goes_with_its_groups_and_its_parent()
    {
        Write("BeatHandler", "a", "1", RegistryValueKind.String);
        using (Registry.CurrentUser.CreateSubKey(_path + @"\ScoreTracker\Deeper")) { }
        Reader().Remove("BeatHandler/a");

        Assert.True(Reader().RemoveEmptyKeys());

        Assert.False(Exists(_path));
        Assert.False(Exists(_parent));
    }

    [WindowsFact]
    public void a_parent_holding_anything_else_stays()
    {
        using (Registry.CurrentUser.CreateSubKey(_path)) { }
        using (Registry.CurrentUser.CreateSubKey(_parent + @"\AnotherApp")) { }

        Assert.True(Reader().RemoveEmptyKeys());

        Assert.False(Exists(_path));
        Assert.True(Exists(_parent + @"\AnotherApp"));
    }
}
