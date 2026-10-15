using System.Text.Json.Nodes;
using Gooner.Core.Data;
using Gooner.Core.Scoring;
using Gooner.Core.Tests.Support;

namespace Gooner.Core.Tests.Scoring;

// Port of tests/test_score_tracker.py. Where Python monkeypatches time.time, these move a
// FakeTime. The two QSettings-migration tests (history_migrates_out_of_settings_on_first_load,
// history_survives_a_migration_then_a_new_session) come with the data migration in phase 4.
public sealed class ScoreTrackerTests : IDisposable
{
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-score-");
    private readonly FakeTime _time = new();

    public void Dispose() => _tmp.Delete(recursive: true);

    private UserDataStore Store(string name = "appdata") => new(Path.Combine(_tmp.FullName, name));

    private ScoreTracker Tracker(UserDataStore? store = null) => new(store, _time);

    private static JsonObject HistoryEntry(Action<JsonObject>? overrides = null)
    {
        var entry = new JsonObject
        {
            ["ended_at"] = "2026-01-01 00:00",
            ["total_dur_sec"] = 1.0,
            ["total_num_beat"] = 1,
            ["average_beat_speed_active"] = 1.0,
            ["fakeout_count"] = 0,
        };
        overrides?.Invoke(entry);
        return entry;
    }

    [Fact]
    public void initial_state()
    {
        var tracker = Tracker();
        Assert.Equal(0, tracker.BeatCount);
        Assert.Equal(0, tracker.NumberOfPauses);
        Assert.Empty(tracker.Patterns);
    }

    [Fact]
    public void session_started_resets_counters()
    {
        var tracker = Tracker();
        tracker.BeatCount = 5;
        tracker.Skips = 3;
        tracker.BeatChanged(2.0, "Standard Beat");
        tracker.BeatChanged(2.0, "Standard Beat");

        tracker.SessionStarted();

        Assert.Equal(0, tracker.BeatCount);
        Assert.Equal(0, tracker.Skips);
        Assert.Empty(tracker.Patterns);
        Assert.NotNull(tracker.SessionStartTime);
    }

    [Fact]
    public void beat_pause_resume_tracks_counts()
    {
        var tracker = Tracker();
        tracker.BeatPaused();
        Assert.Equal(1, tracker.NumberOfPauses);
        Assert.NotNull(tracker.CurPauseStartTime);

        tracker.BeatResumed();
        Assert.Null(tracker.CurPauseStartTime);
        Assert.True(tracker.TotalDurationOfPauses >= 0);
    }

    [Fact]
    public void media_skipped_and_repeated_increment_counters()
    {
        var tracker = Tracker();
        tracker.MediaSkipped();
        tracker.MediaSkipped();
        tracker.MediaRepeated();
        Assert.Equal(2, tracker.Skips);
        Assert.Equal(1, tracker.Repeats);
    }

    [Fact]
    public void beat_changed_tracks_pattern_usage()
    {
        var tracker = Tracker();
        tracker.BeatChanged(2.0, "Standard Beat");
        tracker.BeatChanged(2.0, "Standard Beat");
        tracker.BeatChanged(2.0, "Quick Swing");
        Assert.Equal(3, tracker.NumberOfBeatChanges);
        Assert.Equal([new("Standard Beat", 2), new("Quick Swing", 1)], tracker.Patterns.ToList());
    }

    [Fact]
    public void find_fav_pattern_returns_most_used()
    {
        var tracker = Tracker();
        tracker.BeatChanged(2.0, "A");
        tracker.BeatChanged(2.0, "B");
        tracker.BeatChanged(2.0, "B");
        Assert.Equal("B", tracker.FindFavPattern());
    }

    [Fact]
    public void find_fav_pattern_none_when_no_patterns() =>
        Assert.Null(Tracker().FindFavPattern());

    [Fact]
    public void session_ended_computes_averages()
    {
        var tracker = Tracker();
        tracker.SessionStartTime = 100.0;
        tracker.BeatCount = 20;
        tracker.NumberOfPauses = 2;
        tracker.TotalDurationOfPauses = 5.0;

        _time.Now = 110.0;
        tracker.SessionEnded();

        Assert.Equal(10.0, tracker.TotalRunTime!.Value, 9);
        Assert.Equal(2.5, tracker.AveragePauseDuration!.Value, 9);
        Assert.Equal(2.0, tracker.AverageBeatSpeed!.Value, 9);
        Assert.Equal(20 / 5.0, tracker.AverageBeatSpeedActive!.Value, 9);
    }

