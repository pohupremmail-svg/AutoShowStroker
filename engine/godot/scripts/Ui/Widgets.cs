using System;
using Gooner.Core.App;
using Godot;

namespace Gooner.Engine.Ui;

// Pieces every panel is built from, in the redesign's look (the user's pick of the settings mock,
// 2026-10-24, for every window): sections as cards of glass, dark wells, tiles of numbers, hints.
public static class Widgets
{
    // A section: a card with its title, a line saying what it is for, and whatever goes on the right of
    // the title (a switch, a button); its rows go in the column returned. `variation` "LitSection" for
    // one that stands out.
    public static (PanelContainer Card, VBoxContainer Rows) Section(string? title = null, string? hint = null, Control? trailing = null,
        string variation = "Section", int separation = 10)
    {
        var card = new PanelContainer { ThemeTypeVariation = variation, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", separation);
        card.AddChild(column);
        if (title is not null || trailing is not null)
        {
            var head = new HBoxContainer();
            head.AddThemeConstantOverride("separation", 12);
            var words = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
            words.AddThemeConstantOverride("separation", 2);
            if (title is not null) words.AddChild(new Label { Text = title, ThemeTypeVariation = "SectionTitle", AutowrapMode = TextServer.AutowrapMode.WordSmart });
            if (!string.IsNullOrEmpty(hint)) words.AddChild(Hint(hint));
            head.AddChild(words);
            if (trailing is not null)
            {
                trailing.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                head.AddChild(trailing);
            }
            column.AddChild(head);
        }
        return (card, column);
    }

    // A small grey line saying more.
    public static Label Hint(string text) =>
        new() { Text = text, ThemeTypeVariation = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

    // A number in a dark tile, what it is above it; a grid of them in place of a table.
    public static PanelContainer Tile(string label, string value)
    {
        var tile = new PanelContainer { ThemeTypeVariation = "Well", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 2);
        column.AddChild(new Label { Text = label, ThemeTypeVariation = "Hint", TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, TooltipText = label, MouseFilter = Control.MouseFilterEnum.Pass });
        column.AddChild(new Label { Text = value, ThemeTypeVariation = "TileValue", TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
        tile.AddChild(column);
        return tile;
    }

    public static GridContainer Tiles(int columns)
    {
        var grid = new GridContainer { Columns = columns, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 10);
        return grid;
    }

    // A small lit label: a version, a count.
    public static PanelContainer Pill(string text)
    {
        var pill = new PanelContainer { ThemeTypeVariation = "Pill", SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        pill.AddChild(new Label { Text = text, ThemeTypeVariation = "PillText" });
        return pill;
    }

    // A button in one of the kit's styles: "" the quiet one, "Danger" for what deletes, "PrimaryButton".
    public static Button Button(string text, Action pressed, string variation = "")
    {
        var button = new Button { Text = text, ThemeTypeVariation = variation, FocusMode = Control.FocusModeEnum.None };
        button.Pressed += pressed;
        return button;
    }

    // A row of buttons, left to right.
    public static HBoxContainer Row(params Control[] controls)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        foreach (var control in controls) row.AddChild(control);
        return row;
    }

    public static Control Spacer() => new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

    // A label in one of the app's text styles.
    public static Label Text(string text, int size, string? colour = null, bool bold = false, bool italic = false, bool wrap = false)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        if (colour is not null) label.AddThemeColorOverride("font_color", UiTheme.Of(colour));
        if (bold) label.AddThemeFontOverride("font", UiTheme.Bold);
        else if (italic) label.AddThemeFontOverride("font", UiTheme.Italic);
        if (wrap)
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        }
        return label;
    }

    // Text set in the app's display face, as a page's title is.
    public static Label Display(string text, int size, string? colour = null)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        label.AddThemeFontOverride("font", UiTheme.Display);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", colour is null ? Colors.White : UiTheme.Of(colour));
        return label;
    }
}
