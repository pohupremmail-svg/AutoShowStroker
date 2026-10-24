using Gooner.Core.App;
using Gooner.Core.Data;
using Gooner.Core.Devices;
using Gooner.Core.Panels;
using Gooner.Core.Settings;
using Gooner.Core.Tests.Support;
using Gooner.Core.Timing;

namespace Gooner.Core.Tests.Devices;

// Port of tests/test_plugin_intiface_plugin.py and tests/test_plugin_intiface_ui.py: when the rhythm
// is allowed to reach the device at all, the device's button in the controls row, and the Device tab
// of the settings. No sockets - the controller is only told what to believe about itself
// (Pretend); what is tested is the decision, not the delivery. Where a test does switch output on,
// the address is a closed port on loopback, so nothing is resolved and nothing leaves the machine.
public sealed class IntifacePluginTests : IDisposable
{
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-device-");
    private readonly FrameClock _clock = new(1_800_000_000.0);
    private readonly JsonSettingsStore _settings;
    private readonly GoonerApp _app;

    public IntifacePluginTests()
    {
        var data = new UserDataStore(Path.Combine(_tmp.FullName, "data"));
        _settings = new JsonSettingsStore(data);
        _app = new GoonerApp(new FakeStage(), _clock, new GoonerAppOptions
        {
            Settings = _settings,
            DataStore = data,
            CalloutDir = Repo.PathTo(Path.Combine("res", "callouts")),
            Random = new ScriptedRandom(1),
            DeviceOutput = true,
        });
    }

    public void Dispose()
    {
        _app.Close();
        Gooner.Core.Logging.AppLog.Configure(false, null);
        _tmp.Delete(recursive: true);
    }

    private IntifacePlugin Plugin => _app.Intiface!;

    private IntifacePlugin Ready()
    {
        Plugin.Controller.Pretend(enabled: true, "Test Linear");
        return Plugin;
    }

    private void SessionStarts()
    {
        _app.LoadPlaylist([Path.Combine(_tmp.FullName, "a.png")]);
        _clock.Now += 0.1;
        _app.Frame();
    }

    // --- the arming policy ---

    [Fact]
    public void starting_a_session_arms_device_sync()
    {
        var plugin = Ready();
        Assert.False(plugin.Armed);

        SessionStarts();

        Assert.True(plugin.Armed);
    }

    // The connection arriving a moment after Start is not a resume, and treating it as one stranded
    // anyone whose server came up late: sync stayed off the whole session with no way back but the
    // settings.
    [Fact]
    public void a_device_turning_up_mid_session_arms_without_being_asked()
    {
        var plugin = Ready();
        plugin.Controller.Pretend(enabled: true, "");
        SessionStarts();
        Assert.False(plugin.Armed);

        plugin.Controller.OnDevice("Test Linear");

        Assert.True(plugin.Armed);
    }

    [Fact]
    public void losing_the_device_latches_sync_off()
    {
        var plugin = Ready();
        SessionStarts();

        plugin.Controller.OnDevice("");
        Assert.False(plugin.Armed);

        plugin.Controller.OnDevice("Test Linear");
        Assert.True(plugin.Armed);
    }

    [Fact]
    public void panic_latches_sync_off()
    {
        var plugin = Ready();
        SessionStarts();

        _app.Panic();

        Assert.False(plugin.Armed);
    }

    [Fact]
    public void the_end_of_a_session_latches_sync_off()
    {
        var plugin = Ready();
        SessionStarts();

        _app.Stop();
        _app.AnswerHowItEnded(null);

        Assert.False(plugin.Armed);
        Assert.False(plugin.SessionActive);
    }

    [Fact]
    public void sync_never_arms_without_a_session()
    {
        var plugin = Ready();

        plugin.ResumeSync();

        Assert.False(plugin.Armed);
        Assert.False(plugin.CanResume);
    }

    [Fact]
    public void resume_arms_again_after_an_emergency_stop()
    {
        var plugin = Ready();
        SessionStarts();

        plugin.EmergencyStop();
        Assert.False(plugin.Armed);
        Assert.True(plugin.CanResume);

        plugin.ResumeSync();
        Assert.True(plugin.Armed);
    }

    [Fact]
    public void a_rhythm_pause_gates_device_sync()
    {
        Ready();
        SessionStarts();

        _app.Beat.StartPause();
        Assert.True(Plugin.Paused);

        _app.Beat.PauseTimer.Stop();
        _app.Beat.CurPauseDur = 0;
        _app.Beat.PauseLoop();
        Assert.False(Plugin.Paused);
    }