    [Fact]
    public void session_ended_no_pauses_average_is_none()
    {
        var tracker = Tracker();
        tracker.SessionStartTime = 0.0;
        _time.Now = 5.0;

        tracker.SessionEnded();

        Assert.Null(tracker.AveragePauseDuration);
    }

    [Fact]
    public void deliver_infos_contains_expected_data()
    {
        var tracker = Tracker();
        tracker.SessionStarted();
        tracker.Beat();
        tracker.Beat();
        tracker.BeatChanged(2.0, "Standard Beat");
        tracker.MediaSkipped();
        tracker.MediaRepeated();

        _time.Now = tracker.SessionStartTime!.Value + 1.0;
        tracker.SessionEnded();

        var info = tracker.DeliverInfos();

        Assert.Equal(2, (int)info["total_num_beat"]!);
        Assert.Equal(1, (int)info["skips"]!);
        Assert.Equal(1, (int)info["repeats"]!);
        Assert.Equal("Standard Beat", (string?)info["most_used_pattern"]);
    }

    [Fact]
    public void climax_outcome_defaults_to_none()
    {
        var tracker = Tracker();
        Assert.Null(tracker.ClimaxOutcome);
        Assert.Null(tracker.DeliverInfos()["climax_outcome"]);
    }

    [Fact]
    public void climax_decided_sets_outcome()
    {
        var tracker = Tracker();
        tracker.ClimaxDecided("ruined");
        Assert.Equal("ruined", tracker.ClimaxOutcome);
        Assert.Equal("ruined", (string?)tracker.DeliverInfos()["climax_outcome"]);
    }

    [Fact]
    public void session_started_resets_climax_outcome()
    {
        var tracker = Tracker();
        tracker.ClimaxDecided("denied");
        tracker.SessionStarted();
        Assert.Null(tracker.ClimaxOutcome);
    }

    // --- fakeout counter ---

    [Fact]
    public void fake_climax_triggered_increments_fakeout_count()
    {
        var tracker = Tracker();
        tracker.FakeClimaxTriggered();
        tracker.FakeClimaxTriggered();
        Assert.Equal(2, tracker.FakeoutCount);
        Assert.Equal(2, (int)tracker.DeliverInfos()["fakeout_count"]!);
    }

    [Fact]
    public void session_started_resets_fakeout_count()
    {
        var tracker = Tracker();
        tracker.FakeoutCount = 3;
        tracker.SessionStarted();
        Assert.Equal(0, tracker.FakeoutCount);
    }

    // --- session history persistence ---

    [Fact]
    public void history_empty_by_default() =>
        Assert.Empty(Tracker().GetHistory());

    [Fact]
    public void session_ended_appends_a_history_entry()
    {
        var tracker = Tracker();
        tracker.SessionStartTime = 0.0;
        tracker.BeatCount = 10;
        _time.Now = 10.0;

        tracker.SessionEnded();

        var history = tracker.GetHistory();
        var entry = Assert.Single(history);
        Assert.Equal(10.0, (double)entry["total_dur_sec"]!, 9);
        Assert.Equal(10, (int)entry["total_num_beat"]!);
        Assert.True(entry.ContainsKey("ended_at"));
    }

    [Fact]
    public void session_ended_persists_history_to_the_data_store()
    {
        var store = Store();
        var tracker = Tracker(store);
        tracker.SessionStartTime = 0.0;
        _time.Now = 5.0;

        tracker.SessionEnded();

        var saved = JsonNode.Parse(File.ReadAllText(store.PathFor("session_history")))!.AsArray();
        Assert.Single(saved);
        Assert.Equal(5.0, (double)saved[0]!["total_dur_sec"]!, 9);
    }

    [Fact]
    public void history_loaded_from_the_data_store()
    {
        var store = Store();
        store.Save("session_history", new JsonArray(HistoryEntry(e => e["total_dur_sec"] = 42.0)));

        var tracker = Tracker(store);

        var entry = Assert.Single(tracker.GetHistory());
        Assert.Equal(42.0, (double)entry["total_dur_sec"]!);
    }

