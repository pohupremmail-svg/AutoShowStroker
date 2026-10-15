using System.Text.Json.Nodes;
using Gooner.Core.Achievements;
using Gooner.Core.Data;
using Gooner.Core.Tests.Support;

namespace Gooner.Core.Tests.Achievements;

// Port of tests/test_achievements.py. The achievement catalogue and the tracker that judges it:
// an achievement is a predicate over a session's stats and the history behind it.
public sealed class AchievementsTests : IDisposable
{
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-achievements-");
    private readonly AchievementTracker _tracker;

    public AchievementsTests() => _tracker = new AchievementTracker(Store());

    public void Dispose() => _tmp.Delete(recursive: true);

    private UserDataStore Store() => new(Path.Combine(_tmp.FullName, "data"));

    // One history entry, as ScoreTracker writes it.
    private static JsonObject Entry(Action<JsonObject>? overrides = null)
    {
        var entry = new JsonObject
        {
            ["ended_at"] = "2026-09-17 20:00",
            ["total_dur_sec"] = 600.0,
            ["total_num_beat"] = 1200,
            ["average_beat_speed_active"] = 2.0,
            ["fakeout_count"] = 0,
            ["climax_outcome"] = null,
            ["reported_outcome"] = null,
            ["fakeouts_fallen_for"] = 0,
            ["edge_count"] = 0,
        };
        overrides?.Invoke(entry);
        return entry;
    }

    // What ScoreTracker.DeliverInfos() hands over for the session just ended.
    private static JsonObject Stats(Action<JsonObject>? overrides = null) => Entry(overrides);

    private static List<JsonObject> Many(int count, Func<JsonObject> make) => [.. Enumerable.Range(0, count).Select(_ => make())];

    private static List<string> Ids(IEnumerable<Achievement> achievements) => [.. achievements.Select(a => a.Id)];

    private static Achievement ById(string id) => AchievementCatalogue.All.Single(a => a.Id == id);

    // --- the catalogue itself ---

