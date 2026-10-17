using Gooner.Core.App;
using Gooner.Core.Localization;
using Gooner.Core.Settings;

namespace Gooner.Core.Devices;

// What the device's one control in the controls row shows.
public sealed record DeviceButton(bool Visible, string Text, bool Enabled, string Tooltip, bool Primary);

// Port of IntifacePlugin in src/plugins/intiface/plugin.py: the arming policy and the app-facing
// surface. Three objects with one job each - BeatSync reads the rhythm, IntifaceController talks to
// the server, and this decides when the two may be connected to each other, the one question
// neither can answer alone. Device output is optional: an app without it has no Intiface at all.
//
// Panic, an emergency stop, losing the device and the end of a session all latch device sync off,
// and it stays off until the user says otherwise - the window often comes back by accident, and a
// device that starts moving on its own at that moment is the worst thing this app could do. A
// session starting, or the device arriving during one, arms it; rhythm pauses resume on their own.
public sealed class IntifacePlugin
{
    // The settings tab's name; shown translated.
    public static string TabTitle => L10n.N("Device (Beta)");
    public static string ResumeText => L10n.T("Resume Device");
    public static string StopText => L10n.T("Stop Device");

    internal IntifacePlugin(GoonerApp app, IntifaceController controller)
    {
        Controller = controller;
        Sync = new BeatSync(app.Beat, controller);
        controller.StateChanged += () => StateChanged?.Invoke();
        // A device turning up during a running session is the connection finally arriving, not a
        // resume, so it arms. Losing one latches sync off instead.
        controller.DeviceFound += ArmIfPossible;
        controller.DeviceLost += EmergencyStop;
        Sync.Attach();
        app.SessionStarted += () =>
        {
            SessionActive = true;
            ArmIfPossible();
        };
        app.SessionEnded += () =>
        {
            SessionActive = false;
            EmergencyStop();
        };
        app.Panicked += EmergencyStop;
    }

    public IntifaceController Controller { get; }

    public BeatSync Sync { get; }

    public bool SessionActive { get; private set; }

    public bool Armed => Sync.Armed;

    public bool Paused => Sync.Paused;

    public bool CanResume => Controller.Enabled && Controller.DeviceName.Length > 0 && SessionActive && !Armed;

    public bool IsShutDown { get; private set; }

    public event Action? StateChanged;

    private void ArmIfPossible()
    {
        if (Controller.Enabled && Controller.DeviceName.Length > 0 && SessionActive) Sync.Arm();
        StateChanged?.Invoke();
    }

    public void ResumeSync() => ArmIfPossible();

    public void EmergencyStop()
    {
        Sync.Disarm();
        StateChanged?.Invoke();
    }

    // The button mirrors Mute/Unmute beside it: whichever of the two things is worth doing now.
    public void Press()
    {
        if (Armed) EmergencyStop();
        else ResumeSync();
    }

    // There only while device output is on - it doubles as the disclosure that a connection is open.
    // The accent belongs to the one state asking for a click.
    public DeviceButton Button
    {
        get
        {
            if (!Controller.Enabled) return new DeviceButton(false, "", false, "", false);
            if (Armed)
            {
                return new DeviceButton(true, StopText, true,
                    Paused ? L10n.T("Device sync is running on a rhythm pause - it comes back on its own") : L10n.T("Stop the device without ending the session"),
                    false);
            }
            if (CanResume)
                return new DeviceButton(true, ResumeText, true, L10n.T("Device sync stopped. It stays stopped until you say so."), true);
            return new DeviceButton(true, Controller.DeviceName.Length == 0 ? L10n.T("No device") : L10n.T("Device ready"), false, L10n.T(Controller.Status), false);
        }
    }

    // Whether the app has to wait for the controller's ShutdownFinished before it is gone.
    public bool Shutdown()
    {
        EmergencyStop();
        Controller.Shutdown();
        IsShutDown = true;
        return Controller.HasWorker;
    }

    // Privacy & Data's "delete all settings" - before the wipe, since disconnecting writes the
    // cleared-out address back.
    public void ForgetSettings()
    {
        EmergencyStop();
        Controller.DisconnectAndForget();
    }
}

