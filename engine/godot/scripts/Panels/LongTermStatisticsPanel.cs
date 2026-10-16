using Gooner.Core.App;
using Gooner.Core.Panels;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine.Panels;

// LongTermStatisticsDialog: the history across the app's lifetime, from the Statistics menu only.
public partial class LongTermStatisticsPanel : AppPanel
{
    public LongTermStatisticsPanel(LongTermStatistics view)
        : base("Your Long-term Statistics", new Vector2(960, 720))
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
        row.AddChild(new Label { Text = "Chart metric:" });
        row.AddChild(selector);
        Body.AddChild(row);
        Body.AddChild(chart);
        Show(0);

        AddButton("Close", Close, primary: true);
    }
}
