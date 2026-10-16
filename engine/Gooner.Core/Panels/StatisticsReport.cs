using System.Globalization;
using System.Text.Json.Nodes;
using Gooner.Core.Achievements;
using Gooner.Core.Formatting;
using Gooner.Core.Scoring;
using Gooner.Core.Sessions;

namespace Gooner.Core.Panels;

public sealed record StatisticsRow(string Metric, string Value);

public sealed record RecordCard(string Title, string Value, string Previous);

public sealed record AchievementCard(string Title, string Description);

// Port of src/StatisticsDialog.py without its widgets: the recap after a session. A title that
// fits how it ended, a paragraph about it, a card for every personal record broken and every
// achievement earned, and the table of everything ScoreTracker counted. The way into the Session
// Explorer is there only when the session has something to explore.
//
// The numbers are ScoreTracker.DeliverInfos(), read the way the dialog read the dict: a key that
// is missing shows as N/A, and so does one that is null where Python would have printed None.
public sealed class StatisticsReport
{
    public const string NotAvailable = "N/A";

    public StatisticsReport(
        JsonObject stats,
        IReadOnlyDictionary<string, double>? newRecords = null,
        SessionTimeline? timeline = null,
        IReadOnlyList<Achievement>? newAchievements = null)
    {
        ArgumentNullException.ThrowIfNull(stats);
        Title = TitleForOutcome(stats["climax_outcome"] is JsonValue outcome ? (string?)outcome : null);
        Conclusion = ConclusionFor(stats);
        Rows = RowsFor(stats);
        RecordCards = [.. (newRecords ?? new Dictionary<string, double>()).Select(record => RecordCardFor(stats, record.Key, record.Value))];
        AchievementCards = [.. (newAchievements ?? []).Select(a => new AchievementCard(
            $"✦ Achievement unlocked: {a.Name}",
            string.IsNullOrEmpty(a.Description) ? "Some things you only find by doing." : a.Description))];
        // A session stopped before the beat ever started has no segments, and an explorer opening
        // on an empty timeline is worse than no button at all.
        HasExplorer = timeline is { Segments.Count: > 0 };
    }

    public string Title { get; }

    public string Conclusion { get; }

    public IReadOnlyList<StatisticsRow> Rows { get; }

    public IReadOnlyList<RecordCard> RecordCards { get; }

    public IReadOnlyList<AchievementCard> AchievementCards { get; }

    public bool HasExplorer { get; }

    public static string FormatTime(double? seconds) => Clock.FormatDuration(seconds);

    public static string TitleForOutcome(string? outcome) => outcome switch
    {
        "denied" => "Session over.\nNot today - no cumming for you.",
        "ruined" => "Congratulations on your session.\nEnjoy your ruined orgasm.",
        _ => "Congratulations to your successful session.\nI hope you came a lot!", // "real" or none
    };

    private static double? Number(JsonObject stats, string key) =>
        stats[key] is JsonValue value && PyJson.IsNumber(value) ? PyJson.Number(value) : null;

    private static string ConclusionFor(JsonObject stats)
    {
        var total = Number(stats, "total_dur_sec") ?? 0;
        var active = total - (Number(stats, "pause_dur_sec") ?? 0);
        var skips = stats.ContainsKey("skips") ? PyJson.Str(stats["skips"]) : "0";
        var repeats = stats.ContainsKey("repeats") ? PyJson.Str(stats["repeats"]) : "0";
        var speed = (Number(stats, "average_beat_speed_active") ?? 0).ToString("F2", CultureInfo.InvariantCulture);
        var favourite = PyJson.Truthy(stats["most_used_pattern"]) ? PyJson.Str(stats["most_used_pattern"]) : NotAvailable;
        return $"You survived a total of {FormatTime(total)}! " +
            $"During this session, you spent {FormatTime(active)} actively stroking " +
            $"with an average speed of {speed} beats per second.\n" +
            $"You skipped {skips} media files and repeated {repeats} of them. " +
            // A session too short to ever recalculate the beat has no favourite - that rendered
            // as the literal string 'None' while every other nullable stat here already went
            // through an "N/A".
            $"Your favorite rhythm pattern was '{favourite}'.";
    }

    private static List<StatisticsRow> RowsFor(JsonObject stats)
    {
        string Time(params string[] keys)
        {
            if (keys.Any(k => !stats.ContainsKey(k))) return NotAvailable;
            if (keys.Length == 1) return FormatTime(Number(stats, keys[0]));
            return Number(stats, keys[0]) is { } total && Number(stats, keys[1]) is { } pauses ? FormatTime(total - pauses) : NotAvailable;
        }
        string Plain(string key) => stats.ContainsKey(key) && stats[key] is not null ? PyJson.Str(stats[key]) : NotAvailable;
        string Speed(string key) => Number(stats, key) is { } speed ? speed.ToString("F2", CultureInfo.InvariantCulture) : NotAvailable;
        string OrNotAvailable(string key) => PyJson.Truthy(stats[key]) ? PyJson.Str(stats[key]) : NotAvailable;

        return
        [
            new("Total duration", Time("total_dur_sec")),
            new("Active Time", Time("total_dur_sec", "pause_dur_sec")),
            new("Pause duration", Time("pause_dur_sec")),
            new("Total number of pauses", Plain("total_num_pauses")),
            new("Total number of beats", Plain("total_num_beat")),
            new("Total number of beat changes", Plain("total_num_beat_change")),
            new("Average pause duration", Time("average_pause_dur_sec")),
            new("Average beat speed (1/sec)", Speed("average_beat_speed")),
            new("Average beat speed during active time (1/sec)", Speed("average_beat_speed_active")),
            new("Favourite pattern", OrNotAvailable("most_used_pattern")),
            new("Skips", Plain("skips")),
            new("Repeats", Plain("repeats")),
            new("Fakeouts survived", Plain("fakeout_count")),
            new("Climax outcome", OrNotAvailable("climax_outcome")),
        ];
    }

    private static RecordCard RecordCardFor(JsonObject stats, string metric, double previous)
    {
        var label = ScoreTracker.PrMetricLabels.GetValueOrDefault(metric, metric);
        var value = Number(stats, metric) is { } now ? ScoreTracker.FormatMetricValue(metric, now) : NotAvailable;
        return new RecordCard(
            $"\U0001f3c6 New Personal Record: {label}",
            value,
            $"Previous best: {ScoreTracker.FormatMetricValue(metric, previous)}");
    }
}