    // Deleting the plugin has to leave a working app behind - which is why device output is a part
    // the app can go without.
    [Fact]
    public void the_app_works_without_device_output()
    {
        var app = new GoonerApp(new FakeStage(), new FrameClock(1_800_000_000.0), new GoonerAppOptions());

        Assert.Null(app.Intiface);
        app.Panic();
        app.Close();
    }

    // --- the device button in the controls row ---

    [Fact]
    public void the_device_button_stays_out_of_the_way_until_output_is_on()
    {
        Plugin.Controller.Pretend(enabled: false, "Test Linear");
        Assert.False(Plugin.Button.Visible);

        Plugin.Controller.Pretend(enabled: true, "Test Linear");
        Assert.True(Plugin.Button.Visible);
    }

    // Panic latches device sync off and keeps it off - the window is often restored by accident, and
    // a device starting to move on its own then is the worst thing this app could do. So the way back
    // is a deliberate click, somewhere it can be seen while a session runs.
    [Fact]
    public void after_panic_the_button_offers_the_way_back()
    {
        var plugin = Ready();
        SessionStarts();
        _app.Panic();
        Assert.False(plugin.Armed);

        var button = plugin.Button;
        Assert.True(button.Enabled);
        Assert.Contains("Resume", button.Text);
        Assert.True(button.Primary);

        plugin.Press();
        Assert.True(plugin.Armed);
    }

    [Fact]
    public void the_button_is_the_stop_while_sync_is_running()
    {
        var plugin = Ready();
        SessionStarts();
        Assert.True(plugin.Armed);
        Assert.Contains("Stop", plugin.Button.Text);
        Assert.False(plugin.Button.Primary);

        plugin.Press();

        Assert.False(plugin.Armed);
    }

    [Fact]
    public void the_button_says_why_when_there_is_nothing_to_resume()
    {
        Plugin.Controller.Pretend(enabled: true, "");

        Assert.False(Plugin.Button.Enabled);
        Assert.Equal("No device", Plugin.Button.Text);
        Assert.Equal(Plugin.Controller.Status, Plugin.Button.Tooltip);
    }

    [Fact]
    public void a_connected_device_without_a_session_is_ready()
    {
        Ready();

        Assert.False(Plugin.Button.Enabled);
        Assert.Equal("Device ready", Plugin.Button.Text);
    }

    // --- panic and the way out ---

    [Fact]
    public void panic_stops_the_device_even_with_no_session_running()
    {
        var before = Plugin.Controller.Cancellations;

        _app.Panic();

        Assert.True(Plugin.Controller.Cancellations > before);
    }

    [Fact]
    public void closing_stops_the_device_and_shuts_the_connection_down()
    {
        var before = Plugin.Controller.Cancellations;

        _app.Close();

        Assert.True(Plugin.Controller.Cancellations > before);
        Assert.True(Plugin.IsShutDown);
    }

    // --- the Device tab ---

    private DeviceSettings Tab() => new(Plugin, _settings);

    // Intiface Central shows the address its server listens on, and that is the only one that can be
    // right for a given machine. Filling one in would also mean shipping a network address inside an
    // app that promises not to contact any.
    [Fact]
    public void the_device_tab_ships_no_address_of_its_own()
    {
        var tab = Tab();

        Assert.False(tab.Enabled);
        Assert.Equal("", tab.Server);
        Assert.Equal(25, tab.MinimumPercent);
        Assert.Equal(75, tab.MaximumPercent);
        Assert.False(tab.CanTest);
    }

    // The address only has to be a real one when output is being switched on. A typo in a tab the
    // user is not even using has no business locking the Save button.
    [Fact]
    public void a_half_typed_address_does_not_block_every_other_setting()
    {
        var form = new SettingsForm(_app);
        form.Device!.Server = "not an address";
        form.Numbers["max_dur"].Value = 9;
        form.Numbers["min_dur"].Value = 8;

        Assert.True(form.Apply());

        Assert.Equal(8, _app.Player.MinDur);
    }

    [Fact]
    public void an_impossible_stroke_range_still_blocks_the_save()
    {
        var form = new SettingsForm(_app);
        form.Device!.MinimumPercent = 95;
        form.Device.MaximumPercent = 20;
        form.Numbers["max_dur"].Value = 9;
        form.Numbers["min_dur"].Value = 8;
        var old = _app.Player.MinDur;

        Assert.False(form.Apply());

        Assert.Equal(old, _app.Player.MinDur);
        Assert.False(Plugin.Controller.Enabled);
        Assert.Contains("Stroke positions", form.ValidationError());
    }

