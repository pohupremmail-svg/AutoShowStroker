using System.Collections.Generic;
using System.Linq;
using Gooner.Core.App;
using Gooner.Core.Localization;
using Gooner.Core.Logging;
using Gooner.Core.Panels;
using Gooner.Engine.Ui;
using Gooner.Engine.Ui.Controls;
using Godot;

namespace Gooner.Engine.Panels;

// PrivacyDataDialog: what Gooner.Core.Panels.PrivacyData says and does - where everything is, the
// diagnostic log's switch, and deleting any of it per category - in the redesign's sections. What is
// to be deleted is ticked, never switched (the user's call on the mock, 2026-10-24: a switch beside
// "Session history" read as turning the history off).
public partial class PrivacyPanel : AppPanel
{
    private readonly PrivacyData _privacy;
    private readonly PanelHost _host;
    private readonly Dictionary<string, CheckBox> _boxes = [];
    private readonly Button _delete;

    public PrivacyPanel(PrivacyData privacy, PanelHost host)
        : base(L10n.T("Your Data"), new Vector2(700, 720), scrolls: true)
    {
        _privacy = privacy;
        _host = host;
        Body.AddThemeConstantOverride("separation", 12);
        AddText(L10n.T("GoonerApp never sends any of this anywhere. It does have to keep some of it on disk, though - here is exactly what, and where."), 14, wrap: true);

        var open = Widgets.Button(L10n.T("Open data folder"), () =>
        {
            if (privacy.DataFolderToOpen() is { } folder) OS.ShellOpen(folder);
        });
        var (where, whereRows) = Widgets.Section(L10n.T("Where it is kept"), trailing: open);
        // Selectable, so a path can be copied out rather than retyped.
        var well = new PanelContainer { ThemeTypeVariation = "Well" };
        var locations = new RichTextLabel
        {
            Text = privacy.LocationsText,
            FitContent = true,
            SelectionEnabled = true,
            ContextMenuEnabled = true,
            ScrollActive = false,
        };
        locations.AddThemeFontOverride("normal_font", UiTheme.Mono);
        locations.AddThemeFontSizeOverride("normal_font_size", 12);
        well.AddChild(locations);
        whereRows.AddChild(well);
        Body.AddChild(where);

        BuildDiagnosticLog();
        if (privacy.DeviceOutputNote is { } note)
        {
            var (device, deviceRows) = Widgets.Section(L10n.T("Device output"));
            deviceRows.AddChild(Widgets.Hint(note));
            Body.AddChild(device);
        }

        var (delete, rows) = Widgets.Section(L10n.T("Delete my data"), L10n.T("Tick what should go. Nothing is deleted before you confirm it."), separation: 8);
        foreach (var category in PrivacyData.Categories)
        {
            var check = new CheckBox { FocusMode = FocusModeEnum.None, MouseDefaultCursorShape = CursorShape.PointingHand };
            check.Toggled += on =>
            {
                privacy.Select(category.Key, on);
                _delete!.Disabled = !privacy.CanDelete;
            };
            _boxes[category.Key] = check;
            var entry = new VBoxContainer();
            entry.AddThemeConstantOverride("separation", 0);
            entry.AddChild(check);
            var hint = new MarginContainer();
            hint.AddThemeConstantOverride("margin_left", 28);
            hint.AddChild(Widgets.Hint(L10n.T(category.Description)));
            entry.AddChild(hint);
            rows.AddChild(entry);
        }
        _delete = Widgets.Button(L10n.T("Delete selected..."), AskToDelete, "Danger");
        _delete.Disabled = true;
        rows.AddChild(Widgets.Row(Widgets.Spacer(), _delete));
        Body.AddChild(delete);

        AddButton(L10n.T("Close"), Close, primary: true);
        RefreshCounts();
    }

    private void BuildDiagnosticLog()
    {
        var enabled = new ToggleSwitch(_privacy.DiagnosticLog) { TooltipText = L10n.T("Write a diagnostic log file") };
        var (log, rows) = Widgets.Section(L10n.T("Diagnostic log"),
            L10n.T("Off unless you switch it on. Records what the app is doing so a problem can be traced - which also means it records when you used it. It never writes down the folders you play from; a file that fails to load is named, nothing else is."),
            enabled);
        var level = _privacy.DiagnosticLogLevel;
        var line = new HBoxContainer();
        Segments levels = null!;
        levels = new Segments([.. AppLog.Levels.Select(name => (name, L10n.T(PrivacyData.LevelLabels[name])))], level, picked =>
        {
            level = picked;
            Apply();
        });
        line.AddThemeConstantOverride("separation", 14);
        line.AddChild(new Label { Text = L10n.T("Record:").TrimEnd(':', ' ') });
        line.AddChild(levels);
        rows.AddChild(line);
        // Both apply at once: this panel has no Save button.
        void Live()
        {
            levels.Enabled = enabled.ButtonPressed;
            line.Modulate = new Color(1, 1, 1, enabled.ButtonPressed ? 1 : 0.38f);
        }
        void Apply()
        {
            Live();
            _privacy.SetDiagnosticLog(enabled.ButtonPressed, level);
            RefreshCounts();
        }
        enabled.Toggled += _ => Apply();
        Live();
        Body.AddChild(log);
    }

    private void RefreshCounts()
    {
        foreach (var (key, check) in _boxes) check.Text = _privacy.LabelFor(key);
    }

    private void AskToDelete()
    {
        if (!_privacy.CanDelete) return;
        Confirm.Ask(_host, L10n.T("Delete this data?"), _privacy.ConfirmationText(), () =>
        {
            _privacy.DeleteSelected();
            foreach (var check in _boxes.Values) check.SetPressedNoSignal(false);
            _delete.Disabled = true;
            RefreshCounts();
        }, danger: true);
    }
}