// Port of src/plugins/intiface/consent.py: what the user is told before a single byte leaves the
// machine. Device output carries far more than the update check - a live account of a session
// while it happens - so it is asked for in the same voice and with the same precision.
public static class IntifaceConsent
{
    public static string Key => $"{IntifaceController.SettingsGroup}/consent_acknowledged";

    // The address is quoted back rather than checked against a list of local ones: the user typed
    // it, only they know whether it is this machine, and the app ships no addresses to compare with.
    public static string Text(string url) => L10n.F(
        "This will open a WebSocket connection to {0} and keep it open for as long as " +
        "device output stays enabled.\n\n" +
        "Over it, GoonerApp sends the client name \"GoonerApp\", device discovery and " +
        "heartbeat messages, and movement and stop commands. Those commands are a live " +
        "account of your session while it happens: when it starts and ends, when every " +
        "beat lands, and every pause. Nothing else is sent - no media, no folder names, " +
        "no filenames, no callouts, no statistics.\n\n" +
        "That address is yours, not the app's. If it is not on this machine, everything " +
        "above travels across your network, and an unencrypted ws:// address sends it in " +
        "the clear.\n\n" +
        "Device output switches itself off again at every launch, so you will be asked " +
        "for it the next time too.\n\n" +
        "Enable device output?", url);

    public static bool Acknowledged(ISettingsStore? settings) => settings?.GetBool(Key, false) ?? false;

    public static void Remember(ISettingsStore? settings) => settings?.SetValue(Key, true);
}

// What the Device tab needs done after a click or a save: nothing more, a refusal it now shows, or
// the consent question - after which ConsentGiven or ConsentDeclined finishes the job.
public enum DeviceStep
{
    Done,
    Refused,
    AskConsent,
}

// Port of IntifaceSettingsWidget in src/plugins/intiface/settings_widget.py without its widgets: the
// Device tab. What is stored - the address and the stroke range - is applied when the settings are
// saved, like every other setting there. Turning output on is not stored at all (it starts off at
// every launch), so it is not a setting but an action: it happens the moment the box is ticked,
// which is also where consent is asked, the moment something would leave the machine.
public sealed class DeviceSettings
{
    public static string BetaNotice => L10n.T(
        "Beta. Device output has not been tested against physical hardware yet. Space is the panic key: it " +
        "minimizes the window, mutes the sound and stops the device on the spot. Tell us how it went on the Discord.");

    public static string AddressHint => L10n.T(
        "Start Intiface Central, start its server and connect your device there. Intiface Central shows the " +
        "address its server is listening on - put that in below. The first device it offers with linear " +
        "movement support is the one that gets used.");

    public static string SyncNote => L10n.T(
        "Panic, Emergency Stop, losing the device and losing the connection all leave device sync stopped, and " +
        "it stays stopped until you say otherwise - the window often comes back by accident, and a device that " +
        "starts moving on its own at that moment is the worst thing this app could do. The Resume Device button " +
        "next to Mute in the main window is the way back; so is starting a new session, or the button below. " +
        "Rhythm pauses resume on their own.");

    private readonly IntifacePlugin _plugin;
    private readonly ISettingsStore? _settings;
    private Action? _afterConsent;
    private Action? _ifDeclined;