    [Fact]
    public void history_capped_at_max_entries()
    {
        var tracker = Tracker();
        for (var i = 0; i < ScoreTracker.MaxHistoryEntries; i++)
            tracker.History.Add(HistoryEntry(e => e["ended_at"] = $"entry-{i}"));
        tracker.SessionStartTime = 0.0;
        _time.Now = 1.0;

        tracker.SessionEnded();

        var history = tracker.GetHistory();
        Assert.Equal(ScoreTracker.MaxHistoryEntries, history.Count);
        Assert.Equal("entry-1", (string?)history[0]["ended_at"]);  // oldest (entry-0) dropped
    }

    // --- personal-record detection ---

    [Fact]
    public void first_ever_session_sets_no_new_records()
    {
        var tracker = Tracker();
        tracker.SessionStartTime = 0.0;
        tracker.BeatCount = 50;
        _time.Now = 100.0;

        tracker.SessionEnded();

        Assert.Empty(tracker.LastSessionNewRecords);
    }

    [Fact]
    public void second_session_beating_one_metric_flags_only_that_metric()
    {
        var tracker = Tracker();

        tracker.SessionStartTime = 0.0;
        tracker.BeatCount = 5;
        _time.Now = 10.0;
        tracker.SessionEnded();

        tracker.SessionStarted();
        tracker.SessionStartTime = 100.0;
        tracker.BeatCount = 3;
        _time.Now = 120.0;  // 20s, beats the 10s record; fewer beats doesn't
        tracker.SessionEnded();

        Assert.Equal(["total_dur_sec"], tracker.LastSessionNewRecords.Keys);
        Assert.Equal(10.0, tracker.LastSessionNewRecords["total_dur_sec"], 9);
    }

    [Fact]
    public void tying_previous_best_is_not_a_new_record()
    {
        var tracker = Tracker();
        tracker.SessionStartTime = 0.0;
        _time.Now = 10.0;
        tracker.SessionEnded();

        tracker.SessionStarted();
        tracker.SessionStartTime = 100.0;
        _time.Now = 110.0;  // same 10s duration again
        tracker.SessionEnded();

        Assert.False(tracker.LastSessionNewRecords.ContainsKey("total_dur_sec"));
    }

    [Fact]
    public void get_all_time_bests_reflects_max_across_history()
    {
        var tracker = Tracker();
        tracker.SessionStartTime = 0.0;
        tracker.BeatCount = 5;
        _time.Now = 10.0;
        tracker.SessionEnded();

        tracker.SessionStarted();
        tracker.SessionStartTime = 100.0;
        tracker.BeatCount = 50;
        _time.Now = 105.0;
        tracker.SessionEnded();

        var bests = tracker.GetAllTimeBests();
        Assert.Equal(10.0, bests["total_dur_sec"], 9);
        Assert.Equal(50, bests["total_num_beat"]);
    }

    // --- shared metric-value formatting ---

    [Fact]
    public void format_metric_value_formats_duration_as_time() =>
        Assert.Equal("2 Min 5s", ScoreTracker.FormatMetricValue("total_dur_sec", 125.0));

    [Fact]
    public void format_metric_value_formats_speed_with_unit() =>
        Assert.Equal("0.43 beats/sec", ScoreTracker.FormatMetricValue("average_beat_speed_active", 0.4321));

    [Fact]
    public void format_metric_value_formats_counts_as_plain_numbers()
    {
        Assert.Equal("42", ScoreTracker.FormatMetricValue("total_num_beat", 42));
        Assert.Equal("3", ScoreTracker.FormatMetricValue("fakeout_count", 3));
    }

    // --- live record-chase ---

    [Fact]
    public void live_metrics_empty_before_session_started() =>
        Assert.Empty(Tracker().LiveMetrics());

    [Fact]
    public void live_metrics_reflects_running_session()
    {
        var tracker = Tracker();
        tracker.SessionStarted();
        tracker.SessionStartTime = 0.0;
        tracker.Beat();
        tracker.Beat();
        tracker.FakeClimaxTriggered();
        _time.Now = 10.0;

        var live = tracker.LiveMetrics();

        Assert.Equal(10.0, live["total_dur_sec"], 9);
        Assert.Equal(2, live["total_num_beat"]);
        Assert.Equal(1, live["fakeout_count"]);
        Assert.Equal(2 / 10.0, live["average_beat_speed_active"], 9);
    }

