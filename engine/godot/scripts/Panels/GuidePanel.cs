using System.Collections.Generic;
using Gooner.Core.App;
using Gooner.Core.Localization;
using Gooner.Core.Panels;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine.Panels;

// HelpDialog: the Guide, laid out as the settings are (the redesign) - a topic per entry of a side
// navigation, its page a card of text scrolling on its own.
public partial class GuidePanel : AppPanel
{
    // By the topic's English title, which is what Guide.Topics holds.
    private static readonly Dictionary<string, string> TopicIcons = new()
    {
        ["Beats & Rhythm"] = "rhythm",
        ["On-Screen Display"] = "look",
        ["Characters"] = "person",
        ["Languages & Tones"] = "globe",
        ["Keyboard Shortcuts"] = "keyboard",
        ["Privacy"] = "lock",
    };

    private readonly List<Button> _nav = [];
    private readonly List<Control> _pages = [];

    public GuidePanel()
        : base(L10n.T("Guide"), new Vector2(960, 660), bodyScrolls: false)
    {
        var row = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 18);
        var nav = new VBoxContainer { CustomMinimumSize = new Vector2(210, 0) };
        nav.AddThemeConstantOverride("separation", 4);
        var pages = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        var group = new ButtonGroup();
        var topics = Guide.Translated();
        for (var i = 0; i < topics.Count; i++)
        {
            var index = i;
            var button = new Button
            {
                Text = topics[i].Title,
                Icon = Icons.Get(TopicIcons.GetValueOrDefault(Guide.Topics[i].Title, "book"), 20),
                ThemeTypeVariation = "NavButton",
                ToggleMode = true,
                ButtonGroup = group,
                FocusMode = FocusModeEnum.None,
                Alignment = HorizontalAlignment.Left,
            };
            button.Pressed += () => Show(index);
            nav.AddChild(button);
            _nav.Add(button);
            var page = Page(topics[i]);
            pages.AddChild(page);
            _pages.Add(page);
        }
        row.AddChild(nav);
        row.AddChild(pages);
        Body.AddChild(row);
        AddButton(L10n.T("Close"), Close, primary: true);
        Show(0);
    }

    private void Show(int index)
    {
        for (var i = 0; i < _pages.Count; i++)
        {
            _pages[i].Visible = i == index;
            _nav[i].SetPressedNoSignal(i == index);
        }
    }

    private static VBoxContainer Page(GuideTopic topic)
    {
        var page = new VBoxContainer();
        page.SetAnchorsPreset(LayoutPreset.FullRect);
        page.AddThemeConstantOverride("separation", 12);
        var title = new Label { Text = topic.Title, ThemeTypeVariation = "PageTitle" };
        title.AddThemeFontOverride("font", UiTheme.Display);
        page.AddChild(title);
        var card = new PanelContainer { ThemeTypeVariation = "Section", SizeFlagsVertical = SizeFlags.ExpandFill };
        var text = new RichTextLabel
        {
            BbcodeEnabled = true,
            Text = RichText.ToBbcode(topic.Html, headingSize: 17),
            SelectionEnabled = true,
            FocusMode = FocusModeEnum.None,
            Name = "Text",
        };
        text.AddThemeFontSizeOverride("normal_font_size", 14);
        text.AddThemeFontSizeOverride("bold_font_size", 14);
        text.AddThemeFontSizeOverride("italics_font_size", 14);
        text.AddThemeFontSizeOverride("mono_font_size", 13);
        text.AddThemeConstantOverride("paragraph_separation", 8);
        card.AddChild(text);
        page.AddChild(card);
        return page;
    }
}
