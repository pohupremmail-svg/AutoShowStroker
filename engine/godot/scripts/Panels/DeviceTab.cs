using System;
using Gooner.Core.App;
using Gooner.Core.Devices;
using Gooner.Core.Localization;
using Gooner.Core.Panels;
using Gooner.Engine.Ui;
using Gooner.Engine.Ui.Controls;
using Godot;

namespace Gooner.Engine.Panels;

// IntifaceSettingsWidget: the Device tab, drawn from Gooner.Core.Devices.DeviceSettings in the
// redesign's cards (2026-10-24): the beta warning, the switch with what is connected at a glance, then
// the connection - its address, the stroke range as one slider with two handles, the tests, and the
// emergency stop set apart. Switching it on connects there and then - after the consent - while the
// address and the stroke range are applied with the rest of the settings on Save.
public partial class DeviceTab : VBoxContainer
{
    private readonly DeviceSettings _device;
    private readonly PanelHost _host;
    private readonly ToggleSwitch _enabled;
    private readonly LineEdit _server;
    private readonly Label _error;
    private readonly (Dot Dot, Label Text)[] _states;
    private readonly Button _testUp;
    private readonly Button _testDown;
    private readonly Button _resume;
    private readonly Button _scan;

    public DeviceTab(DeviceSettings device, PanelHost host)
    {
        _device = device;
        _host = host;
        AddThemeConstantOverride("separation", 12);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;

        var banner = new PanelContainer();
        var warm = UiTheme.Box(UiTheme.Of(Palette.Ruined, 0.13f), 12, 12);
        warm.SetBorderWidthAll(1);
        warm.BorderColor = UiTheme.Of(Palette.Ruined, 0.4f);
        banner.AddThemeStyleboxOverride("panel", warm);
        banner.AddChild(Widgets.Text(DeviceSettings.BetaNotice, 13, Palette.Text, wrap: true));
        AddChild(banner);

        var (output, outputRows) = Widgets.Section(separation: 12);
        var head = new HBoxContainer();
        head.AddChild(new Label { Text = L10n.T("Enable device output"), ThemeTypeVariation = "SectionTitle", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _enabled = new ToggleSwitch(device.Enabled);
        _enabled.Toggled += EnableClicked;
        head.AddChild(_enabled);
        outputRows.AddChild(head);
        var states = new HBoxContainer();
        states.AddThemeConstantOverride("separation", 10);
        _states = new (Dot, Label)[3];
        for (var i = 0; i < 3; i++)
        {
            var tile = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            tile.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Of(Palette.SurfaceDarkest), 12, 10));
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 8);
            var dot = new Dot(9);
            var text = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            text.AddThemeFontSizeOverride("font_size", 13);
            line.AddChild(dot);
            line.AddChild(text);
            tile.AddChild(line);
            states.AddChild(tile);
            _states[i] = (dot, text);
        }
        outputRows.AddChild(states);
        _error = Widgets.Text("", 13, Palette.Denied, wrap: true);
        outputRows.AddChild(_error);
        AddChild(output);

        var (connection, rows) = Widgets.Section(separation: 12);
        rows.AddChild(new Label { Text = DeviceSettings.AddressHint, ThemeTypeVariation = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        _server = new LineEdit { Text = device.Server, PlaceholderText = L10n.T("the address Intiface Central shows") };
        _server.TextChanged += text => _device.Server = text;
        rows.AddChild(Pair(L10n.T("Server:"), _server));
        var stroke = new SliderSpec(0, 1, 0.001, ValueUnit.Percent);
        var slider = new SliderBar(stroke, device.MinimumPercent / 100, device.MaximumPercent / 100);
        var badge = new ValueBadge(stroke, range: true);
        badge.Show(device.MinimumPercent / 100, device.MaximumPercent / 100);
        void Take(double low, double high)
        {
            _device.MinimumPercent = Math.Round(low * 100, 1);
            _device.MaximumPercent = Math.Round(high * 100, 1);
            badge.Show(low, high);
        }
        slider.Changed += Take;
        badge.Typed += (low, high) =>
        {
            Take(low, high);
            slider.Set(low, high);
        };
        var range = new HBoxContainer();
        range.AddThemeConstantOverride("separation", 14);
        range.AddChild(slider);
        range.AddChild(badge);
        rows.AddChild(Pair(L10n.T("Stroke range (DOWN - UP):"), range));

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        _testDown = Button(L10n.T("Test Down"), device.TestDown);
        _testUp = Button(L10n.T("Test Up"), device.TestUp);
        _scan = Button(L10n.T("Scan for Devices"), device.Scan);
        _resume = Button(L10n.T("Resume Device Sync"), device.Resume);
        foreach (var button in new[] { _testDown, _testUp, _scan, _resume }) buttons.AddChild(button);
        buttons.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var stop = Button(L10n.T("Emergency Stop"), device.EmergencyStop);
        stop.ThemeTypeVariation = "Danger";
        buttons.AddChild(stop);
        rows.AddChild(buttons);
        rows.AddChild(new Label { Text = DeviceSettings.SyncNote, ThemeTypeVariation = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        AddChild(connection);

        _device.Changed += Refresh;
        TreeExiting += () => _device.Changed -= Refresh;
        Refresh();
    }

    private static HBoxContainer Pair(string label, Control control)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        row.AddChild(new Label { Text = label.TrimEnd().TrimEnd(':'), CustomMinimumSize = new Vector2(170, 0) });
        control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(control);
        return row;
    }

    private static Button Button(string text, Action pressed)
    {
        var button = new Button { Text = text, ThemeTypeVariation = "Ghost", FocusMode = FocusModeEnum.None };
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
        // Green for what is up, amber for a sync stopped and waiting to be resumed, grey for off.
        var green = new Color(0.23f, 1f, 0.63f);
        var grey = UiTheme.Of(Palette.DisabledText);
        (string Text, Color Dot)[] states =
        [
            (_device.Status, _device.Enabled ? green : grey),
            (_device.DeviceLine, _device.CanTest ? green : grey),
            (_device.SyncLine, _device.CanResume ? UiTheme.Of(Palette.Ruined) : _device.Enabled && _device.CanTest ? green : grey),
        ];
        for (var i = 0; i < states.Length; i++)
        {
            _states[i].Text.Text = states[i].Text;
            _states[i].Dot.Colour = states[i].Dot;
        }
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
