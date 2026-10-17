using Gooner.Core.App;
using Gooner.Core.Localization;
using Gooner.Core.Panels;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine.Panels;

// LongTermStatisticsDialog: the history across the app's lifetime, from the Statistics menu only.
public partial class LongTermStatisticsPanel : AppPanel
{
    public LongTermStatisticsPanel(LongTermStatistics view)
        : base(L10n.T("Your Long-term Statistics"), new Vector2(960, 720))
    {
        Body.AddChild(Widgets.Text(view.SessionsPlayed, 13, Palette.Text));
        foreach (var line in view.BestLines.Values)
            Body.AddChild(Widgets.Text(line, 13, Palette.Text));

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
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = L10n.T("Chart metric:") });
        row.AddChild(selector);
        Body.AddChild(row);
        Body.AddChild(chart);
        Show(0);

        AddButton(L10n.T("Close"), Close, primary: true);
    }
}