    [Fact]
    public void live_metrics_excludes_completed_pause_duration()
    {
        var tracker = Tracker();
        tracker.SessionStarted();
        tracker.SessionStartTime = 0.0;
        tracker.BeatCount = 4;
        tracker.TotalDurationOfPauses = 6.0;
        _time.Now = 10.0;

        var live = tracker.LiveMetrics();

        // active_time = 10s elapsed - 6s paused = 4s active
        Assert.Equal(4 / 4.0, live["average_beat_speed_active"], 9);
    }

    [Fact]
    public void record_chase_status_none_without_session_running() =>
        Assert.Null(Tracker().RecordChaseStatus(new Dictionary<string, double> { ["total_num_beat"] = 100 }));

    [Fact]
    public void record_chase_status_none_without_any_bests()
    {
        var tracker = Tracker();
        tracker.SessionStarted();
        tracker.SessionStartTime = 0.0;
        _time.Now = 5.0;

        Assert.Null(tracker.RecordChaseStatus(new Dictionary<string, double>()));
    }

    [Fact]
    public void record_chase_status_none_below_reveal_threshold()
    {
        var tracker = Tracker();
        tracker.SessionStarted();
        tracker.SessionStartTime = 0.0;
        tracker.BeatCount = 50;  // 50% of the record - below the 0.8 reveal threshold
        _time.Now = 1.0;

        Assert.Null(tracker.RecordChaseStatus(new Dictionary<string, double> { ["total_num_beat"] = 100 }));
    }

    [Fact]
    public void record_chase_status_selects_highest_progress_metric()
    {
        var tracker = Tracker();
        tracker.SessionStarted();
        tracker.SessionStartTime = 0.0;
        tracker.BeatCount = 85;  // 85% of its record
        _time.Now = 1.0;

        var bestValues = new Dictionary<string, double> { ["total_num_beat"] = 100, ["fakeout_count"] = 100 };  // fakeout_count stays at 0%
        var status = tracker.RecordChaseStatus(bestValues);

        Assert.Equal(("total_num_beat", 85.0, 100.0), status);
    }

    [Fact]
    public void record_chase_status_prioritizes_already_broken_metric()
    {
        var tracker = Tracker();
        tracker.SessionStarted();
        tracker.SessionStartTime = 0.0;
        tracker.BeatCount = 90;  // 90% of its record - closer in absolute terms
        tracker.FakeoutCount = 3;  // already exceeds its record of 2
        _time.Now = 1.0;

        var bestValues = new Dictionary<string, double> { ["total_num_beat"] = 100, ["fakeout_count"] = 2 };
        var status = tracker.RecordChaseStatus(bestValues);

        Assert.Equal(("fakeout_count", 3.0, 2.0), status);
    }

    // Python passes {"fakeout_count": None}; a C# dictionary of doubles says "missing" by leaving
    // the key out.
    [Fact]
    public void record_chase_status_ignores_zero_or_missing_bests()
    {
        var tracker = Tracker();
        tracker.SessionStarted();
        tracker.SessionStartTime = 0.0;
        tracker.BeatCount = 90;
        _time.Now = 1.0;

        Assert.Null(tracker.RecordChaseStatus(new Dictionary<string, double> { ["total_num_beat"] = 0 }));
    }

    // Instantaneous speed right at session start can spike above the eventual session average
    // (small active_time denominator), so it's excluded from the live chase even when its ratio
    // would otherwise dominate every other metric.
    [Fact]
    public void record_chase_status_ignores_average_beat_speed_active()
    {
        var tracker = Tracker();
        tracker.SessionStarted();
        tracker.SessionStartTime = 0.0;
        tracker.BeatCount = 10;  // 10 beats in 1s = 10 beats/sec, wildly over a 0.5 beats/sec best
        _time.Now = 1.0;

        var bestValues = new Dictionary<string, double> { ["average_beat_speed_active"] = 0.5, ["total_num_beat"] = 1000 };
        Assert.Null(tracker.RecordChaseStatus(bestValues));
    }

    [Fact]
    public void clear_history_empties_memory_and_the_data_store()
    {
        var store = Store();
        var tracker = Tracker(store);
        tracker.History.Add(new JsonObject { ["total_dur_sec"] = 5 });
        store.Save("session_history", new JsonArray(new JsonObject { ["total_dur_sec"] = 5 }));

        tracker.ClearHistory();

        Assert.Empty(tracker.GetHistory());
        Assert.Equal(ScoreTracker.ComputeBests([]), tracker.GetAllTimeBests());
        Assert.False(File.Exists(store.PathFor("session_history")));
    }

