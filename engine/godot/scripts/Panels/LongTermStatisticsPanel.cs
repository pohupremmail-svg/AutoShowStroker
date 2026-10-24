using Gooner.Core.Localization;
using Gooner.Core.Panels;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine.Panels;

// LongTermStatisticsDialog: the history across the app's lifetime, from the Statistics menu only - the
// bests as tiles, the chart in a section of its own with its metric picked above it.
public partial class LongTermStatisticsPanel : AppPanel
{
    public LongTermStatisticsPanel(LongTermStatistics view)
        : base(L10n.T("Your Long-term Statistics"), new Vector2(960, 740))
    {
        Body.AddThemeConstantOverride("separation", 12);
        var (bests, bestRows) = Widgets.Section(L10n.T("Your bests"), view.SessionsPlayed);
        var tiles = Widgets.Tiles(view.Bests.Count);
        foreach (var best in view.Bests.Values) tiles.AddChild(Widgets.Tile(best.Label, best.Value));
        bestRows.AddChild(tiles);
        Body.AddChild(bests);

        var chart = new LineChart();
        var selector = new OptionButton { FocusMode = FocusModeEnum.None };
        foreach (var metric in LongTermStatistics.Metrics)
            selector.AddItem(metric.Label);
        void Show(long index)
        {
            var metric = LongTermStatistics.Metrics[(int)index];
            chart.Plot(view.Series(metric.Metric), $"{metric.Label} ({metric.Unit})");
        }
        selector.ItemSelected += Show;
        var (history, rows) = Widgets.Section(L10n.T("Session by session"), trailing: selector);
        history.SizeFlagsVertical = SizeFlags.ExpandFill;
        rows.SizeFlagsVertical = SizeFlags.ExpandFill;
        rows.AddChild(chart);
        Body.AddChild(history);
        Show(0);

        AddButton(L10n.T("Close"), Close, primary: true);
    }
}
