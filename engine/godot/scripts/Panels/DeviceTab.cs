using System;
using Gooner.Core.App;
using Gooner.Core.Devices;
using Gooner.Core.Localization;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine.Panels;

// IntifaceSettingsWidget: the Device tab, drawn from Gooner.Core.Devices.DeviceSettings. Ticking the
// box connects there and then - after the consent - while the address and the stroke range are
// applied with the rest of the settings on Save.
public partial class DeviceTab : VBoxContainer
{
    private readonly DeviceSettings _device;
    private readonly PanelHost _host;
    private readonly CheckBox _enabled;
    private readonly LineEdit _server;
    private readonly SpinBox _minimum;
    private readonly SpinBox _maximum;
    private readonly Label _error;
    private readonly Label _status;
    private readonly Label _deviceLine;
    private readonly Label _syncLine;
    private readonly Button _testUp;
    private readonly Button _testDown;
    private readonly Button _resume;
    private readonly Button _scan;

    public DeviceTab(DeviceSettings device, PanelHost host)
    {
        _device = device;
        _host = host;
        AddThemeConstantOverride("separation", 8);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddChild(Widgets.Text(DeviceSettings.BetaNotice, 13, Palette.Text, wrap: true));
        _enabled = new CheckBox { Text = L10n.T("Enable device output"), ButtonPressed = device.Enabled, FocusMode = FocusModeEnum.None };
        _enabled.Toggled += EnableClicked;
        AddChild(_enabled);
        AddChild(Widgets.Text(DeviceSettings.AddressHint, 13, Palette.Text, wrap: true));

        var form = new GridContainer { Columns = 2 };
        form.AddThemeConstantOverride("h_separation", 10);
        form.AddChild(new Label { Text = L10n.T("Server:") });
        _server = new LineEdit { Text = device.Server, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _server.TextChanged += text => _device.Server = text;
        form.AddChild(_server);
        form.AddChild(new Label { Text = L10n.T("Minimum position (DOWN):") });
        _minimum = Percent(device.MinimumPercent, value => _device.MinimumPercent = value);
        form.AddChild(_minimum);
        form.AddChild(new Label { Text = L10n.T("Maximum position (UP):") });
        _maximum = Percent(device.MaximumPercent, value => _device.MaximumPercent = value);
        form.AddChild(_maximum);
        AddChild(form);

        _error = Widgets.Text("", 13, Palette.Denied, wrap: true);
        _status = Widgets.Text("", 13, Palette.Text, wrap: true);
        _deviceLine = Widgets.Text("", 13, Palette.Text, wrap: true);
        _syncLine = Widgets.Text("", 13, Palette.Text, wrap: true);
        foreach (var label in new[] { _error, _status, _deviceLine, _syncLine }) AddChild(label);

        var buttons = new HBoxContainer();
        _testUp = Button(L10n.T("Test Up"), device.TestUp);
        _testDown = Button(L10n.T("Test Down"), device.TestDown);
        buttons.AddChild(_testUp);
        buttons.AddChild(_testDown);
        buttons.AddChild(Button(L10n.T("Emergency Stop"), device.EmergencyStop));
        AddChild(buttons);
        _resume = Button(L10n.T("Resume Device Sync"), device.Resume);
        AddChild(_resume);
        _scan = Button(L10n.T("Scan for Devices"), device.Scan);
        AddChild(_scan);
        AddChild(Widgets.Text(DeviceSettings.SyncNote, 13, Palette.Text, wrap: true));

        _device.Changed += Refresh;
        TreeExiting += () => _device.Changed -= Refresh;
        Refresh();
    }

    private static SpinBox Percent(double value, Action<double> changed)
    {
        var spin = new SpinBox { MinValue = 0, MaxValue = 100, Step = 0.1, Suffix = "%", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        spin.SetValueNoSignal(value);
        spin.ValueChanged += changed.Invoke;
        return spin;
    }

    private static Button Button(string text, Action pressed)
    {
        var button = new Button { Text = text, FocusMode = FocusModeEnum.None };
        button.Pressed += pressed;
        return button;
    }

    // The act of connecting, so it happens now - and it is where consent is asked, because this is
    // the moment something would actually leave the machine.
    private void EnableClicked(bool on)
    {
        switch (_device.EnableClicked(on))
        {
            case DeviceStep.AskConsent:
                DeviceConsent.Ask(_host, _device, Refresh);
                break;
            default:
                Refresh();
                break;
        }
    }

    public void Refresh()
    {
        if (!IsInstanceValid(this)) return;
        _device.Refresh();
        _enabled.SetPressedNoSignal(_device.Enabled);
        if (_server.Text != _device.Server) _server.Text = _device.Server;
        _error.Text = _device.Error;
        _error.Visible = _error.Text.Length > 0;
        _status.Text = _device.Status;
        _deviceLine.Text = _device.DeviceLine;
        _syncLine.Text = _device.SyncLine;
        _testUp.Disabled = _testDown.Disabled = !_device.CanTest;
        _scan.Disabled = !_device.CanScan;
        _resume.Disabled = !_device.CanResume;
    }
}

// The question asked before device output connects anywhere, with "Don't ask me this again".
// No is the default, as for every question that lets something leave the machine.
public static class DeviceConsent
{
    public static void Ask(PanelHost host, DeviceSettings device, Action answered)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(device);
        var panel = new AppPanel(L10n.T("Enable Device Output?"), new Vector2(520, 0));
        panel.AddText(device.ConsentText, 14, wrap: true);
        var remember = new CheckBox { Text = L10n.T("Don't ask me this again"), FocusMode = Control.FocusModeEnum.None };
        panel.Body.AddChild(remember);
        void Decline()
        {
            panel.Close();
            device.ConsentDeclined();
            answered();
        }
        panel.AddButton(L10n.T("Yes"), () =>
        {
            panel.Close();
            device.ConsentGiven(remember.ButtonPressed);
            answered();
        });
        panel.AddButton(L10n.T("No"), Decline, primary: true);
        panel.OnReject = Decline;
        host.Show(panel);
    }
}
