using System.IO;
using Gooner.Core.App;
using Gooner.Core.Localization;
using Gooner.Core.Panels;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine.Panels;

// AchievementsDialog: every tile of Gooner.Core.Panels.AchievementsOverview, two to a row.
public partial class AchievementsPanel : AppPanel
{
    private const int MarkSize = 48;
    private const int GlowSpread = 12;
    private const string LockedColour = "#6a6175";

    public AchievementsPanel(AchievementsOverview overview, string root)
        : base(L10n.T("Achievements"), new Vector2(820, 600))
    {
        Body.AddChild(Widgets.Text(overview.Summary, 15, Palette.Text));
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        foreach (var tile in overview.Tiles)
            grid.AddChild(Tile(tile, root));
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        scroll.AddChild(grid);
        Body.AddChild(scroll);
        AddButton(L10n.T("Close"), Close, primary: true);
    }

    private static PanelContainer Tile(AchievementTile tile, string root)
    {
        var (card, column) = Widgets.Card(UiTheme.Of(Palette.SurfaceDark), radius: 10, margin: 10);
        card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        card.RemoveChild(column);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        card.AddChild(row);
        row.AddChild(Mark(Path.Combine(root, tile.IconPath), tile.Unlocked));
        column.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(column);

        var heading = new HBoxContainer();
        var title = Widgets.Text(tile.Title, 14, tile.Unlocked ? Palette.Accent : Palette.Text, bold: true);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        heading.AddChild(title);
        if (tile.Pips is { } pips)
        {
            // One dot per level, filled for the ones earned: a track's shape at a glance.
            var dots = Widgets.Text(string.Join(" ", pips.ToCharArray()), 13, tile.Unlocked ? Palette.Accent : LockedColour);
            heading.AddChild(dots);
        }
        column.AddChild(heading);
        column.AddChild(Widgets.Text(tile.Detail, 12, Palette.Text, wrap: true));
        if (tile.Status.Length > 0)
            column.AddChild(Widgets.Text(tile.Status, 11, LockedColour));
        if (tile.Progress is { } progress)
        {
            var bar = new ProgressBar
            {
                MaxValue = progress.Target,
                Value = progress.Current,
                ShowPercentage = false,
                CustomMinimumSize = new Vector2(0, 6),
            };
            // Qt's default chunk was a flat green that belonged to another application.
            bar.AddThemeStyleboxOverride("background", UiTheme.Box(UiTheme.Of(Palette.SurfaceDarkest), 3));
            bar.AddThemeStyleboxOverride("fill", UiTheme.Box(UiTheme.Of(Palette.Accent), 3));
            column.AddChild(bar);
        }
        return card;
    }

    // The mark in the accent with its glow once anything of it is earned, grey before.
    private static Control Mark(string path, bool lit)
    {
        var holder = new Control { CustomMinimumSize = new Vector2(MarkSize, MarkSize), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        if (Marks.Tinted(path, UiTheme.Of(lit ? Palette.Accent : LockedColour), MarkSize) is not { } mark) return holder;
        if (lit)
        {
            holder.AddChild(new TextureRect
            {
                Texture = ImageTexture.CreateFromImage(Marks.Glow(mark, GlowSpread)),
                Position = new Vector2(-GlowSpread, -GlowSpread),
                MouseFilter = MouseFilterEnum.Ignore,
            });
        }
        holder.AddChild(new TextureRect { Texture = ImageTexture.CreateFromImage(mark), MouseFilter = MouseFilterEnum.Ignore });
        return holder;
    }
}
