using System.Text.Json.Nodes;
using Gooner.Core.Panels;
using Gooner.Core.Scoring;

namespace Gooner.Core.Tests.Panels;

// Port of tests/test_long_term_statistics_dialog.py: the history view without pyqtgraph. The
// chart is the series it would plot and the label of its y axis.
public sealed class LongTermStatisticsTests
{
    private static List<JsonObject> History() =>
    [
        new()
        {
            ["ended_at"] = "2026-01-01 10:00", ["total_dur_sec"] = 60.0, ["total_num_beat"] = 10,
            ["average_beat_speed_active"] = 0.5, ["fakeout_count"] = 1,
        },
        new()
        {
            ["ended_at"] = "2026-01-02 10:00", ["total_dur_sec"] = 120.0, ["total_num_beat"] = 30,
            ["average_beat_speed_active"] = 0.8, ["fakeout_count"] = 0,
        },
    ];

    private static Dictionary<string, double> Bests() => new()
    {
        ["total_dur_sec"] = 120.0, ["total_num_beat"] = 30, ["average_beat_speed_active"] = 0.8, ["fakeout_count"] = 1,
    };

    private readonly LongTermStatistics _view = new(History(), Bests());

    [Fact]
    public void sessions_played_count() => Assert.Equal("Sessions played: 2", _view.SessionsPlayed);

    [Fact]
    public void the_bests_name_their_metric_and_show_its_formatted_all_time_best()
    {
        Assert.Equal(new StatTile("Total Duration", "2 Min"), _view.Bests["total_dur_sec"]);
        Assert.Equal(new StatTile("Total Beats", "30"), _view.Bests["total_num_beat"]);
        Assert.Equal(new StatTile("Active Beat Speed", "0.80 beats/sec"), _view.Bests["average_beat_speed_active"]);
        Assert.Equal(new StatTile("Fakeouts Survived", "1"), _view.Bests["fakeout_count"]);
    }

    [Fact]
    public void a_best_never_reached_shows_na() =>
        Assert.Equal("N/A", new LongTermStatistics([], new Dictionary<string, double>()).Bests["total_dur_sec"].Value);

    [Fact]
    public void the_bests_come_in_the_record_metrics_order() =>
        Assert.Equal(ScoreTracker.PrMetrics, _view.Bests.Keys);

    [Fact]
    public void metric_selector_lists_all_pr_metrics() =>
        Assert.Equal(ScoreTracker.PrMetrics.Select(m => ScoreTracker.PrMetricLabels[m]), LongTermStatistics.Metrics.Select(m => m.Label));

    [Fact]
    public void default_chart_metric_is_total_duration()
    {
        Assert.Equal("total_dur_sec", LongTermStatistics.Metrics[0].Metric);
        Assert.Equal([60.0, 120.0], _view.Series("total_dur_sec").Select(p => p.Value));
    }

    [Fact]
    public void changing_metric_selector_replots_chart() =>
        Assert.Equal([10.0, 30.0], _view.Series("total_num_beat").Select(p => p.Value));

    [Fact]
    public void sessions_are_numbered_from_one() =>
        Assert.Equal([1, 2], _view.Series("total_num_beat").Select(p => p.Session));

    [Fact]
    public void default_chart_y_axis_shows_a_unit()
    {
        Assert.Equal(ScoreTracker.PrMetricUnits["total_dur_sec"], LongTermStatistics.Metrics[0].Unit);
        Assert.NotEmpty(LongTermStatistics.Metrics[0].Unit);
    }

    [Fact]
    public void changing_metric_selector_updates_y_axis_unit() =>
        Assert.Equal(ScoreTracker.PrMetricUnits["fakeout_count"], LongTermStatistics.Metrics.Single(m => m.Metric == "fakeout_count").Unit);

    [Fact]
    public void empty_history_plots_without_crashing() =>
        Assert.Empty(new LongTermStatistics([], new Dictionary<string, double>()).Series("total_dur_sec"));

    // entry.get(metric, 0): an entry from before the metric existed plots as zero.
    [Fact]
    public void an_entry_without_the_metric_plots_as_zero()
    {
        var view = new LongTermStatistics([new JsonObject { ["total_num_beat"] = 5 }], new Dictionary<string, double>());

        Assert.Equal([0.0], view.Series("fakeout_count").Select(p => p.Value));
    }
}