    [Fact]
    public void the_device_tab_is_one_of_the_settings_tabs() =>
        Assert.Equal(IntifacePlugin.TabTitle, new SettingsForm(_app).Tabs[^1].Title);

    // Ticking the box is the act of connecting, not a preference saved later - which is also where
    // consent belongs: the moment something would actually leave the machine.
    [Fact]
    public void enabling_device_output_asks_first_and_connects_there_and_then()
    {
        var tab = Tab();
        tab.Server = "ws://127.0.0.1:1";

        Assert.Equal(DeviceStep.AskConsent, tab.EnableClicked(true));
        Assert.False(Plugin.Controller.Enabled);
        Assert.Contains("ws://127.0.0.1:1", tab.ConsentText);

        tab.ConsentGiven(remember: false);

        Assert.True(Plugin.Controller.Enabled);
        Assert.Equal("ws://127.0.0.1:1", Plugin.Controller.ServerUrl);
        Assert.Equal((0.25, 0.75), (Plugin.Controller.MinPosition, Plugin.Controller.MaxPosition));
    }

    [Fact]
    public void declining_the_consent_leaves_device_output_off()
    {
        var tab = Tab();
        tab.Server = "ws://127.0.0.1:1";
        tab.EnableClicked(true);

        tab.ConsentDeclined();

        Assert.False(tab.Enabled);
        Assert.False(Plugin.Controller.Enabled);
        Assert.False(Plugin.Controller.HasWorker);
    }

    [Fact]
    public void enabling_with_no_address_says_so_instead_of_connecting()
    {
        var tab = Tab();

        Assert.Equal(DeviceStep.Refused, tab.EnableClicked(true));

        Assert.False(tab.Enabled);
        Assert.Contains("Intiface Central", tab.Error);
        Assert.False(Plugin.Controller.Enabled);
    }

    // "Don't ask me this again" is honoured - and only once it was ticked.
    [Fact]
    public void a_remembered_consent_is_not_asked_again()
    {
        var tab = Tab();
        tab.Server = "ws://127.0.0.1:1";
        tab.EnableClicked(true);
        tab.ConsentGiven(remember: true);
        tab.EnableClicked(false);

        Assert.Equal(DeviceStep.Done, tab.EnableClicked(true));
        Assert.True(Plugin.Controller.Enabled);
    }

    [Fact]
    public void switching_output_off_needs_no_consent()
    {
        var tab = Tab();
        tab.Server = "ws://127.0.0.1:1";
        tab.EnableClicked(true);
        tab.ConsentGiven(remember: false);

        Assert.Equal(DeviceStep.Done, tab.EnableClicked(false));
        Assert.False(Plugin.Controller.Enabled);
    }

    [Fact]
    public void the_stroke_range_is_applied_on_save()
    {
        var tab = Tab();
        tab.Server = "ws://127.0.0.1:1";
        tab.MinimumPercent = 15;
        tab.MaximumPercent = 85;

        Assert.Equal(DeviceStep.Done, tab.Apply());

        Assert.False(Plugin.Controller.Enabled);
        Assert.Equal("ws://127.0.0.1:1", Plugin.Controller.ServerUrl);
        Assert.Equal((0.15, 0.85), (Plugin.Controller.MinPosition, Plugin.Controller.MaxPosition));
    }

    [Fact]
    public void saving_unrelated_settings_does_not_touch_the_connection()
    {
        var changes = 0;
        Plugin.Controller.StateChanged += () => changes++;

        Assert.Equal(DeviceStep.Done, Tab().Apply());

        Assert.Equal(0, changes);
    }

    private void AlreadyConnectedTo(string address)
    {
        Plugin.Controller.Configure(false, address, 0.25, 0.75);
        Plugin.Controller.Pretend(enabled: true, "");
    }

    // Consent names the address it is for, and says that one which is not this machine crosses the
    // network. Agreeing to a server on your desk and being moved to another one by a Save would make
    // that promise worth nothing.
    [Fact]
    public void saving_a_different_address_asks_before_connecting_to_it()
    {
        AlreadyConnectedTo("ws://127.0.0.1:2");
        var tab = Tab();
        tab.Server = "ws://127.0.0.1:3";

        Assert.Equal(DeviceStep.AskConsent, tab.Apply());
        Assert.Equal("ws://127.0.0.1:2", Plugin.Controller.ServerUrl);
        Assert.Contains("ws://127.0.0.1:3", tab.ConsentText);

        tab.ConsentGiven(remember: false);

        Assert.Equal("ws://127.0.0.1:3", Plugin.Controller.ServerUrl);
        Assert.True(Plugin.Controller.Enabled);
    }

