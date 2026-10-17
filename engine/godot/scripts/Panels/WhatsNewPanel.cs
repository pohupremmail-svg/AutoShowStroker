using System.Collections.Generic;
using System.Linq;
using Gooner.Core.App;
using Gooner.Core.Localization;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine.Panels;

// WhatsNewDialog: one card per version, newest first, each with its version badge and the entry's
// rich text.
public partial class WhatsNewPanel : AppPanel
{
    public WhatsNewPanel(IEnumerable<ChangelogEntry> entries)
        : base(L10n.T("What's new"), new Vector2(520, 480), scrolls: true)
    {
        Body.AddThemeConstantOverride("separation", 14);
        foreach (var entry in entries.Reverse())
            Body.AddChild(Card(entry));
        AddButton(L10n.T("Got it"), Close, primary: true);
    }

    // Qt drew the card's left edge in the accent and the rest in the secondary colour. A StyleBoxFlat
    // has one border colour, so the accent edge is the card's frame showing past the inner box.
    private static PanelContainer Card(ChangelogEntry entry)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var edge = UiTheme.Box(UiTheme.Of(Palette.Accent), 8);
        edge.ContentMarginLeft = 4;
        card.AddThemeStyleboxOverride("panel", edge);
        var inner = new PanelContainer();
        var frame = UiTheme.Box(UiTheme.Of(Palette.Surface), 0);
        frame.CornerRadiusTopRight = frame.CornerRadiusBottomRight = 8;
        frame.BorderWidthTop = frame.BorderWidthRight = frame.BorderWidthBottom = 1;
        frame.BorderColor = UiTheme.Of(Palette.Secondary);
        frame.ContentMarginLeft = frame.ContentMarginRight = 14;
        frame.ContentMarginTop = 12;
        frame.ContentMarginBottom = 14;
        inner.AddThemeStyleboxOverride("panel", frame);
        card.AddChild(inner);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        inner.AddChild(column);

        var badgeRow = new HBoxContainer();
        var badge = new PanelContainer();
        var badgeBox = UiTheme.Box(UiTheme.Of(Palette.Accent), 8);
        badgeBox.ContentMarginLeft = badgeBox.ContentMarginRight = 10;
        badgeBox.ContentMarginTop = badgeBox.ContentMarginBottom = 3;
        badge.AddThemeStyleboxOverride("panel", badgeBox);
        var version = new Label { Text = L10n.F("Version {0}", entry.Version) };
        version.AddThemeFontOverride("font", UiTheme.Bold);
        version.AddThemeFontSizeOverride("font_size", 12);
        version.AddThemeColorOverride("font_color", UiTheme.Of(Palette.Background));
        badge.AddChild(version);
        badgeRow.AddChild(badge);
        column.AddChild(badgeRow);

        var text = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            // Each entry translated as a whole: it is written in the app's voice, not sentence by sentence.
            Text = RichText.ToBbcode(L10n.T(entry.Html)),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        text.AddThemeFontSizeOverride("normal_font_size", 13);
        text.AddThemeFontSizeOverride("bold_font_size", 13);
        text.AddThemeFontSizeOverride("italics_font_size", 13);
        text.AddThemeFontSizeOverride("mono_font_size", 12);
        column.AddChild(text);

        return card;
    }
}
