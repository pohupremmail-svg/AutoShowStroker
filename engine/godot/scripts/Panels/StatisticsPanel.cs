using System;
using System.Linq;
using Gooner.Core.App;
using Gooner.Core.Panels;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine.Panels;

// StatisticsDialog: the recap after a session - what Gooner.Core.Panels.StatisticsReport says, laid
// out as the dialog laid it out. The Session Explorer is reached from here, and is where the
// session can be saved, as on the Python side.
public partial class StatisticsPanel : AppPanel
{
    public StatisticsPanel(StatisticsReport report, Action? openExplorer)
        : base("Session Statistics", new Vector2(560, 760), scrolls: true)
    {
        Body.AddChild(Widgets.Text(report.Title, 24, Palette.Accent, bold: true));
        Body.AddChild(Widgets.Text(report.Conclusion, 14, italic: true, wrap: true));

        foreach (var record in report.RecordCards)
        {
            var (card, column) = Widgets.Card(UiTheme.Of(Palette.Accent), margin: 12);
            column.AddChild(Widgets.Text(record.Title, 14, Palette.Background, bold: true));
            column.AddChild(Widgets.Text(record.Value, 18, Palette.Background, bold: true));
            column.AddChild(Widgets.Text(record.Previous, 11, Palette.Background));
            Body.AddChild(card);
        }
        // Same shape as a personal-record card, one shade quieter: a record is you beating
        // yourself, an achievement a thing the app was holding out on you.
        foreach (var achievement in report.AchievementCards)
        {
            var (card, column) = Widgets.Card(UiTheme.Of(Palette.SurfaceDark), margin: 12, border: UiTheme.Of(Palette.Accent), borderWidth: 2);
            column.AddChild(Widgets.Text(achievement.Title, 14, Palette.Accent, bold: true));
            column.AddChild(Widgets.Text(achievement.Description, 11, Palette.Text, wrap: true));
            Body.AddChild(card);
        }

        Body.AddChild(Widgets.Table(["metric", "value"], report.Rows.Select(row => new[] { row.Metric, row.Value }),
            [HorizontalAlignment.Left, HorizontalAlignment.Center]));

        if (report.HasExplorer && openExplorer is not null)
        {
            var explorer = AddButton("Session Explorer", openExplorer);
            explorer.TooltipText = "Scroll back through this session - every rhythm, and what was on screen during it";
        }
        AddButton("Close", Close, primary: true);
    }
}