    [Fact]
    public void declining_the_new_address_leaves_the_connection_where_it_was()
    {
        AlreadyConnectedTo("ws://127.0.0.1:2");
        var tab = Tab();
        tab.Server = "ws://127.0.0.1:3";
        tab.Apply();

        tab.ConsentDeclined();

        Assert.Equal("ws://127.0.0.1:2", Plugin.Controller.ServerUrl);
        // What is on screen has to match what is actually connected.
        Assert.Equal("ws://127.0.0.1:2", tab.Server);
    }

    [Fact]
    public void saving_only_the_stroke_range_does_not_ask_again()
    {
        AlreadyConnectedTo("ws://127.0.0.1:2");
        var tab = Tab();
        tab.MinimumPercent = 30;

        Assert.Equal(DeviceStep.Done, tab.Apply());

        Assert.Equal((0.30, 0.75), (Plugin.Controller.MinPosition, Plugin.Controller.MaxPosition));
    }

    [Fact]
    public void the_tab_says_where_device_sync_stands()
    {
        var tab = Tab();
        Assert.Equal("Device sync: waiting for a session", tab.SyncLine);
        Assert.Equal("Device: No linear device connected", tab.DeviceLine);

        Ready();
        SessionStarts();
        Assert.Equal("Device sync: active", Tab().SyncLine);
        Assert.Equal("Device: Test Linear", Tab().DeviceLine);
        Assert.True(Tab().CanTest);

        Plugin.EmergencyStop();
        Assert.Equal("Device sync: stopped", Tab().SyncLine);
        Assert.True(Tab().CanResume);
    }

    [Fact]
    public void the_tabs_buttons_reach_the_device()
    {
        Ready();
        SessionStarts();
        var tab = Tab();

        tab.EmergencyStop();
        Assert.False(Plugin.Armed);

        tab.Resume();
        Assert.True(Plugin.Armed);
    }

    // --- what the user is told before a single byte leaves the machine ---

    [Fact]
    public void the_consent_spells_out_what_goes_over_the_wire()
    {
        var text = IntifaceConsent.Text("ws://somewhere:1");

        Assert.Contains("ws://somewhere:1", text);
        Assert.Contains("\"GoonerApp\"", text);
        Assert.Contains("movement and stop commands", text);
        Assert.Contains("ws://", text);
        Assert.Contains("every launch", text);
    }

    // --- Privacy & Data ---

    // The diagnostic log lives in Privacy & Data because it writes extra data about the user; an
    // outbound connection is at least that, so it is named there as well - the switch stays in the
    // settings, beside the address and the test buttons it needs.
    [Fact]
    public void device_output_is_disclosed_here_too()
    {
        var text = new PrivacyData(_app).DeviceOutputNote!;

        Assert.Contains("off", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Settings", text);
    }

    [Fact]
    public void clearing_all_settings_also_drops_a_live_device_connection()
    {
        Plugin.Controller.Configure(false, "ws://somewhere:1", 0.25, 0.75);
        Plugin.Controller.Pretend(enabled: true, "Test Linear");

        new PrivacyData(_app).ClearCategories(["settings"]);

        Assert.False(Plugin.Controller.Enabled);
        Assert.Equal("", Plugin.Controller.ServerUrl);
        Assert.Null(_settings.Value("IntifaceController/server_url"));
    }

    [Fact]
    public void the_dialog_works_without_the_device_plugin()
    {
        var app = new GoonerApp(new FakeStage(), new FrameClock(1_800_000_000.0), new GoonerAppOptions { Settings = _settings });
        var privacy = new PrivacyData(app);

        Assert.Null(privacy.DeviceOutputNote);
        privacy.ClearCategories(["settings"]);
    }

    // There is no main window with a row of buttons: the way back is the dock's Device button.
    [Fact]
    public void the_way_back_is_named_where_it_is()
    {
        Assert.DoesNotContain("next to Mute", DeviceSettings.SyncNote, StringComparison.Ordinal);
        Assert.Contains("in the dock", DeviceSettings.SyncNote, StringComparison.Ordinal);
    }
}
