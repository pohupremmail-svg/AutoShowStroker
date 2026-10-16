using System.Text.Json.Nodes;
using Gooner.Core.Achievements;
using Gooner.Core.Panels;
using Gooner.Core.Sessions;

namespace Gooner.Core.Tests.Panels;

// Port of tests/test_statistics_dialog.py: the recap after a session, without its widgets. The
// Qt-only tests - the table's fixed height, buttons inside the locked dialog size - have no
// counterpart; a panel lays itself out.
public sealed class StatisticsReportTests
{
    private static JsonObject FullStats() => new()
    {
        ["total_dur_sec"] = 125.0,
        ["pause_dur_sec"] = 10.0,
        ["average_pause_dur_sec"] = 5.0,
        ["total_num_pauses"] = 2,
        ["total_num_beat"] = 50,
        ["total_num_beat_change"] = 5,
        ["average_beat_speed"] = 0.4,
        ["average_beat_speed_active"] = 0.43,
        ["most_used_pattern"] = "Standard Beat",
        ["skips"] = 1,
        ["repeats"] = 0,
        ["climax_outcome"] = null,
        ["fakeout_count"] = 3,
    };

    private static JsonObject Without(params string[] keys)
    {
        var stats = FullStats();
        foreach (var key in keys) stats.Remove(key);
        return stats;
    }

    private static JsonObject With(string key, JsonNode? value)
    {
        var stats = FullStats();
        stats[key] = value;
        return stats;
    }

    private static string Row(StatisticsReport report, string metric) =>
        report.Rows.Single(row => row.Metric == metric).Value;

    [Theory]
    [InlineData(null, "N/A")]
    [InlineData(0.0, "0s")]
    [InlineData(45.0, "45s")]
    [InlineData(60.0, "1 Min")]
    [InlineData(125.0, "2 Min 5s")]
    public void format_time(double? seconds, string expected) =>
        Assert.Equal(expected, StatisticsReport.FormatTime(seconds));

    [Fact]
    public void populate_table_has_a_row_per_metric() =>
        Assert.Equal(14, new StatisticsReport(FullStats()).Rows.Count);

    [Fact]
    public void fakeout_row_shows_value() =>
        Assert.Equal("3", Row(new StatisticsReport(FullStats()), "Fakeouts survived"));

    [Fact]
    public void missing_fakeout_count_shows_na() =>
        Assert.Equal("N/A", Row(new StatisticsReport(Without("fakeout_count")), "Fakeouts survived"));

    [Fact]
    public void missing_optional_key_shows_na() =>
        Assert.Equal("N/A", Row(new StatisticsReport(Without("skips")), "Skips"));

    [Fact]
    public void the_table_reads_in_the_dialogs_order()
    {
        var report = new StatisticsReport(FullStats());

        Assert.Equal(
            [
                ("Total duration", "2 Min 5s"), ("Active Time", "1 Min 55s"), ("Pause duration", "10s"),
                ("Total number of pauses", "2"), ("Total number of beats", "50"),
                ("Total number of beat changes", "5"), ("Average pause duration", "5s"),
                ("Average beat speed (1/sec)", "0.40"), ("Average beat speed during active time (1/sec)", "0.43"),
                ("Favourite pattern", "Standard Beat"), ("Skips", "1"), ("Repeats", "0"),
                ("Fakeouts survived", "3"), ("Climax outcome", "N/A"),
            ],
            report.Rows.Select(row => (row.Metric, row.Value)));
    }

    [Fact]
    public void gen_conc_text_formats_summary()
    {
        var text = new StatisticsReport(FullStats()).Conclusion;

        Assert.Contains("2 Min 5s", text); // total_dur_sec formatted
        Assert.Contains("1 Min 55s", text); // active time = total - pauses, formatted
        Assert.Contains("0.43 beats per second", text); // average_beat_speed_active, 2 decimals
        Assert.Contains("skipped 1 media files and repeated 0 of them", text);
        Assert.Contains("'Standard Beat'", text);
    }

    [Fact]
    public void gen_conc_text_defaults_missing_skips_and_repeats_to_zero() =>
        Assert.Contains("skipped 0 media files and repeated 0 of them", new StatisticsReport(Without("skips", "repeats")).Conclusion);

    [Fact]
    public void the_conclusion_reads_word_for_word()
    {
        Assert.Equal(
            "You survived a total of 2 Min 5s! During this session, you spent 1 Min 55s actively stroking " +
            "with an average speed of 0.43 beats per second.\nYou skipped 1 media files and repeated 0 of them. " +
            "Your favorite rhythm pattern was 'Standard Beat'.",
            new StatisticsReport(FullStats()).Conclusion);
    }

    [Fact]
    public void title_for_outcome_real() =>
        Assert.Equal("Congratulations to your successful session.\nI hope you came a lot!", StatisticsReport.TitleForOutcome("real"));

    [Fact]
    public void title_for_outcome_ruined() =>
        Assert.Equal("Congratulations on your session.\nEnjoy your ruined orgasm.", StatisticsReport.TitleForOutcome("ruined"));

    [Fact]
    public void title_for_outcome_denied() =>
        Assert.Equal("Session over.\nNot today - no cumming for you.", StatisticsReport.TitleForOutcome("denied"));

