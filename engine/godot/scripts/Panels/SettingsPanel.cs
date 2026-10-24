using System;
using System.Collections.Generic;
using System.Linq;
using Gooner.Core.App;
using Gooner.Core.Beat;
using Gooner.Core.Callouts;
using Gooner.Core.Localization;
using Gooner.Core.Panels;
using Gooner.Engine.Ui;
using Gooner.Engine.Ui.Controls;
using Godot;

namespace Gooner.Engine.Panels;

// The settings (the redesign, the user's pick of the mock, 2026-10-24): a page per
// Gooner.Core.Panels.SettingsForm tab behind a side navigation, each page a column of sections on
// glass - a title, a line saying what it is for, the switch that turns the whole of it on and off.
// Numbers are sliders with a value one can type, a minimum and its maximum one slider with two
// handles; choices are segments, tiles showing what they are, or colour swatches; the Look page plays
// its choices live beside them. Every widget writes into the form's draft; Save & Close applies it,
// Cancel, Esc and the cross drop it, and the foot counts what is not saved yet.
public partial class SettingsPanel : AppPanel
{
    private static readonly Dictionary<string, string> PageIcons = new()
    {
        ["Playback"] = "play",
        ["Beat & Rhythm"] = "rhythm",
        ["Climax"] = "climax",
        ["Callouts"] = "voice",
        ["Look"] = "look",
    };

    private readonly SettingsForm _form;
    private readonly PanelHost _host;
    private readonly Action<Action> _openPatternEditor;
    private readonly Action _openPhraseFiles;
    private readonly List<Button> _nav = [];
    private readonly Control _pages = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly Label _changes;
    private readonly List<Action> _refreshTones = [];
    private int _shown;

