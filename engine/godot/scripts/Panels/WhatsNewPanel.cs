using System.Collections.Generic;
using System.Linq;
using Gooner.Core.App;
using Gooner.Core.Localization;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine.Panels;

// WhatsNewDialog: one card per version, newest first, each with its version badge and the entry's
// rich text, in the redesign's sections.
public partial class WhatsNewPanel : AppPanel
{
    public WhatsNewPanel(IEnumerable<ChangelogEntry> entries)
        : base(L10n.T("What's new"), new Vector2(620, 600), scrolls: true)
    {
        Body.AddThemeConstantOverride("separation", 12);
        foreach (var entry in entries.Reverse())
            Body.AddChild(Card(entry));
        AddButton(L10n.T("Got it"), Close, primary: true);
    }

    // A section card of the redesign, the version a lit pill on top.
    private static PanelContainer Card(ChangelogEntry entry)
    {
        var (card, column) = Widgets.Section(separation: 8);
        column.AddChild(Widgets.Pill(L10n.F("Version {0}", entry.Version)));
        var text = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            // Each entry translated as a whole: it is written in the app's voice, not sentence by sentence.
            Text = RichText.ToBbcode(L10n.T(entry.Html)),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        text.AddThemeFontSizeOverride("normal_font_size", 14);
        text.AddThemeFontSizeOverride("bold_font_size", 14);
        text.AddThemeFontSizeOverride("italics_font_size", 14);
        text.AddThemeFontSizeOverride("mono_font_size", 13);
        text.AddThemeConstantOverride("paragraph_separation", 4);
        column.AddChild(text);
        return card;
    }
}
