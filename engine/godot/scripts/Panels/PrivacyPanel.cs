using System.Collections.Generic;
using System.Linq;
using Gooner.Core.App;
using Gooner.Core.Logging;
using Gooner.Core.Panels;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine.Panels;

// PrivacyDataDialog: what Gooner.Core.Panels.PrivacyData says and does - where everything is, the
// diagnostic log's switch, and deleting any of it per category.
public partial class PrivacyPanel : AppPanel
{
    private readonly PrivacyData _privacy;
    private readonly PanelHost _host;
    private readonly Dictionary<string, CheckBox> _boxes = [];
    private readonly Button _delete;

    public PrivacyPanel(PrivacyData privacy, PanelHost host)
        : base("Your Data", new Vector2(620, 640), scrolls: true)
    {
        _privacy = privacy;
        _host = host;
        Body.AddChild(Widgets.Text(
            "GoonerApp never sends any of this anywhere. It does have to keep some of it on disk, though - here is exactly what, and where.",
            15, Palette.Text, wrap: true));

        // Selectable, so a path can be copied out rather than retyped.
        var locations = new RichTextLabel
        {
            Text = privacy.LocationsText,
            FitContent = true,
            SelectionEnabled = true,
            ContextMenuEnabled = true,
            ScrollActive = false,
        };
        locations.AddThemeFontOverride("normal_font", UiTheme.Mono);
        locations.AddThemeFontSizeOverride("normal_font_size", 13);
        var box = UiTheme.Box(UiTheme.Of(Palette.SurfaceDark), 6, 8);
        locations.AddThemeStyleboxOverride("normal", box);
        Body.AddChild(locations);

        var open = new Button { Text = "Open data folder", SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        open.Pressed += () =>
        {
            if (privacy.DataFolderToOpen() is { } folder) OS.ShellOpen(folder);
        };
        Body.AddChild(open);

        BuildDiagnosticLog();

        Body.AddChild(Header("Delete my data"));
        foreach (var category in PrivacyData.Categories)
        {
            var check = new CheckBox { TooltipText = category.Description, FocusMode = FocusModeEnum.None };
            check.Toggled += on =>
            {
                privacy.Select(category.Key, on);
                _delete!.Disabled = !privacy.CanDelete;
            };
            _boxes[category.Key] = check;
            Body.AddChild(check);
        }
        _delete = new Button { Text = "Delete selected...", Disabled = true, SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        _delete.Pressed += AskToDelete;
        Body.AddChild(_delete);

        AddButton("Close", Close, primary: true);
        RefreshCounts();
    }

    private static Label Header(string text)
    {
        var header = Widgets.Text(text, 14, Palette.Accent, bold: true);
        header.CustomMinimumSize = new Vector2(0, 30);
        header.VerticalAlignment = VerticalAlignment.Bottom;
        return header;
    }

    private void BuildDiagnosticLog()
    {
        Body.AddChild(Header("Diagnostic log"));
        Body.AddChild(Widgets.Text(
            "Off unless you switch it on. Records what the app is doing so a problem can be traced - which also means it records when you used it. It never writes down the folders you play from; a file that fails to load is named, nothing else is.",
            15, Palette.Text, wrap: true));
        var enabled = new CheckBox { Text = "Write a diagnostic log file", ButtonPressed = _privacy.DiagnosticLog, FocusMode = FocusModeEnum.None };
        var level = new OptionButton { FocusMode = FocusModeEnum.None };
        foreach (var name in AppLog.Levels) level.AddItem(PrivacyData.LevelLabels[name]);
        var saved = AppLog.Levels.ToList().IndexOf(_privacy.DiagnosticLogLevel);
        if (saved >= 0) level.Select(saved);
        level.Disabled = !enabled.ButtonPressed;
        // Both apply at once: this panel has no Save button.
        void Apply()
        {
            level.Disabled = !enabled.ButtonPressed;
            _privacy.SetDiagnosticLog(enabled.ButtonPressed, AppLog.Levels[level.Selected]);
            RefreshCounts();
        }
        enabled.Toggled += _ => Apply();
        level.ItemSelected += _ => Apply();
        Body.AddChild(enabled);
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "Record:" });
        row.AddChild(level);
        Body.AddChild(row);
    }

    private void RefreshCounts()
    {
        foreach (var (key, check) in _boxes) check.Text = _privacy.LabelFor(key);
    }

    private void AskToDelete()
    {
        if (!_privacy.CanDelete) return;
        Confirm.Ask(_host, "Delete this data?", _privacy.ConfirmationText(), () =>
        {
            _privacy.DeleteSelected();
            foreach (var check in _boxes.Values) check.SetPressedNoSignal(false);
            _delete.Disabled = true;
            RefreshCounts();
        });
    }
}