    // openPatternEditor gets what to do once the editor is closed again: show its patterns.
    public SettingsPanel(SettingsForm form, PanelHost host, Action<Action> openPatternEditor, Action openPhraseFiles)
        : base(L10n.T("Settings"), new Vector2(1000, 720), bodyScrolls: false)
    {
        _form = form;
        _host = host;
        _openPatternEditor = openPatternEditor;
        _openPhraseFiles = openPhraseFiles;

        var row = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 18);
        var nav = new VBoxContainer { CustomMinimumSize = new Vector2(190, 0) };
        nav.AddThemeConstantOverride("separation", 4);
        var group = new ButtonGroup();
        foreach (var (tab, index) in form.Tabs.Select((t, i) => (t, i)))
        {
            var button = new Button
            {
                Text = tab.DisplayTitle,
                Icon = Icons.Get(PageIcons.GetValueOrDefault(tab.Title, "device"), 20),
                ThemeTypeVariation = "NavButton",
                ToggleMode = true,
                ButtonGroup = group,
                FocusMode = FocusModeEnum.None,
                Alignment = HorizontalAlignment.Left,
            };
            button.Pressed += () => Show(index);
            nav.AddChild(button);
            _nav.Add(button);
        }
        nav.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        var esc = new Label { Text = L10n.T("Esc closes without saving"), ThemeTypeVariation = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        nav.AddChild(esc);
        row.AddChild(nav);
        row.AddChild(_pages);
        Body.AddChild(row);
        Build();

        _changes = new Label { ThemeTypeVariation = "Hint", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        Buttons.AddChild(_changes);
        var cancel = AddButton(L10n.T("Cancel"), Reject);
        cancel.ThemeTypeVariation = "Ghost";
        AddButton(L10n.T("Save & Close Settings"), Save, primary: true);
        Show(0);
    }

    public void ShowTab(string title) => Show(Math.Max(0, _form.Tabs.ToList().FindIndex(t => t.Title == title)));

    private void Show(int index)
    {
        _shown = index;
        for (var i = 0; i < _pages.GetChildCount(); i++) ((Control)_pages.GetChild(i)).Visible = i == index;
        for (var i = 0; i < _nav.Count; i++) _nav[i].SetPressedNoSignal(i == index);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        var changes = _form.Changes;
        _changes.Text = changes == 0 ? L10n.T("No changes") : L10n.P(changes, "{0} change not saved yet", "{0} changes not saved yet");
    }

    // --- the pages ---

    // Every page, from the draft as it stands: at the start, and again after a reset or the pattern
    // editor, on the page and at the scroll position the user was at.
    private void Build()
    {
        var scrolls = _pages.GetChildren().OfType<Control>().Select(Scroll).ToList();
        foreach (var child in _pages.GetChildren()) child.QueueFree();
        _refreshTones.Clear();
        foreach (var (tab, index) in _form.Tabs.Select((t, i) => (t, i)))
        {
            var page = Page(tab);
            page.Visible = index == _shown;
            _pages.AddChild(page);
            if (index < scrolls.Count && scrolls[index] is { } at) Callable.From(() => SetScroll(page, at)).CallDeferred();
        }
    }

    private static int? Scroll(Control page) => page.FindChildren("*", nameof(ScrollContainer), owned: false).OfType<ScrollContainer>().FirstOrDefault()?.ScrollVertical;

    private static void SetScroll(Control page, int at)
    {
        if (page.FindChildren("*", nameof(ScrollContainer), owned: false).OfType<ScrollContainer>().FirstOrDefault() is { } scroll) scroll.ScrollVertical = at;
    }

    private VBoxContainer Page(SettingsTab tab)
    {
        var page = new VBoxContainer { Name = tab.Title.Replace("&", "and", StringComparison.Ordinal) };
        page.SetAnchorsPreset(LayoutPreset.FullRect);
        page.AddThemeConstantOverride("separation", 12);

        var head = new HBoxContainer();
        var title = new Label { Text = tab.DisplayTitle, ThemeTypeVariation = "PageTitle", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeFontOverride("font", UiTheme.Display);
        head.AddChild(title);
        if (tab.Rows.OfType<ResetRow>().FirstOrDefault() is { } reset)
        {
            var link = new Button { Text = L10n.T("Reset this page"), ThemeTypeVariation = "Link", FocusMode = FocusModeEnum.None };
            link.Pressed += () =>
            {
                _form.Reset(reset.Tab);
                Build();
            };
            head.AddChild(link);
        }
        page.AddChild(head);

        var sections = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        sections.AddThemeConstantOverride("separation", 12);
        Fill(sections, tab.Rows);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var inset = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        inset.AddThemeConstantOverride("margin_right", 12);
        inset.AddChild(sections);
        scroll.AddChild(inset);

        // The Look page plays its choices beside them, where scrolling cannot take the preview away.
        if (tab.Rows.OfType<LookPreviewRow>().Any())
        {
            var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
            columns.AddThemeConstantOverride("separation", 14);
            scroll.SizeFlagsStretchRatio = 1.05f;
            columns.AddChild(scroll);
            var side = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 0.95f };
            side.AddThemeConstantOverride("separation", 8);
            var preview = new PanelContainer();
            var frame = UiTheme.Box(Colors.Black, 14);
            frame.SetBorderWidthAll(1);
            frame.BorderColor = new Color(1, 1, 1, 0.1f);
            preview.AddThemeStyleboxOverride("panel", frame);
            preview.ClipChildren = ClipChildrenMode.Only;
            preview.AddChild(new LookPreview(() => (Choice("note_track"), Choice("censor"), Choice("effects"), _form.Toggles["focus_wanders"].Value)));
            side.AddChild(preview);
            side.AddChild(new Label
            {
                Text = L10n.T("Plays your choices as you make them - track, ring and effects - over a stand-in picture. The colours change once saved."),
                ThemeTypeVariation = "Hint",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
            columns.AddChild(side);
            page.AddChild(columns);
        }
        else
        {
            page.AddChild(scroll);
        }
        return page;
    }

    private string Choice(string name) => _form.Choices[name].Value;

    // The title of the section being filled: a choice that would only repeat it goes without a caption.
    private string _section = "";

    // The rows of a tab, a section card at each SectionRow.
    private void Fill(VBoxContainer sections, IReadOnlyList<SettingsRow> rows)
    {
        VBoxContainer? body = null;
        foreach (var row in rows)
        {
            if (row is SectionRow section)
            {
                body = Section(sections, section);
                _section = section.Title;
                continue;
            }
            if (row is ResetRow or LookPreviewRow) continue;
            // The device draws cards of its own.
            if (row is DeviceRow)
            {
                sections.AddChild(Row(row));
                continue;
            }
            if (body is null) body = Section(sections, null);
            body.AddChild(Row(row));
        }
    }

    // A card: its title and what it is for, the switch of the whole of it, and the column its rows go in -
    // greyed out while that switch is off.
    private VBoxContainer Section(VBoxContainer sections, SectionRow? section)
    {
        var card = new PanelContainer { ThemeTypeVariation = "Section" };
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        card.AddChild(column);
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 10);
        if (section is not null)
        {
            var head = new HBoxContainer();
            head.AddThemeConstantOverride("separation", 12);
            var words = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            words.AddThemeConstantOverride("separation", 2);
            words.AddChild(new Label { Text = section.Title, ThemeTypeVariation = "SectionTitle" });
            if (section.Description.Length > 0)
                words.AddChild(new Label { Text = section.Description, ThemeTypeVariation = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart });
            head.AddChild(words);
            if (section.Switch is { } name)
            {
                var toggle = _form.Toggles[name];
                var on = new ToggleSwitch(toggle.Value) { TooltipText = toggle.Label };
                on.Toggled += value =>
                {
                    toggle.Value = value;
                    Live(rows, value);
                };
                head.AddChild(on);
                Callable.From(() => Live(rows, toggle.Value)).CallDeferred();
            }
            column.AddChild(head);
        }
        column.AddChild(rows);
        sections.AddChild(card);
        return rows;
    }

    // Greyed out and out of reach while what it belongs to is off - and back.
    private void Live(Control rows, bool live)
    {
        rows.Modulate = new Color(1, 1, 1, live ? 1 : 0.38f);
        foreach (var node in rows.FindChildren("*", "", owned: false))
        {
            switch (node)
            {
                case SliderBar slider:
                    slider.Enabled = live;
                    break;
                case ValueBadge badge:
                    badge.Enabled = live;
                    break;
                case BaseButton button:
                    button.Disabled = !live;
                    break;
            }
        }
        if (live) foreach (var refresh in _refreshTones) refresh();
    }

    private Control Row(SettingsRow row) => row switch
    {
        NoteRow note => new Label { Text = note.Text, ThemeTypeVariation = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart },
        NumberRow number => Number(number),
        RangeRow range => Range(range),
        ToggleRow toggle => Toggle(_form.Toggles[toggle.Name]),
        ChoiceRow choice => Choice(_form.Choices[choice.Name]),
        RhythmsRow => Rhythms(),
        ManagePatternsRow => Ghost(L10n.T("Manage Custom Patterns..."), () => _openPatternEditor(Refresh)),
        LanguageRow => Language(),
        UiLanguageRow => UiLanguage(),
        CharacterFolderRow => CharacterFolder(),
        TonesRow => Tones(),
        ManagePhraseFilesRow => Ghost(L10n.T("Manage Custom Phrase Files..."), _openPhraseFiles),
        DeviceRow => new DeviceTab(_form.Device!, _host),
        _ => new Control(),
    };

    // A label on the left, with a line under it saying more, if there is one.
    private static HBoxContainer Caption(string label, string hint = "", Control? before = null)
    {
        var row = new HBoxContainer { CustomMinimumSize = new Vector2(170, 0), SizeFlagsHorizontal = SizeFlags.Fill };
        row.AddThemeConstantOverride("separation", 10);
        if (before is not null) row.AddChild(before);
        var words = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        words.AddThemeConstantOverride("separation", 0);
        words.AddChild(new Label { Text = Plain(label), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        if (hint.Length > 0) words.AddChild(new Label { Text = hint, ThemeTypeVariation = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        row.AddChild(words);
        return row;
    }

    // A label written for a form row ("Toy colour:") as a caption.
    private static string Plain(string label) => label.TrimEnd().TrimEnd(':').TrimEnd();

    private static HBoxContainer Line(Control caption, params Control[] rest)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        caption.SizeFlagsStretchRatio = 0.55f;
        caption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(caption);
        foreach (var control in rest) row.AddChild(control);
        return row;
    }

    // A number: its slider and its value, which can be typed; with the switch of what it is the
    // chance of beside it, if it has one.
    private HBoxContainer Number(NumberRow row)
    {
        var setting = _form.Numbers[row.Name];
        var slider = new SliderBar(setting.Slider, setting.Value);
        var badge = new ValueBadge(setting.Slider, range: false);
        badge.Show(setting.Value, setting.Value);
        slider.Changed += (value, _) =>
        {
            setting.Value = value;
            badge.Show(setting.Value, setting.Value);
        };
        badge.Typed += (value, _) =>
        {
            setting.Value = value;
            slider.Set(setting.Value, setting.Value);
            badge.Show(setting.Value, setting.Value);
        };
        ToggleSwitch? on = null;
        if (row.Switch is { } name)
        {
            var toggle = _form.Toggles[name];
            on = new ToggleSwitch(toggle.Value) { TooltipText = toggle.Label };
            on.Toggled += value =>
            {
                toggle.Value = value;
                slider.Enabled = badge.Enabled = value;
                slider.Modulate = badge.Modulate = new Color(1, 1, 1, value ? 1 : 0.4f);
            };
            slider.Enabled = badge.Enabled = toggle.Value;
            slider.Modulate = badge.Modulate = new Color(1, 1, 1, toggle.Value ? 1 : 0.4f);
        }
        return Line(Caption(setting.Label, before: on), slider, badge);
    }

    // A minimum and its maximum: one slider, two handles.
    private HBoxContainer Range(RangeRow row)
    {
        var (low, high) = (_form.Numbers[row.Min], _form.Numbers[row.Max]);
        var slider = new SliderBar(low.Slider, low.Value, high.Value);
        var badge = new ValueBadge(low.Slider, range: true);
        badge.Show(low.Value, high.Value);
        void Take(double a, double b)
        {
            low.Value = a;
            high.Value = b;
            badge.Show(low.Value, high.Value);
        }
        slider.Changed += Take;
        badge.Typed += (a, b) =>
        {
            Take(a, b);
            slider.Set(low.Value, high.Value);
        };
        return Line(Caption(row.Label, row.Hint), slider, badge);
    }

    private static HBoxContainer Toggle(ToggleSetting setting)
    {
        var on = new ToggleSwitch(setting.Value);
        on.Toggled += value => setting.Value = value;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        var caption = Caption(setting.Label, setting.Tooltip);
        caption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(caption);
        row.AddChild(on);
        return row;
    }

    private Control Choice(ChoiceSetting setting)
    {
        var options = setting.Options.Select(o => (o.Value, o.Label)).ToList();
        void Pick(string value) => _form.Choices[setting.Name].Value = value;
        switch (setting.Style)
        {
            case ChoiceStyle.List:
                var list = new OptionButton { FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill };
                foreach (var option in setting.Options) list.AddItem(option.Label);
                list.Select(setting.Options.ToList().FindIndex(o => o.Value == setting.Value));
                // Looked up when picked: the form may have replaced the setting with a longer list since.
                var name = setting.Name;
                list.ItemSelected += index => _form.Choices[name].Value = _form.Choices[name].Options[(int)index].Value;
                return Line(Caption(setting.Label), list);
            case ChoiceStyle.Tiles:
                return Stacked(setting.Label, new Tiles(setting.Name, options, setting.Value, Pick));
            case ChoiceStyle.Swatches:
                return Stacked(setting.Label, new SwatchRow(options, setting.Value, Pick));
            default:
                return Line(Caption(setting.Label), new Segments(options, setting.Value, Pick));
        }
    }

    // A caption over what it captions, for what needs the whole width.
    private VBoxContainer Stacked(string label, Control content)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        if (!string.Equals(Plain(label), _section, StringComparison.OrdinalIgnoreCase)) column.AddChild(new Label { Text = Plain(label) });
        column.AddChild(content);
        return column;
    }

    private static Button Ghost(string text, Action pressed)
    {
        var button = new Button { Text = text, ThemeTypeVariation = "Ghost", FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        button.Pressed += pressed;
        return button;
    }

    // The app's own language, each in its own name.
    private Segments UiLanguage() =>
        new Segments([.. _form.UiLanguages.Select(o => (o.Value, o.Label))], _form.UiLanguage, value => _form.UiLanguage = value);

    // The callouts' language, each in its own name; the tones it lacks grey out at once.
    private HBoxContainer Language()
    {
        var languages = _form.Languages.Select(lang => (lang, Catalogue.NativeName(lang))).ToList();
        void Pick(string lang)
        {
            _form.Language = lang;
            foreach (var refresh in _refreshTones) refresh();
        }
        if (languages.Count <= 4) return Line(Caption(L10n.T("Language")), new Segments(languages, _form.Language, Pick));
        var list = new OptionButton { FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var (_, label) in languages) list.AddItem(label);
        list.Select(_form.Languages.ToList().IndexOf(_form.Language));
        list.ItemSelected += index => Pick(_form.Languages[(int)index]);
        return Line(Caption(L10n.T("Language")), list);
    }

    // Tones as chips. One the language lacks is greyed out, not unticked: it keeps its place in the
    // saved mix, for when the user switches back.
    private VBoxContainer Tones()
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        var chips = new HFlowContainer();
        chips.AddThemeConstantOverride("h_separation", 8);
        chips.AddThemeConstantOverride("v_separation", 8);
        var buttons = new Dictionary<string, Button>();
        foreach (var (tone, ticked) in _form.Tones)
        {
            var chip = new Button { Text = CalloutHandler.ToneLabel(tone), ThemeTypeVariation = "Chip", ToggleMode = true, FocusMode = FocusModeEnum.None };
            chip.SetPressedNoSignal(ticked);
            chip.Toggled += on => _form.SetTone(tone, on);
            chips.AddChild(chip);
            buttons[tone] = chip;
        }
        column.AddChild(chips);
        var hint = new Label { ThemeTypeVariation = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(hint);
        void Refresh()
        {
            foreach (var (tone, chip) in buttons)
            {
                chip.Disabled = !_form.ToneUsable(tone);
                chip.TooltipText = _form.ToneTooltip(tone);
            }
            hint.Text = _form.ToneHint;
            hint.Visible = hint.Text.Length > 0;
        }
        _refreshTones.Add(Refresh);
        Refresh();
        return column;
    }

    // Rhythms as chips, each with its steps as dots: a beat full, a silent step hollow, the shorter
    // the smaller.
    private HFlowContainer Rhythms()
    {
        var chips = new HFlowContainer();
        chips.AddThemeConstantOverride("h_separation", 8);
        chips.AddThemeConstantOverride("v_separation", 8);
        foreach (var (name, ticked) in _form.Rhythms)
        {
            var chip = new Button
            {
                Text = BeatPatterns.DisplayName(name),
                Icon = Dots(_form.Pattern(name)),
                IconAlignment = HorizontalAlignment.Right,
                ThemeTypeVariation = "Chip",
                ToggleMode = true,
                FocusMode = FocusModeEnum.None,
            };
            chip.SetPressedNoSignal(ticked);
            chip.Toggled += on => _form.SetRhythm(name, on);
            chips.AddChild(chip);
        }
        return chips;
    }

    private static ImageTexture? Dots(IReadOnlyList<int> steps)
    {
        if (steps.Count == 0) return null;
        const int height = 12;
        var sizes = steps.Select(step => 4 + (4 - Math.Min(4, Math.Abs(step))) * 2).ToList();
        var width = sizes.Sum() + 3 * (sizes.Count - 1);
        var image = Image.CreateEmpty(Math.Max(1, width), height, false, Image.Format.Rgba8);
        var colour = UiTheme.Of(Palette.Text);
        var x = 0;
        for (var i = 0; i < steps.Count; i++)
        {
            var d = sizes[i];
            var centre = new Vector2(x + d / 2f, height / 2f);
            for (var py = 0; py < height; py++)
            {
                for (var px = x; px < x + d; px++)
                {
                    var r = new Vector2(px + 0.5f, py + 0.5f).DistanceTo(centre);
                    var inside = Math.Clamp(d / 2f - r + 0.5f, 0, 1);
                    var ring = steps[i] > 0 ? inside : inside * Math.Clamp(r - (d / 2f - 1.6f) + 0.5f, 0, 1) * 0.6f;
                    if (ring > 0) image.SetPixel(px, py, new Color(colour, ring));
                }
            }
            x += d + 3;
        }
        return ImageTexture.CreateFromImage(image);
    }

    // After the pattern editor: its patterns appear, deleted ones go.
    private void Refresh()
    {
        _form.RefreshRhythms();
        Build();
    }

    // The folder of the user's own models: its name, a native folder dialog to pick it, and a way to
    // forget it. The list of characters takes in its models at once.
    private HBoxContainer CharacterFolder()
    {
        var right = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 8);
        var shown = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, ThemeTypeVariation = "Hint" };
        var forget = new Button { Text = L10n.T("Forget"), ThemeTypeVariation = "Link", FocusMode = FocusModeEnum.None };
        void Show()
        {
            // The folder's own name only: enough to recognise it, and no path on screen for a stray screenshot.
            shown.Text = _form.CharacterFolder is { } folder ? System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(folder)) : L10n.T("no folder chosen");
            shown.TooltipText = _form.CharacterFolder ?? "";
            forget.Visible = _form.CharacterFolder is not null;
        }
        void Use(string? folder)
        {
            _form.SetCharacterFolder(folder);
            Build();
        }
        var chooser = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
            Title = L10n.T("Folder of your own .vrm models"),
        };
        chooser.DirSelected += folder => Use(folder);
        right.AddChild(chooser);
        var choose = Ghost(L10n.T("Choose Folder..."), () => chooser.PopupCentered(new Vector2I(900, 600)));
        forget.Pressed += () => Use(null);
        right.AddChild(shown);
        right.AddChild(forget);
        right.AddChild(choose);
        Show();
        return Line(Caption(L10n.T("Your own models:")), right);
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
