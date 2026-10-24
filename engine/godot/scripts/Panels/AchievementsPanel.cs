using System.IO;
using System.Linq;
using Gooner.Core.App;
using Gooner.Core.Localization;
using Gooner.Core.Panels;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine.Panels;

// AchievementsDialog: every tile of Gooner.Core.Panels.AchievementsOverview, two to a row - a section
// card each, lit in the accent once anything of it is earned.
public partial class AchievementsPanel : AppPanel
{
    private const int MarkSize = 48;
    private const int GlowSpread = 12;
    private const string LockedColour = "#6a6175";

    public AchievementsPanel(AchievementsOverview overview, string root)
        : base(L10n.T("Achievements"), new Vector2(860, 640))
    {
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 14);
        head.AddChild(new Label { Text = overview.Summary, ThemeTypeVariation = "SectionTitle" });
        var earned = new ProgressBar
        {
            MaxValue = overview.Tiles.Count,
            Value = overview.Tiles.Count(tile => tile.Unlocked),
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 8),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        head.AddChild(earned);
        Body.AddChild(head);
        var grid = Widgets.Tiles(2);
        foreach (var tile in overview.Tiles)
            grid.AddChild(Tile(tile, root));
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var inset = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        inset.AddThemeConstantOverride("margin_right", 12);
        inset.AddChild(grid);
        scroll.AddChild(inset);
        Body.AddChild(scroll);
        AddButton(L10n.T("Close"), Close, primary: true);
    }

    private static PanelContainer Tile(AchievementTile tile, string root)
    {
        var (card, column) = Widgets.Section(variation: tile.Unlocked ? "LitSection" : "Section", separation: 3);
        card.RemoveChild(column);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        card.AddChild(row);
        row.AddChild(Mark(Path.Combine(root, tile.IconPath), tile.Unlocked));
        column.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(column);

        var heading = new HBoxContainer();
        var title = new Label { Text = tile.Title, ThemeTypeVariation = "SectionTitle" };
        if (!tile.Unlocked) title.AddThemeColorOverride("font_color", UiTheme.Of(Palette.Text, 0.75f));
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        heading.AddChild(title);
        if (tile.Pips is { } pips)
        {
            // One dot per level, filled for the ones earned: a track's shape at a glance.
            var dots = Widgets.Text(string.Join(" ", pips.ToCharArray()), 13, tile.Unlocked ? Palette.Accent : LockedColour);
            heading.AddChild(dots);
        }
        column.AddChild(heading);
        column.AddChild(Widgets.Text(tile.Detail, 13, Palette.Text, wrap: true));
        if (tile.Status.Length > 0)
            column.AddChild(Widgets.Hint(tile.Status));
        if (tile.Progress is { } progress)
        {
            column.AddChild(new ProgressBar
            {
                MaxValue = progress.Target,
                Value = progress.Current,
                ShowPercentage = false,
                CustomMinimumSize = new Vector2(0, 6),
            });
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
