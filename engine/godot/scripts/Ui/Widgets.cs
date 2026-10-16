using System.Collections.Generic;
using Gooner.Core.App;
using Godot;

namespace Gooner.Engine.Ui;

// Pieces several panels are built from, styled as the Qt stylesheet styled their counterparts.
public static class Widgets
{
    // A QTableWidget as the app styled it: a rounded dark frame, a header row in the accent,
    // alternating row colours, no grid. Read-only - none of the app's tables is edited in place.
    public static PanelContainer Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows,
        IReadOnlyList<HorizontalAlignment>? align = null)
    {
        var frame = new PanelContainer();
        var box = UiTheme.Box(UiTheme.Of(Palette.SurfaceDark), 10);
        box.SetBorderWidthAll(1);
        box.BorderColor = UiTheme.Of(Palette.Secondary);
        box.SetContentMarginAll(1);
        frame.AddThemeStyleboxOverride("panel", box);
        var grid = new GridContainer { Columns = headers.Count };
        grid.AddThemeConstantOverride("h_separation", 0);
        grid.AddThemeConstantOverride("v_separation", 0);
        frame.AddChild(grid);

        for (var column = 0; column < headers.Count; column++)
        {
            var corner = column == 0 ? 10 : 0;
            var lastCorner = column == headers.Count - 1 ? 10 : 0;
            var header = UiTheme.Box(UiTheme.Of(Palette.Surface), 0);
            header.CornerRadiusTopLeft = corner;
            header.CornerRadiusTopRight = lastCorner;
            grid.AddChild(Cell(headers[column], header, HorizontalAlignment.Left, bold: true, colour: Palette.Accent, expand: column == headers.Count - 1));
        }
        var index = 0;
        foreach (var row in rows)
        {
            var background = UiTheme.Box(UiTheme.Of(index++ % 2 == 1 ? Palette.Surface : Palette.SurfaceDark), 0);
            for (var column = 0; column < row.Count; column++)
            {
                var alignment = align is not null && column < align.Count ? align[column] : HorizontalAlignment.Left;
                grid.AddChild(Cell(row[column], background, alignment, bold: false, colour: Palette.Text, expand: column == row.Count - 1));
            }
        }
        return frame;
    }

    private static PanelContainer Cell(string text, StyleBoxFlat background, HorizontalAlignment alignment, bool bold, string colour, bool expand)
    {
        var cell = new PanelContainer { SizeFlagsHorizontal = expand ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill };
        var padded = (StyleBoxFlat)background.Duplicate();
        padded.ContentMarginLeft = padded.ContentMarginRight = 12;
        padded.ContentMarginTop = padded.ContentMarginBottom = bold ? 8 : 6;
        cell.AddThemeStyleboxOverride("panel", padded);
        var label = new Label { Text = text, HorizontalAlignment = alignment };
        label.AddThemeColorOverride("font_color", UiTheme.Of(colour));
        if (bold) label.AddThemeFontOverride("font", UiTheme.Bold);
        cell.AddChild(label);
        return cell;
    }

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

    // A rounded card holding a column of whatever is put in it.
    public static (PanelContainer Card, VBoxContainer Column) Card(Color background, int radius = 8, int margin = 12, Color? border = null, int borderWidth = 0)
    {
        var card = new PanelContainer();
        var box = UiTheme.Box(background, radius, margin);
        if (border is { } colour)
        {
            box.SetBorderWidthAll(borderWidth);
            box.BorderColor = colour;
        }
        card.AddThemeStyleboxOverride("panel", box);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 4);
        card.AddChild(column);
        return (card, column);
    }
}
