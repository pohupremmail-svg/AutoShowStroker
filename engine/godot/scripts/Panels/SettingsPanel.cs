using System;
using System.Collections.Generic;
using System.Linq;
using Gooner.Core.App;
using Gooner.Core.Localization;
using Gooner.Core.Panels;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine.Panels;

// SettingsDialog: a tab per Gooner.Core.Panels.SettingsForm tab, each row drawn as the dialog drew
// it. Every widget writes into the form's draft; Save & Close applies it, Esc and the cross drop it.
public partial class SettingsPanel : AppPanel
{
    private readonly SettingsForm _form;
    private readonly PanelHost _host;
    private readonly Action<Action> _openPatternEditor;
    private readonly Action _openPhraseFiles;
    private readonly Dictionary<string, SpinBox> _numbers = [];
    private readonly Dictionary<string, CheckBox> _toggles = [];
    private readonly Dictionary<string, CheckBox> _tones = [];
    private readonly Dictionary<string, List<Button>> _choices = [];
    private readonly List<GridContainer> _rhythmGrids = [];
    private readonly List<Button> _uiLanguages = [];
    private readonly TabContainer _tabs;
    private OptionButton? _language;
    private Label? _toneHint;

    // openPatternEditor gets what to do once the editor is closed again: show its patterns.
    public SettingsPanel(SettingsForm form, PanelHost host, Action<Action> openPatternEditor, Action openPhraseFiles)
        : base(L10n.T("Settings"), new Vector2(620, 660))
    {
        _form = form;
        _host = host;
        _openPatternEditor = openPatternEditor;
        _openPhraseFiles = openPhraseFiles;
        _tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        var tabs = _tabs;
        foreach (var tab in form.Tabs)
        {
            var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            column.AddThemeConstantOverride("separation", 6);
            foreach (var row in tab.Rows) column.AddChild(Row(row));
            var margin = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            foreach (var side in new[] { "left", "right", "top", "bottom" })
                margin.AddThemeConstantOverride($"margin_{side}", 10);
            margin.AddChild(column);
            var scroll = new ScrollContainer { Name = tab.Title, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            scroll.AddChild(margin);
            tabs.AddChild(scroll);
            // The node keeps the tab's name; what the tab says is its title in the language in use.
            tabs.SetTabTitle(tabs.GetTabCount() - 1, tab.DisplayTitle);
        }
        Body.AddChild(tabs);
        var save = AddButton(L10n.T("Save & Close Settings"), Save, primary: true);
        save.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        Buttons.Alignment = BoxContainer.AlignmentMode.Center;
        Buttons.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        RefreshTones();
    }

    public void ShowTab(string title) => _tabs.CurrentTab = _form.Tabs.ToList().FindIndex(t => t.Title == title);

    private Control Row(SettingsRow row) => row switch
    {
        HeaderRow header => Header(header.Text),
        NoteRow note => Widgets.Text(note.Text, 11, Palette.Text, wrap: true),
        NumberRow number => Number(_form.Numbers[number.Name]),
        ToggleRow toggle => Toggle(_form.Toggles[toggle.Name]),
        ChoiceRow choice => Choice(_form.Choices[choice.Name]),
        RhythmsRow => Rhythms(),
        ManagePatternsRow => Action(L10n.T("Manage Custom Patterns..."), () => _openPatternEditor(RefreshRhythms)),
        LanguageRow => Language(),
        UiLanguageRow => UiLanguage(),
        TonesRow => Tones(),
        ManagePhraseFilesRow => Action(L10n.T("Manage Custom Phrase Files..."), _openPhraseFiles),
        DeviceRow => new DeviceTab(_form.Device!, _host),
        ResetRow reset => Action(L10n.T("Reset to defaults"), () =>
        {
            _form.Reset(reset.Tab);
            Pull();
        }),
        _ => new Control(),
    };

    private static Label Header(string text)
    {
        var header = Widgets.Text($"--- {text} ---", 14, Palette.Accent, bold: true);
        header.CustomMinimumSize = new Vector2(0, 30);
        header.VerticalAlignment = VerticalAlignment.Bottom;
        return header;
    }

    private static Button Action(string text, Action pressed)
    {
        var button = new Button { Text = text, FocusMode = FocusModeEnum.None };
        button.Pressed += pressed;
        return button;
    }

    // A label taking a third of the row and the spin box the rest, as the dialog's stretches had it.
    private HBoxContainer Number(NumberSetting setting)
    {
        var row = new HBoxContainer();
        var label = new Label { Text = setting.Label, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        // Fine-grained rather than the arrow step: a Range snaps to its step, and a stored 1.23 in a
        // 0.1-step field would quietly become 1.2 the moment the panel opened.
        var spin = new SpinBox
        {
            MinValue = setting.Min,
            MaxValue = setting.Max,
            Step = setting.IsInt ? 1 : Math.Pow(10, -setting.Decimals),
            CustomArrowStep = setting.Step,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 2,
            SelectAllOnFocus = true,
        };
        spin.SetValueNoSignal(setting.Value);
        spin.ValueChanged += value =>
        {
            setting.Value = value;
            if (Math.Abs(spin.Value - setting.Value) > 1e-12) spin.SetValueNoSignal(setting.Value);
        };
        _numbers[setting.Name] = spin;
        row.AddChild(label);
        row.AddChild(spin);
        return row;
    }

    private CheckBox Toggle(ToggleSetting setting)
    {
        var check = new CheckBox { Text = setting.Label, TooltipText = setting.Tooltip, ButtonPressed = setting.Value, FocusMode = FocusModeEnum.None };
        check.Toggled += on => setting.Value = on;
        _toggles[setting.Name] = check;
        return check;
    }

    // A button per option, the chosen one lit: the options are few, and seeing them all side by side
    // is half the point of a look setting.
    private HBoxContainer Choice(ChoiceSetting setting)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = setting.Label, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1 });
        var options = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 2 };
        options.AddThemeConstantOverride("separation", 4);
        var group = new ButtonGroup();
        var buttons = new List<Button>();
        foreach (var option in setting.Options)
        {
            var button = new Button
            {
                Text = option.Label,
                ToggleMode = true,
                ButtonGroup = group,
                FocusMode = FocusModeEnum.None,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            button.Toggled += on => button.ThemeTypeVariation = on ? "PrimaryButton" : "";
            button.ButtonPressed = option.Value == setting.Value;
            button.Pressed += () => setting.Value = option.Value;
            buttons.Add(button);
            options.AddChild(button);
        }
        _choices[setting.Name] = buttons;
        row.AddChild(options);
        return row;
    }

    // The app's own language: a button per language, each in its own name, the chosen one lit -
    // drawn like a look choice, but written to the form's own draft of it.
    private HBoxContainer UiLanguage()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        var group = new ButtonGroup();
        _uiLanguages.Clear();
        foreach (var option in _form.UiLanguages)
        {
            var button = new Button
            {
                Text = option.Label,
                ToggleMode = true,
                ButtonGroup = group,
                FocusMode = FocusModeEnum.None,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            button.Toggled += on => button.ThemeTypeVariation = on ? "PrimaryButton" : "";
            button.ButtonPressed = option.Value == _form.UiLanguage;
            button.Pressed += () => _form.UiLanguage = option.Value;
            _uiLanguages.Add(button);
            row.AddChild(button);
        }
        return row;
    }

    private GridContainer Rhythms()
    {
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 12);
        _rhythmGrids.Add(grid);
        FillRhythms(grid);
        return grid;
    }

    private void FillRhythms(GridContainer grid)
    {
        foreach (var child in grid.GetChildren()) child.QueueFree();
        foreach (var (name, ticked) in _form.Rhythms)
        {
            var check = new CheckBox { Text = _form.RhythmLabel(name), ButtonPressed = ticked, FocusMode = FocusModeEnum.None };
            check.Toggled += on => _form.SetRhythm(name, on);
            grid.AddChild(check);
        }
    }

    // After the pattern editor: its patterns appear, deleted ones go.
    private void RefreshRhythms()
    {
        _form.RefreshRhythms();
        foreach (var grid in _rhythmGrids) FillRhythms(grid);
    }

    private OptionButton Language()
    {
        _language = new OptionButton { FocusMode = FocusModeEnum.None };
        // Each under its own name, as the app's own languages are offered.
        foreach (var lang in _form.Languages) _language.AddItem(Catalogue.NativeName(lang));
        _language.Select(_form.Languages.ToList().IndexOf(_form.Language));
        _language.ItemSelected += index =>
        {
            _form.Language = _form.Languages[(int)index];
            RefreshTones();
        };
        return _language;
    }

    private VBoxContainer Tones()
    {
        var column = new VBoxContainer();
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 12);
        foreach (var (tone, ticked) in _form.Tones)
        {
            var check = new CheckBox { Text = Gooner.Core.Callouts.CalloutHandler.ToneLabel(tone), ButtonPressed = ticked, FocusMode = FocusModeEnum.None };
            check.Toggled += on => _form.SetTone(tone, on);
            _tones[tone] = check;
            grid.AddChild(check);
        }
        column.AddChild(grid);
        _toneHint = Widgets.Text("", 11, Palette.DisabledText, wrap: true);
        column.AddChild(_toneHint);
        return column;
    }

    // Greyed out, not unticked: a tone the language lacks keeps its place in the saved mix.
    private void RefreshTones()
    {
        foreach (var (tone, check) in _tones)
        {
            check.Disabled = !_form.ToneUsable(tone);
            check.TooltipText = _form.ToneTooltip(tone);
        }
        if (_toneHint is not null)
        {
            _toneHint.Text = _form.ToneHint;
            _toneHint.Visible = _toneHint.Text.Length > 0;
        }
    }

    // The draft, back onto the widgets - after a reset.
    private void Pull()
    {
        foreach (var (name, spin) in _numbers) spin.SetValueNoSignal(_form.Numbers[name].Value);
        foreach (var (name, check) in _toggles) check.SetPressedNoSignal(_form.Toggles[name].Value);
        foreach (var (tone, check) in _tones) check.SetPressedNoSignal(_form.Tones[tone]);
        foreach (var (name, buttons) in _choices)
        {
            var setting = _form.Choices[name];
            for (var i = 0; i < buttons.Count; i++)
            {
                var on = setting.Options[i].Value == setting.Value;
                buttons[i].SetPressedNoSignal(on);
                buttons[i].ThemeTypeVariation = on ? "PrimaryButton" : "";
            }
        }
        foreach (var grid in _rhythmGrids) FillRhythms(grid);
        _language?.Select(_form.Languages.ToList().IndexOf(_form.Language));
        RefreshTones();
    }

    private void Save()
    {
        if (_form.Apply())
        {
            // The device's address and range last: a changed address may first need consent, and
            // the panel closes once that is answered either way.
            if (_form.Device?.Apply() == Gooner.Core.Devices.DeviceStep.AskConsent)
                DeviceConsent.Ask(_host, _form.Device, Close);
            else
                Close();
            return;
        }
        var refusal = new AppPanel(L10n.T("Can't save these settings"), new Vector2(460, 0));
        refusal.AddText(_form.ValidationError() ?? "", 14, wrap: true);
        refusal.AddButton(L10n.T("OK"), refusal.Close, primary: true);
        _host.Show(refusal);
    }
}
