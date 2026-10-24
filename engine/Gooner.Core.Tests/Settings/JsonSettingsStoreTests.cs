using System.Text.Json.Nodes;
using Gooner.Core.Data;
using Gooner.Core.Settings;

namespace Gooner.Core.Tests.Settings;

// What replaces QSettings: one settings.json in the data directory, keys as "Group/name" exactly
// as the Python app writes them, and reads that convert the way the Python read sides do - so a
// value carried over from the registry (always text there) reads as what it was.
public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-settings-");

    public void Dispose() => _tmp.Delete(recursive: true);

    private UserDataStore Data() => new(Path.Combine(_tmp.FullName, "appdata"));

    private JsonSettingsStore Store() => new(Data());

    // --- Privacy & Data ---

    // What QSettings.fileName() named on the Python side: where the settings are, to be shown.
    [Fact]
    public void the_store_says_where_its_file_is() =>
        Assert.Equal(Path.Combine(_tmp.FullName, "appdata", "settings.json"), Store().Location);

    // "All settings" under Delete my data: every key gone, and the file with them.
    [Fact]
    public void clearing_removes_every_key_and_the_file()
    {
        var store = Store();
        store.SetValue("GoonerApp/min_dur", 2.5);
        store.SetValue("BeatHandler/max_beat_dur", 50.0);

        store.Clear();

        Assert.Empty(store.Keys);
        Assert.False(File.Exists(store.Location));
        Assert.Empty(Store().Keys);
    }

    [Fact]
    public void a_setting_written_after_clearing_is_kept()
    {
        var store = Store();
        store.SetValue("GoonerApp/min_dur", 2.5);
        store.Clear();

        store.SetValue("GoonerApp/max_dur", 9.0);

        Assert.Equal(["GoonerApp/max_dur"], Store().Keys);
    }

    // --- storing ---

    [Fact]
    public void an_unset_key_reads_as_the_fallback()
    {
        var store = Store();

        Assert.Equal(45.0, store.GetDouble("BeatHandler/max_beat_dur", 45.0));
        Assert.Equal(15, store.GetInt("BeatHandler/min_beat_dur", 15));
        Assert.True(store.GetBool("BeatHandler/ramping_active", true));
        Assert.Equal("en", store.GetString("CalloutHandler/lang", "en"));
        Assert.Null(store.GetStringList("BeatHandler/selected_beat_patterns"));
        Assert.False(store.Contains("BeatHandler/max_beat_dur"));
    }

    [Fact]
    public void a_value_reads_back_as_it_was_set()
    {
        var store = Store();
        store.SetValue("BeatHandler/max_beat_dur", 50.5);
        store.SetValue("BeatHandler/min_beat_dur", 20);
        store.SetValue("BeatHandler/ramping_active", false);
        store.SetValue("CalloutHandler/lang", "de");
        store.SetValue("BeatHandler/selected_beat_patterns", new JsonArray("Quick Swing", "Build Up"));

        Assert.Equal(50.5, store.GetDouble("BeatHandler/max_beat_dur", 45.0));
        Assert.Equal(20, store.GetInt("BeatHandler/min_beat_dur", 15));
        Assert.False(store.GetBool("BeatHandler/ramping_active", true));
        Assert.Equal("de", store.GetString("CalloutHandler/lang", "en"));
        Assert.Equal(["Quick Swing", "Build Up"], store.GetStringList("BeatHandler/selected_beat_patterns")!);
    }

    [Fact]
    public void settings_survive_a_restart()
    {
        Store().SetValue("BeatHandler/max_beat_dur", 50.5);

        Assert.Equal(50.5, Store().GetDouble("BeatHandler/max_beat_dur", 45.0));
    }

    // In the data directory beside the session history - never the roaming profile, and never
    // the registry, which only Windows has.
    [Fact]
    public void settings_live_in_one_file_in_the_data_directory()
    {
        var data = Data();
        new JsonSettingsStore(data).SetValue("CalloutHandler/lang", "de");

        var file = JsonNode.Parse(File.ReadAllText(data.PathFor(JsonSettingsStore.FileName)))!.AsObject();
        Assert.Equal("de", (string?)file["CalloutHandler/lang"]);
    }

    [Fact]
    public void a_removed_setting_falls_back_again_and_stays_removed()
    {
        Store().SetValue("CalloutHandler/lang", "de");

        var store = Store();
        Assert.True(store.Remove("CalloutHandler/lang"));

        Assert.Equal("en", store.GetString("CalloutHandler/lang", "en"));
        Assert.False(Store().Contains("CalloutHandler/lang"));
    }

    [Fact]
    public void removing_a_setting_that_was_never_set_says_so() =>
        Assert.False(Store().Remove("CalloutHandler/lang"));

    // A broken settings file must never stop the app from starting: it is set aside, and every
    // setting is back at its default.
    [Fact]
    public void a_corrupt_settings_file_means_defaults()
    {
        var data = Data();
        Directory.CreateDirectory(data.BaseDir);
        File.WriteAllText(data.PathFor(JsonSettingsStore.FileName), "{not json");

        var store = new JsonSettingsStore(data);

        Assert.Equal(45.0, store.GetDouble("BeatHandler/max_beat_dur", 45.0));
        Assert.True(File.Exists(Path.ChangeExtension(data.PathFor(JsonSettingsStore.FileName), ".json.corrupt")));
    }

    [Fact]
    public void a_settings_file_that_is_not_an_object_means_defaults()
    {
        var data = Data();
        data.Save(JsonSettingsStore.FileName, new JsonArray(1, 2));

        Assert.Equal(45.0, new JsonSettingsStore(data).GetDouble("BeatHandler/max_beat_dur", 45.0));
    }

    // --- reading what the registry carried over ---

    // QSettings hands most values back as text, and the Python read sides convert them with
    // float(), int(float()) and type=bool. The registry import passes them on as they are.
    [Theory]
    [InlineData("45", 45.0)]
    [InlineData("45.5", 45.5)]
    [InlineData(" 7 ", 7.0)]
    [InlineData("1e3", 1000.0)]
    [InlineData("-2.5", -2.5)]
    public void a_number_stored_as_text_reads_as_the_number(string stored, double expected)
    {
        var store = Store();
        store.SetValue("k", stored);
        Assert.Equal(expected, store.GetDouble("k", 0.0));
    }

    // int(float(x)) cuts towards zero rather than rounding.
    [Theory]
    [InlineData("5.7", 5)]
    [InlineData("-5.7", -5)]
    [InlineData("20", 20)]
    public void a_whole_number_setting_cuts_off_the_fraction(string stored, int expected)
    {
        var store = Store();
        store.SetValue("k", stored);
        Assert.Equal(expected, store.GetInt("k", 0));
    }

    [Fact]
    public void a_whole_number_stored_as_a_fraction_cuts_it_off_too()
    {
        var store = Store();
        store.SetValue("k", 5.7);
        Assert.Equal(5, store.GetInt("k", 0));
    }

    // Qt's bool conversion of text: false only for "", "0" and "false" in any case.
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("0", false)]
    [InlineData("1", true)]
    [InlineData("", false)]
    [InlineData("no", true)]
    public void a_switch_stored_as_text_reads_the_way_qt_reads_it(string stored, bool expected)
    {
        var store = Store();
        store.SetValue("k", stored);
        Assert.Equal(expected, store.GetBool("k", !expected));
    }

    [Fact]
    public void a_switch_stored_as_a_number_is_on_unless_it_is_zero()
    {
        var store = Store();
        store.SetValue("on", 2);
        store.SetValue("off", 0);
        Assert.True(store.GetBool("on", false));
        Assert.False(store.GetBool("off", true));
    }

    // Python would raise out of the constructor and the app would not start. One unreadable value
    // is not worth that: it reads as the default, and is left in the file for a human to look at.
    [Fact]
    public void an_unreadable_number_falls_back_and_is_left_alone()
    {
        var data = Data();
        var store = new JsonSettingsStore(data);
        store.SetValue("BeatHandler/max_beat_dur", "lots");

        Assert.Equal(45.0, store.GetDouble("BeatHandler/max_beat_dur", 45.0));
        Assert.Equal(15, store.GetInt("BeatHandler/max_beat_dur", 15));
        Assert.Equal("lots", (string?)new JsonSettingsStore(data).Value("BeatHandler/max_beat_dur"));
    }

    [Fact]
    public void a_list_setting_with_a_single_name_reads_as_a_list_of_one()
    {
        // QSettings writes a one-element list to an ini file as a bare string, so an import can
        // hand one over that way.
        var store = Store();
        store.SetValue("BeatHandler/selected_beat_patterns", "Quick Swing");

        Assert.Equal(["Quick Swing"], store.GetStringList("BeatHandler/selected_beat_patterns")!);
    }

    [Fact]
    public void text_stored_as_something_else_reads_as_its_text()
    {
        var store = Store();
        store.SetValue("k", 3);
        Assert.Equal("3", store.GetString("k", ""));
    }
}