    // --- what the user says actually happened ---

    // Unknown is a real answer and must not be guessed at: a session nobody answered for is not
    // the same as one that was stopped.
    [Fact]
    public void the_reported_outcome_starts_unknown()
    {
        var tracker = Tracker();
        Assert.Null(tracker.ReportedOutcome);
        Assert.Null(tracker.DeliverInfos()["reported_outcome"]);
    }

    // The whole point: demanded 'denied' and reported 'came' is disobedience, and that only exists
    // as a fact if the two are stored separately.
    [Fact]
    public void the_reported_outcome_is_kept_apart_from_the_announced_one()
    {
        var tracker = Tracker();
        tracker.ClimaxDecided("denied");

        tracker.OutcomeReported("came");

        Assert.Equal("denied", (string?)tracker.DeliverInfos()["climax_outcome"]);
        Assert.Equal("came", (string?)tracker.DeliverInfos()["reported_outcome"]);
    }

    [Fact]
    public void falling_for_a_fake_out_is_counted()
    {
        var tracker = Tracker();
        tracker.FakeClimaxTriggered();
        tracker.FakeClimaxTriggered();

        tracker.FellForFakeClimax();

        Assert.Equal(2, (int)tracker.DeliverInfos()["fakeout_count"]!);
        Assert.Equal(1, (int)tracker.DeliverInfos()["fakeouts_fallen_for"]!);
    }

    [Fact]
    public void a_new_session_forgets_what_was_reported_last_time()
    {
        var tracker = Tracker();
        tracker.OutcomeReported("came");
        tracker.FellForFakeClimax();

        tracker.SessionStarted();

        Assert.Null(tracker.ReportedOutcome);
        Assert.Equal(0, tracker.FakeoutsFallenFor);
    }

    [Fact]
    public void the_history_remembers_both_outcomes_so_achievements_can_look_back()
    {
        var tracker = Tracker(Store("data"));
        tracker.SessionStarted();
        tracker.ClimaxDecided("denied");
        tracker.OutcomeReported("came");
        tracker.FellForFakeClimax();
        _time.Now = tracker.SessionStartTime!.Value + 1.0;

        tracker.SessionEnded();

        var entry = tracker.GetHistory()[^1];
        Assert.Equal("denied", (string?)entry["climax_outcome"]);
        Assert.Equal("came", (string?)entry["reported_outcome"]);
        Assert.Equal(1, (int)entry["fakeouts_fallen_for"]!);
    }

    [Fact]
    public void edges_are_counted_and_remembered()
    {
        var tracker = Tracker(Store("data"));
        tracker.SessionStarted();
        tracker.EdgeReached();
        tracker.EdgeReached();
        _time.Now = tracker.SessionStartTime!.Value + 1.0;

        tracker.SessionEnded();

        Assert.Equal(2, (int)tracker.DeliverInfos()["edge_count"]!);
        Assert.Equal(2, (int)tracker.GetHistory()[^1]["edge_count"]!);
    }

    [Fact]
    public void a_new_session_starts_at_zero_edges()
    {
        var tracker = Tracker();
        tracker.EdgeReached();
        tracker.SessionStarted();
        Assert.Equal(0, tracker.EdgeCount);
    }

    // Otherwise nothing downstream can tell a replay from a fresh session - they look identical in
    // every number.
    [Fact]
    public void a_replayed_session_is_marked_as_one()
    {
        var tracker = Tracker(Store("data"));
        tracker.SessionStarted();
        tracker.ReplayStarted();
        _time.Now = tracker.SessionStartTime!.Value + 1.0;

        tracker.SessionEnded();

        Assert.True((bool)tracker.DeliverInfos()["was_replay"]!);
        Assert.True((bool)tracker.GetHistory()[^1]["was_replay"]!);
    }

    [Fact]
    public void a_fresh_session_is_not_marked_as_a_replay()
    {
        var tracker = Tracker();
        tracker.ReplayStarted();
        tracker.SessionStarted();
        Assert.False((bool)tracker.DeliverInfos()["was_replay"]!);
    }
}