    [Fact]
    public void every_achievement_has_a_unique_id()
    {
        var ids = Ids(AchievementCatalogue.All);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    // A typo here is invisible until the dialog is opened, and there is no compiler to catch it.
    [Fact]
    public void every_achievement_names_an_icon_file_that_exists()
    {
        foreach (var item in AchievementCatalogue.All)
            Assert.True(File.Exists(Repo.PathTo(AchievementCatalogue.IconPath(item))), $"{item.Id} -> {item.Icon}.svg");
    }

    [Fact]
    public void every_achievement_says_what_it_wants_unless_it_is_secret()
    {
        foreach (var item in AchievementCatalogue.All)
        {
            Assert.False(string.IsNullOrEmpty(item.Name));
            if (!item.Secret)
                Assert.False(string.IsNullOrEmpty(item.Description));
        }
    }

    [Fact]
    public void the_catalogue_has_secret_entries_and_they_are_the_minority()
    {
        var secret = AchievementCatalogue.All.Where(a => a.Secret).ToList();
        Assert.NotEmpty(secret);
        Assert.True(secret.Count < AchievementCatalogue.All.Count / 2.0);
    }

    // --- unlocking ---

    [Fact]
    public void a_long_session_unlocks_the_endurance_tier()
    {
        var history = new List<JsonObject> { Entry(e => e["total_dur_sec"] = 45 * 60) };

        var unlocked = _tracker.Evaluate(Stats(e => e["total_dur_sec"] = 45 * 60), history);

        Assert.Contains("endurance_45", Ids(unlocked));
    }

    [Fact]
    public void a_short_session_unlocks_nothing_it_has_not_earned()
    {
        static void Short(JsonObject e)
        {
            e["total_dur_sec"] = 60.0;
            e["total_num_beat"] = 100;
        }
        var history = new List<JsonObject> { Entry(Short) };

        var unlocked = _tracker.Evaluate(Stats(Short), history);

        Assert.Empty(Ids(unlocked));
    }

    [Fact]
    public void an_achievement_is_only_ever_announced_once()
    {
        var history = new List<JsonObject> { Entry(e => e["total_dur_sec"] = 45 * 60) };
        _tracker.Evaluate(Stats(e => e["total_dur_sec"] = 45 * 60), history);

        var again = _tracker.Evaluate(Stats(e => e["total_dur_sec"] = 45 * 60), [.. history, Entry()]);

        Assert.DoesNotContain("endurance_45", Ids(again));
    }

    [Fact]
    public void unlocking_is_remembered_across_restarts()
    {
        new AchievementTracker(Store()).Evaluate(
            Stats(e => e["total_dur_sec"] = 45 * 60), [Entry(e => e["total_dur_sec"] = 45 * 60)]);

        var reopened = new AchievementTracker(Store());

        Assert.True(reopened.IsUnlocked("endurance_45"));
        Assert.False(string.IsNullOrEmpty(reopened.UnlockedAt("endurance_45")));
    }

    [Fact]
    public void lifetime_beats_add_up_across_every_session()
    {
        var history = Many(5, () => Entry(e => e["total_num_beat"] = 20_000));

        var unlocked = _tracker.Evaluate(Stats(e => e["total_num_beat"] = 20_000), history);

        Assert.Contains("lifetime_beats_100k", Ids(unlocked));
    }

    [Fact]
    public void coming_back_is_its_own_achievement()
    {
        var history = Many(5, () => Entry());

        var unlocked = _tracker.Evaluate(Stats(), history);

        Assert.Contains("returner_5", Ids(unlocked));
    }

    // --- obedience is only obedience when it cost something ---

    private static Action<JsonObject> Outcomes(string? demanded, string? reported) => e =>
    {
        e["climax_outcome"] = demanded;
        e["reported_outcome"] = reported;
    };

    [Fact]
    public void obeying_a_denial_counts()
    {
        var history = new List<JsonObject> { Entry(Outcomes("denied", "stopped")) };

        var unlocked = _tracker.Evaluate(Stats(), history);

        Assert.Contains("obedient_1", Ids(unlocked));
    }

    [Fact]
    public void obeying_a_ruin_counts() =>
        Assert.Contains("obedient_1", Ids(_tracker.Evaluate(Stats(), [Entry(Outcomes("ruined", "ruined"))])));

    // It is doing what you were going to do anyway - letting it count would make the whole track
    // free.
    [Fact]
    public void being_told_to_come_and_coming_is_not_obedience()
    {
        var history = Many(20, () => Entry(Outcomes("real", "came")));

        var unlocked = _tracker.Evaluate(Stats(), history);

        Assert.DoesNotContain("obedient_1", Ids(unlocked));
    }

    [Fact]
    public void coming_after_a_denial_is_disobedience() =>
        Assert.Contains("disobedient_1", Ids(_tracker.Evaluate(Stats(), [Entry(Outcomes("denied", "came"))])));

    [Fact]
    public void a_session_nobody_answered_for_counts_as_neither()
    {
        var history = Many(20, () => Entry(Outcomes("denied", null)));

        var ids = Ids(_tracker.Evaluate(Stats(), history));

        Assert.DoesNotContain("obedient_1", ids);
        Assert.DoesNotContain("disobedient_1", ids);
    }

    // --- fake-outs and edges ---

    [Fact]
    public void surviving_three_fake_outs_in_one_session_counts() =>
        Assert.Contains("fakeouts_3", Ids(_tracker.Evaluate(
            Stats(e => e["fakeout_count"] = 3), [Entry(e => e["fakeout_count"] = 3)])));

    [Fact]
    public void edging_your_way_through_a_session_counts() =>
        Assert.Contains("edges_10", Ids(_tracker.Evaluate(
            Stats(e => e["edge_count"] = 10), [Entry(e => e["edge_count"] = 10)])));

    [Fact]
    public void a_long_session_without_a_single_edge_counts()
    {
        static void Played(JsonObject e)
        {
            e["total_dur_sec"] = 45 * 60;
            e["edge_count"] = 0;
        }

        Assert.Contains("edges_none", Ids(_tracker.Evaluate(Stats(Played), [Entry(Played)])));
    }

    // --- progress on the ones still locked ---

    [Fact]
    public void a_locked_achievement_reports_how_far_along_you_are()
    {
        var endurance = ById("endurance_45");

        var (current, target) = endurance.Progress!(Stats(e => e["total_dur_sec"] = 20 * 60), [Entry()]);

        Assert.Equal(45 * 60, target);
        Assert.Equal(20 * 60, current, 9);
    }

    // A progress bar at 300% is a bug, not a flourish.
    [Fact]
    public void progress_never_reads_past_its_target()
    {
        foreach (var item in AchievementCatalogue.All)
        {
            if (item.Progress is null) continue;
            var huge = Stats(e =>
            {
                e["total_dur_sec"] = 1_000_000;
                e["total_num_beat"] = 10_000_000;
                e["fakeout_count"] = 500;
                e["edge_count"] = 500;
            });
            var (current, target) = item.Progress(huge, Enumerable.Repeat(huge, 500).ToList());
            Assert.True(current <= target, item.Id);
        }
    }

    // --- clearing it all ---

    [Fact]
    public void everything_can_be_deleted()
    {
        _tracker.Evaluate(Stats(e => e["total_dur_sec"] = 45 * 60), [Entry(e => e["total_dur_sec"] = 45 * 60)]);

        _tracker.Clear();

        Assert.Empty(_tracker.UnlockedIds);
        Assert.False(_tracker.IsUnlocked("endurance_45"));
    }

    // Same shape as ScoreTracker: no store means nothing is persisted, not a crash.
    [Fact]
    public void a_tracker_without_a_store_still_works()
    {
        var bare = new AchievementTracker(dataStore: null);

        var unlocked = bare.Evaluate(Stats(e => e["total_dur_sec"] = 45 * 60), [Entry(e => e["total_dur_sec"] = 45 * 60)]);

        // A 45 minute session with no edges earns the endurance tier and "Never Asked" both.
        Assert.Contains("endurance_45", Ids(unlocked));
        Assert.True(bare.IsUnlocked("endurance_45"));
    }

    [Fact]
    public void a_corrupt_unlock_file_does_not_stop_the_app()
    {
        var store = Store();
        store.Save("achievements", new JsonArray("not", "a", "mapping"));

        Assert.Empty(new AchievementTracker(store).UnlockedIds);
    }

    // One bad predicate must not cost the user every other unlock in the same session.
    [Fact]
    public void an_achievement_whose_check_explodes_is_skipped()
    {
        var broken = new Achievement("boom", "Boom", "explodes", "tip",
            Check: (_, _) => throw new DivideByZeroException());
        var tracker = new AchievementTracker(Store(), [broken, .. AchievementCatalogue.All]);

        var unlocked = tracker.Evaluate(Stats(e => e["total_dur_sec"] = 45 * 60), [Entry(e => e["total_dur_sec"] = 45 * 60)]);

        Assert.Contains("endurance_45", Ids(unlocked));
        Assert.DoesNotContain("boom", Ids(unlocked));
    }

    // --- tiers belong to one another ---

    [Fact]
    public void a_tiered_achievement_knows_its_track_and_level()
    {
        var endurance = AchievementCatalogue.All.Where(a => a.Track == "Endurance").ToList();

        Assert.Equal([1, 2, 3], endurance.Select(a => a.Level));
        Assert.Equal(["endurance_45", "endurance_90", "endurance_120"], Ids(endurance));
    }

    [Fact]
    public void a_standalone_achievement_has_no_track()
    {
        var edges = ById("edges_10");

        Assert.Null(edges.Track);
        Assert.Equal(0, edges.Level);
    }

    // Three levels of the same thing are one achievement with three stages, not three
    // achievements that happen to look alike.
    [Fact]
    public void grouping_puts_a_whole_track_in_one_tile()
    {
        var groups = AchievementCatalogue.Grouped();

        var endurance = groups.First(g => g[0].Id == "endurance_45");
        Assert.Equal([1, 2, 3], endurance.Select(a => a.Level));
    }

    [Fact]
    public void grouping_leaves_standalone_achievements_alone()
    {
        var groups = AchievementCatalogue.Grouped();

        var single = groups.First(g => g[0].Id == "edges_10");
        Assert.Single(single);
    }

    [Fact]
    public void grouping_loses_nothing_and_keeps_the_order()
    {
        var flat = AchievementCatalogue.Grouped().SelectMany(g => g).ToList();

        Assert.Equal(AchievementCatalogue.All, flat);
    }

    // The tile carries the track name, so the level only has to say which step it is.
    [Fact]
    public void a_tier_is_named_for_its_step_not_for_the_whole_track()
    {
        var endurance = AchievementCatalogue.All.Where(a => a.Track == "Endurance");

        Assert.Equal(["45 minutes", "90 minutes", "120 minutes"], endurance.Select(a => a.Name));
    }

    // --- fake-outs are a track, and surviving means surviving ---

    [Fact]
    public void the_fake_out_track_has_three_steps()
    {
        var fakes = AchievementCatalogue.All.Where(a => a.Track == "Not Falling For It").ToList();

        Assert.Equal([1, 2, 3], fakes.Select(a => a.Level));
        Assert.Equal(["fakeouts_1", "fakeouts_3", "fakeouts_5"], Ids(fakes));
    }

    private static Action<JsonObject> Fakes(int count, int fallenFor) => e =>
    {
        e["fakeout_count"] = count;
        e["fakeouts_fallen_for"] = fallenFor;
    };

    // The rule used to count the cues and ignore what you did about them, so "Not Falling For It"
    // was handed out to people who fell for every single one.
    [Fact]
    public void surviving_fake_outs_means_not_falling_for_them()
    {
        var unlocked = _tracker.Evaluate(Stats(Fakes(3, 3)), [Entry(Fakes(3, 3))]);

        Assert.DoesNotContain(Ids(unlocked), id => id.StartsWith("fakeouts", StringComparison.Ordinal));
    }

    [Fact]
    public void one_survived_fake_out_is_already_worth_something()
    {
        var unlocked = Ids(_tracker.Evaluate(Stats(Fakes(1, 0)), [Entry(Fakes(1, 0))]));

        Assert.Contains("fakeouts_1", unlocked);
        Assert.DoesNotContain("fakeouts_3", unlocked);
    }

    [Fact]
    public void five_survived_fake_outs_take_the_whole_track()
    {
        var unlocked = Ids(_tracker.Evaluate(Stats(Fakes(5, 0)), [Entry(Fakes(5, 0))]));

        Assert.Subset(unlocked.ToHashSet(), new HashSet<string> { "fakeouts_1", "fakeouts_3", "fakeouts_5" });
    }

    // A bar filling to the top on a session that earned nothing would be a lie.
    [Fact]
    public void a_session_you_were_fooled_in_makes_no_progress_at_all()
    {
        var (current, _) = ById("fakeouts_1").Progress!(Stats(Fakes(4, 1)), []);

        Assert.Equal(0, current);
    }

    // --- disobedience is discovered, never advertised ---

    // Listing "come anyway after being denied" as a goal is an invitation, not a record.
    [Fact]
    public void the_disobedience_track_is_secret()
    {
        var disobedient = AchievementCatalogue.All.Where(a => a.Track == "Couldn't Help It").ToList();

        Assert.NotEmpty(disobedient);
        Assert.All(disobedient, a => Assert.True(a.Secret));
    }

    [Fact]
    public void obedience_is_not_secret()
    {
        var obedient = AchievementCatalogue.All.Where(a => a.Track == "Good Boy").ToList();

        Assert.NotEmpty(obedient);
        Assert.DoesNotContain(obedient, a => a.Secret);
    }

    [Fact]
    public void a_secret_track_still_unlocks_normally() =>
        Assert.Contains("disobedient_1", Ids(_tracker.Evaluate(Stats(), [Entry(Outcomes("denied", "came"))])));

    // An unused SVG is dead weight in the bundle and, worse, a thing somebody redraws for nothing.
    [Fact]
    public void no_mark_is_shipped_that_nothing_uses()
    {
        var shipped = Directory.GetFiles(Repo.PathTo(AchievementCatalogue.IconDir), "*.svg")
            .Select(p => Path.GetFileNameWithoutExtension(p)!).ToHashSet();
        var used = AchievementCatalogue.All.Select(a => a.Icon).ToHashSet();

        Assert.Equal(used, shipped);
    }

    // Its condition gives the mechanic away: a goal that reads "let fake cues pass without acting
    // on one" tells the user fakes exist and that they should hesitate at every climax
    // announcement - which is the one thing a fake-out cannot survive.
    [Fact]
    public void the_fake_out_track_is_secret_too()
    {
        var fakes = AchievementCatalogue.All.Where(a => a.Track == "Not Falling For It").ToList();

        Assert.NotEmpty(fakes);
        Assert.All(fakes, a => Assert.True(a.Secret));
    }
}
