using Gooner.Core.App;
using Gooner.Core.Panels;
using Godot;

namespace Gooner.Engine.Panels;

// HelpDialog: the Guide, a tab per topic, each page scrolling on its own.
public partial class GuidePanel : AppPanel
{
    public GuidePanel()
        : base("Guide", new Vector2(560, 520))
    {
        var tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        foreach (var topic in Guide.Topics)
        {
            var text = new RichTextLabel
            {
                Name = topic.Title,
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
            var page = new MarginContainer { Name = topic.Title };
            foreach (var side in new[] { "left", "right", "top", "bottom" })
                page.AddThemeConstantOverride($"margin_{side}", 10);
            text.Name = "Text";
            page.AddChild(text);
            tabs.AddChild(page);
        }
        Body.AddChild(tabs);
        AddButton("Close", Close, primary: true);
    }
}
