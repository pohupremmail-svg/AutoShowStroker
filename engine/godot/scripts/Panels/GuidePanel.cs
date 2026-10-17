using Gooner.Core.App;
using Gooner.Core.Localization;
using Gooner.Core.Panels;
using Godot;

namespace Gooner.Engine.Panels;

// HelpDialog: the Guide, a tab per topic, each page scrolling on its own.
public partial class GuidePanel : AppPanel
{
    public GuidePanel()
        : base(L10n.T("Guide"), new Vector2(560, 520))
    {
        var tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        var topics = Guide.Translated();
        for (var i = 0; i < topics.Count; i++)
        {
            var topic = topics[i];
            var text = new RichTextLabel
            {
                BbcodeEnabled = true,
                Text = RichText.ToBbcode(topic.Html, headingSize: 16),
                SelectionEnabled = true,
                FocusMode = FocusModeEnum.None,
            };
            text.AddThemeFontSizeOverride("normal_font_size", 13);
            text.AddThemeFontSizeOverride("bold_font_size", 13);
            text.AddThemeFontSizeOverride("italics_font_size", 13);
            text.AddThemeFontSizeOverride("mono_font_size", 12);
            text.AddThemeConstantOverride("paragraph_separation", 6);
            var page = new MarginContainer();
            foreach (var side in new[] { "left", "right", "top", "bottom" })
                page.AddThemeConstantOverride($"margin_{side}", 10);
            text.Name = "Text";
            page.AddChild(text);
            tabs.AddChild(page);
            // Not the node's name: a title may hold what a node name cannot.
            tabs.SetTabTitle(i, topic.Title);
        }
        Body.AddChild(tabs);
        AddButton(L10n.T("Close"), Close, primary: true);
    }
}