    [Fact]
    public void title_for_outcome_none_defaults_to_classic_message() =>
        Assert.Equal("Congratulations to your successful session.\nI hope you came a lot!", StatisticsReport.TitleForOutcome(null));

    [Fact]
    public void dialog_title_reflects_denied_outcome() =>
        Assert.Equal("Session over.\nNot today - no cumming for you.", new StatisticsReport(With("climax_outcome", "denied")).Title);

    [Fact]
    public void climax_outcome_row_shows_na_when_none() =>
        Assert.Equal("N/A", Row(new StatisticsReport(FullStats()), "Climax outcome"));

    [Fact]
    public void climax_outcome_row_shows_outcome_value() =>
        Assert.Equal("ruined", Row(new StatisticsReport(With("climax_outcome", "ruined")), "Climax outcome"));

    // --- new personal record cards ---

    [Fact]
    public void no_record_cards_when_new_records_is_none() =>
        Assert.Empty(new StatisticsReport(FullStats(), newRecords: null).RecordCards);

    [Fact]
    public void no_record_cards_when_new_records_is_empty() =>
        Assert.Empty(new StatisticsReport(FullStats(), newRecords: new Dictionary<string, double>()).RecordCards);

    [Fact]
    public void one_card_per_new_record() =>
        Assert.Equal(2, new StatisticsReport(FullStats(), new Dictionary<string, double> { ["total_dur_sec"] = 100.0, ["fakeout_count"] = 1 }).RecordCards.Count);

    [Fact]
    public void record_card_shows_metric_label_new_value_and_previous_best()
    {
        // total_dur_sec = 125.0 -> "2 Min 5s"; the previous best 100.0 -> "1 Min 40s".
        var card = new StatisticsReport(FullStats(), new Dictionary<string, double> { ["total_dur_sec"] = 100.0 }).RecordCards[0];

        Assert.Equal("\U0001f3c6 New Personal Record: Total Duration", card.Title);
        Assert.Equal("2 Min 5s", card.Value);
        Assert.Equal("Previous best: 1 Min 40s", card.Previous);
    }

    [Fact]
    public void record_card_formats_count_metrics_as_plain_numbers()
    {
        var card = new StatisticsReport(FullStats(), new Dictionary<string, double> { ["fakeout_count"] = 1 }).RecordCards[0];

        Assert.Contains("Fakeouts Survived", card.Title);
        Assert.Equal("3", card.Value);
        Assert.Equal("Previous best: 1", card.Previous);
    }

    // A second session stopped after a couple of seconds never recalculates the beat, so
    // most_used_pattern is None - it used to render as the literal string 'None'.
    [Fact]
    public void missing_favourite_pattern_reads_as_not_available()
    {
        var stats = new JsonObject
        {
            ["total_dur_sec"] = 10.0,
            ["pause_dur_sec"] = 0.0,
            ["total_num_pauses"] = 0,
            ["total_num_beat"] = 0,
            ["total_num_beat_change"] = 0,
            ["average_pause_dur_sec"] = null,
            ["average_beat_speed"] = 0.0,
            ["average_beat_speed_active"] = 0.0,
            ["most_used_pattern"] = null,
            ["skips"] = 0,
            ["repeats"] = 0,
            ["fakeout_count"] = 0,
            ["climax_outcome"] = null,
        };

        var report = new StatisticsReport(stats);

        Assert.DoesNotContain("'None'", report.Conclusion);
        Assert.Contains("N/A", report.Conclusion);
        Assert.Equal("N/A", Row(report, "Average pause duration"));
    }

    // --- Session Explorer entry point ---

    private static SessionTimeline Timeline(bool withSegments = true) => new(
        1000.0, 1060.0, null, null, [],
        withSegments ? [new RecordedSegment("beat", "Standard Beat", 2.0, 1000.0, 1060.0, [])] : []);

    [Fact]
    public void no_explorer_button_without_a_timeline() =>
        Assert.False(new StatisticsReport(FullStats()).HasExplorer);

    [Fact]
    public void the_explorer_button_appears_with_a_timeline() =>
        Assert.True(new StatisticsReport(FullStats(), timeline: Timeline()).HasExplorer);

    [Fact]
    public void no_explorer_button_for_a_timeline_without_segments() =>
        Assert.False(new StatisticsReport(FullStats(), timeline: Timeline(withSegments: false)).HasExplorer);

    [Fact]
    public void newly_earned_achievements_get_a_card_of_their_own()
    {
        var first = AchievementCatalogue.All[0];

        var cards = new StatisticsReport(FullStats(), newAchievements: [first]).AchievementCards;

        Assert.Single(cards);
        Assert.Contains(first.Name, cards[0].Title);
        Assert.Equal($"✦ Achievement unlocked: {first.Name}", cards[0].Title);
        Assert.Equal(first.Description, cards[0].Description);
    }

    // A secret one may not carry a description of its own.
    [Fact]
    public void an_achievement_without_a_description_gets_the_teaser()
    {
        var bare = AchievementCatalogue.All[0] with { Description = "" };

        var card = new StatisticsReport(FullStats(), newAchievements: [bare]).AchievementCards[0];

        Assert.Equal("Some things you only find by doing.", card.Description);
    }

    [Fact]
    public void no_achievement_cards_when_nothing_was_earned() =>
        Assert.Empty(new StatisticsReport(FullStats()).AchievementCards);
}
