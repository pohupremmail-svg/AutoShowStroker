using System.Text.Json.Nodes;
using Gooner.Core.Localization;
using Gooner.Core.Scoring;
using Gooner.Core.Sessions;

namespace Gooner.Core.Panels;

public sealed record ChartMetric(string Metric, string Label, string Unit);

public sealed record ChartPoint(int Session, double Value);

// A number shown as a tile: what it is, and its value.
public sealed record StatTile(string Label, string Value);

// Port of src/LongTermStatisticsDialog.py without pyqtgraph: the read-only view of the session
// history across the app's lifetime, shown only from the Statistics menu, never by itself. How
// many sessions, the best of every record metric, and one metric at a time as a line over the
// sessions - the series it would plot, and the label of the axis it is plotted against.
public sealed class LongTermStatistics
{
    private readonly IReadOnlyList<JsonObject> _history;

    public LongTermStatistics(IReadOnlyList<JsonObject> history, IReadOnlyDictionary<string, double> allTimeBests)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(allTimeBests);
        _history = history;
        SessionsPlayed = L10n.F("Sessions played: {0}", history.Count);
        Bests = ScoreTracker.PrMetrics.ToDictionary(metric => metric, metric => new StatTile(
            L10n.T(ScoreTracker.PrMetricLabels[metric]),
            allTimeBests.TryGetValue(metric, out var value) ? ScoreTracker.FormatMetricValue(metric, value) : StatisticsReport.NotAvailable));
    }

    // The choices of the chart's metric selector, the first one shown first, in the language in use.
    public static IReadOnlyList<ChartMetric> Metrics =>
        [.. ScoreTracker.PrMetrics.Select(m => new ChartMetric(m, L10n.T(ScoreTracker.PrMetricLabels[m]), L10n.T(ScoreTracker.PrMetricUnits[m])))];

    public string SessionsPlayed { get; }

    // The all-time best of every record metric, in the record metrics' order.
    public IReadOnlyDictionary<string, StatTile> Bests { get; }

    // Session #1, #2, ... and the metric's value in each. An entry from before the metric existed
    // counts as zero, as entry.get(metric, 0) had it; one holding something that is not a number
    // is left out rather than plotted as a guess.
    public IReadOnlyList<ChartPoint> Series(string metric)
    {
        var points = new List<ChartPoint>();
        for (var i = 0; i < _history.Count; i++)
        {
            var entry = _history[i];
            if (!entry.ContainsKey(metric)) points.Add(new ChartPoint(i + 1, 0));
            else if (entry[metric] is JsonValue value && PyJson.IsNumber(value)) points.Add(new ChartPoint(i + 1, PyJson.Number(value)));
        }
        return points;
    }
}
