using System;
using Gooner.Core.App;
using Gooner.Core.Localization;
using Gooner.Core.Panels;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine.Panels;

// StatisticsDialog: the recap after a session - what Gooner.Core.Panels.StatisticsReport says, in the
// redesign's look: the outcome in the display face, a record broken lit up, every number a tile. The
// Session Explorer is reached from here, and is where the session can be saved, as on the Python side.
public partial class StatisticsPanel : AppPanel
{
    public StatisticsPanel(StatisticsReport report, Action? openExplorer)
        : base(L10n.T("Session Statistics"), new Vector2(760, 760), scrolls: true)
    {
        Body.AddThemeConstantOverride("separation", 12);
        Body.AddChild(Widgets.Display(report.Title, 28, Palette.Accent));
        Body.AddChild(Widgets.Text(report.Conclusion, 14, italic: true, wrap: true));

        // A record is you beating yourself: lit. An achievement is a thing the app was holding out on
        // you: the same card, one shade quieter.
        if (report.RecordCards.Count > 0)
        {
            var records = Widgets.Tiles(Math.Min(2, report.RecordCards.Count));
            foreach (var record in report.RecordCards)
            {
                var (card, column) = Widgets.Section(variation: "LitSection", separation: 2);
                column.AddChild(new Label { Text = record.Title, ThemeTypeVariation = "SectionTitle", AutowrapMode = TextServer.AutowrapMode.WordSmart });
                var value = new Label { Text = record.Value, ThemeTypeVariation = "TileValue" };
                value.AddThemeFontSizeOverride("font_size", 24);
                column.AddChild(value);
                column.AddChild(Widgets.Hint(record.Previous));
                records.AddChild(card);
            }
            Body.AddChild(records);
        }
        foreach (var achievement in report.AchievementCards)
        {
            var (card, column) = Widgets.Section(separation: 2);
            column.AddChild(Widgets.Text(achievement.Title, 15, Palette.Accent, bold: true, wrap: true));
            column.AddChild(Widgets.Hint(achievement.Description));
            Body.AddChild(card);
        }

        var (numbers, rows) = Widgets.Section(L10n.T("This session"));
        var tiles = Widgets.Tiles(3);
        foreach (var row in report.Rows) tiles.AddChild(Widgets.Tile(row.Metric, row.Value));
        rows.AddChild(tiles);
        Body.AddChild(numbers);

        if (report.HasExplorer && openExplorer is not null)
        {
            var explorer = AddButton(L10n.T("Session Explorer"), openExplorer);
            explorer.TooltipText = L10n.T("Scroll back through this session - every rhythm, and what was on screen during it");
        }
        AddButton(L10n.T("Close"), Close, primary: true);
    }
}