    public DeviceSettings(IntifacePlugin plugin, ISettingsStore? settings)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        _plugin = plugin;
        _settings = settings;
        var controller = plugin.Controller;
        Enabled = controller.Enabled;
        Server = controller.ServerUrl;
        MinimumPercent = Math.Round(controller.MinPosition * 100, 1);
        MaximumPercent = Math.Round(controller.MaxPosition * 100, 1);
    }

    private IntifaceController Controller => _plugin.Controller;

    public bool Enabled { get; private set; }

    public string Server { get; set; }

    public double MinimumPercent { get; set; }

    public double MaximumPercent { get; set; }

    public string Error { get; private set; } = "";

    public string ConsentText => IntifaceConsent.Text(Server.Trim());

    private (bool Enabled, string Url, double Low, double High) Values => (Enabled, Server.Trim(), MinimumPercent / 100, MaximumPercent / 100);

    // What stops these values from being saved, or null. The address is only checked when output is
    // being switched on: a half-typed one in a tab nobody uses must not block every other setting.
    public string? ValidationError() => ValidationError(Enabled);

    private string? ValidationError(bool enabling)
    {
        var (_, url, low, high) = Values;
        try
        {
            IntifaceController.ValidatePositions(low, high);
            if (enabling) IntifaceController.ValidateUrl(url);
        }
        catch (ArgumentException e)
        {
            return e.Message;
        }
        return null;
    }

    public DeviceStep EnableClicked(bool on)
    {
        Error = ValidationError(on) ?? "";
        if (Error.Length > 0)
        {
            Enabled = false;
            return DeviceStep.Refused;
        }
        var (_, url, low, high) = Values;
        if (on && !IntifaceConsent.Acknowledged(_settings))
            return Ask(() => Configure(true, url, low, high), () => Enabled = false);
        Configure(on, url, low, high);
        return DeviceStep.Done;
    }

    // Save: consent is per address, not per session. It names the machine it is for, so moving a
    // running connection to a different address is the same act as opening it and is asked the same
    // way - agreeing to a server on the user's own desk and then editing the field must not be
    // enough to be reconnected somewhere else.
    public DeviceStep Apply()
    {
        var values = Values;
        if (values == (Controller.Enabled, Controller.ServerUrl, Controller.MinPosition, Controller.MaxPosition)) return DeviceStep.Done;
        var (enabled, url, low, high) = values;
        if (enabled && url != Controller.ServerUrl && !IntifaceConsent.Acknowledged(_settings))
            // Declined: the field goes back to what is actually connected.
            return Ask(() => Configure(enabled, url, low, high), () => Server = Controller.ServerUrl);
        Configure(enabled, url, low, high);
        return DeviceStep.Done;
    }

    private DeviceStep Ask(Action consented, Action declined)
    {
        _afterConsent = consented;
        _ifDeclined = declined;
        return DeviceStep.AskConsent;
    }

    // "Don't ask me this again" is remembered only when it was ticked.
    public void ConsentGiven(bool remember)
    {
        if (remember) IntifaceConsent.Remember(_settings);
        var then = _afterConsent;
        _afterConsent = _ifDeclined = null;
        then?.Invoke();
    }

    public void ConsentDeclined()
    {
        var then = _ifDeclined;
        _afterConsent = _ifDeclined = null;
        then?.Invoke();
    }

    private void Configure(bool enabled, string url, double low, double high)
    {
        Controller.Configure(enabled, url, low, high);
        Enabled = Controller.Enabled;
    }

    // --- what the tab shows ---

    public string Status => L10n.T(Controller.Status);

    public string DeviceLine => L10n.F("Device: {0}", Controller.DeviceName.Length > 0 ? Controller.DeviceName : L10n.T("No linear device connected"));

    public string SyncLine =>
        !_plugin.SessionActive ? L10n.T("Device sync: waiting for a session")
        : !_plugin.Armed ? L10n.T("Device sync: stopped")
        : _plugin.Paused ? L10n.T("Device sync: rhythm pause")
        : L10n.T("Device sync: active");

    public bool CanTest => Controller.Enabled && Controller.DeviceName.Length > 0;

    public bool CanScan => Controller.Enabled;

    public bool CanResume => _plugin.CanResume;

    // The live state back onto the draft - after the connection changed under the tab.
    public void Refresh() => Enabled = Controller.Enabled;

    // --- the tab's buttons ---

    public void TestUp() => Controller.TestUp();

    public void TestDown() => Controller.TestDown();

    public void EmergencyStop() => _plugin.EmergencyStop();

    public void Resume() => _plugin.ResumeSync();

    public void Scan() => Controller.Scan();

    // Something the tab shows has changed.
    public event Action? Changed
    {
        add => _plugin.StateChanged += value;
        remove => _plugin.StateChanged -= value;
    }
}
