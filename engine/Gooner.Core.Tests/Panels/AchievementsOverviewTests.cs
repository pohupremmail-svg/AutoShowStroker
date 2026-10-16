using System.Text.Json.Nodes;
using Gooner.Core.Achievements;
using Gooner.Core.Panels;

namespace Gooner.Core.Tests.Panels;

// Port of tests/test_achievements_dialog.py: the achievement explorer - everything there is, and
// what has actually been earned - without its widgets. The tinting of the marks is the engine's
// (an image operation); what is left of those tests is which state a mark is drawn in.
public sealed class AchievementsOverviewTests
{
    private readonly AchievementTracker _tracker = new(dataStore: null);

    private static JsonObject Entry(double? totalDurSec = null)
    {
        var entry = new JsonObject
        {
            ["ended_at"] = "2026-09-17 20:00", ["total_dur_sec"] = 600.0, ["total_num_beat"] = 1200,
            ["average_beat_speed_active"] = 2.0, ["fakeout_count"] = 0, ["climax_outcome"] = null,
            ["reported_outcome"] = null, ["fakeouts_fallen_for"] = 0, ["edge_count"] = 0, ["was_replay"] = false,
        };
        if (totalDurSec is { } dur) entry["total_dur_sec"] = dur;
        return entry;
    }

    private AchievementsOverview Overview(params JsonObject[] history) => new(_tracker, history);

    // --- what is listed ---

    // The whole complaint this answers: you could only ever see an achievement by getting it.
    [Fact]
    public void everything_is_listed_whether_you_have_it_or_not()
    {
        var overview = Overview();

        Assert.Equal(AchievementCatalogue.Grouped().Count, overview.Tiles.Count);
        Assert.True(overview.Tiles.Count < AchievementCatalogue.All.Count); // tracks are folded into one tile
    }

    [Fact]
    public void the_summary_counts_what_is_earned()
    {
        _tracker.MarkUnlocked("endurance_45", "2026-09-17 21:00");

        Assert.Equal($"1 of {AchievementCatalogue.All.Count} earned", Overview().Summary);
    }

    [Fact]
    public void an_earned_achievement_shows_when_you_earned_it()
    {
        _tracker.Evaluate(Entry(45 * 60), [Entry(45 * 60)]);

        var tile = Overview(Entry(45 * 60)).TileFor("endurance_45")!;

        Assert.True(tile.Unlocked);
        Assert.Contains(_tracker.UnlockedAt("endurance_45")!, tile.Status);
    }

    [Fact]
    public void a_locked_achievement_says_what_it_wants()
    {
        var tile = Overview().TileFor("endurance_45")!;

        Assert.False(tile.Unlocked);
        Assert.Contains("45 minutes", tile.Detail);
    }

    [Fact]
    public void a_tile_carries_the_track_name_not_the_level_name() =>
        Assert.Equal("Endurance", Overview().TileFor("endurance_90")!.Title);

    [Fact]
    public void a_tile_shows_one_pip_per_level()
    {
        _tracker.MarkUnlocked("endurance_45", "2026-09-17 21:00");

        var tile = Overview().TileFor("endurance_120")!;

        Assert.Equal(1, tile.LevelsEarned);
        Assert.Equal(3, tile.LevelCount);
        Assert.Equal("●○○", tile.Pips);
    }

    // Naming the one already earned would be telling the user what they know.
    [Fact]
    public void a_tile_points_at_the_next_level_you_have_not_reached()
    {
        _tracker.MarkUnlocked("endurance_45", "2026-09-17 21:00");

        var tile = Overview(Entry(50 * 60)).TileFor("endurance_45")!;

        Assert.Contains("90 minutes", tile.Detail);
        Assert.Equal(90 * 60, tile.Progress!.Value.Target);
    }

    [Fact]
    public void a_finished_track_stops_asking_for_more()
    {
        foreach (var level in new[] { "endurance_45", "endurance_90", "endurance_120" })
            _tracker.MarkUnlocked(level, "2026-09-17 21:00");

        var tile = Overview().TileFor("endurance_45")!;

        Assert.Equal(tile.LevelCount, tile.LevelsEarned);
        Assert.Null(tile.Progress);
        Assert.Equal("Level 3 of 3 - earned 2026-09-17 21:00", tile.Status);
    }

    [Fact]
    public void a_standalone_achievement_has_no_level_pips()
    {
        var tile = Overview().TileFor("edges_10")!;

        Assert.Equal(1, tile.LevelCount);
        Assert.Null(tile.Pips);
    }

    [Fact]
    public void a_standalone_achievement_says_only_when_it_was_earned()
    {
        _tracker.MarkUnlocked("edges_10", "2026-09-18 22:00");

        Assert.Equal("Earned 2026-09-18 22:00", Overview().TileFor("edges_10")!.Status);
    }

    [Fact]
    public void a_locked_achievement_shows_how_far_along_you_are()
    {
        var tile = Overview(Entry(20 * 60)).TileFor("endurance_45")!;

        Assert.Equal((20 * 60, 45 * 60), tile.Progress);
        Assert.Equal("", tile.Status);
    }

    // A per-session rule has to be measured against a session, and the last one played is the
    // only honest choice.
    [Fact]
    public void progress_is_read_from_the_most_recent_session() =>
        Assert.Equal(5 * 60, Overview(Entry(40 * 60), Entry(5 * 60)).TileFor("endurance_45")!.Progress!.Value.Current);

    [Fact]
    public void an_empty_history_still_opens() =>
        Assert.Equal(0, Overview().TileFor("endurance_45")!.Progress!.Value.Current);

    // --- secrets ---

    private static Achievement FirstSecret(bool tracked = false) =>
        AchievementCatalogue.All.First(a => a.Secret && (!tracked || a.Track is not null));

    [Fact]
    public void a_secret_achievement_keeps_its_name_to_itself()
    {
        var secret = FirstSecret();

        var tile = Overview().TileFor(secret.Id)!;

        Assert.Equal(AchievementsOverview.SecretTitle, tile.Title);
        Assert.DoesNotContain(secret.Name, tile.Title);
        Assert.Equal("Not everything announces itself in advance.", tile.Detail);
        Assert.Null(tile.Progress);
    }

    [Fact]
    public void a_secret_achievement_gives_itself_up_once_earned()
    {
        var secret = FirstSecret();
        _tracker.MarkUnlocked(secret.Id, "2026-09-17 21:00");

        Assert.Equal(secret.Track ?? secret.Name, Overview().TileFor(secret.Id)!.Title);
    }

    // The pips would give away that there is more of it, which is half of what is secret.
    [Fact]
    public void a_secret_track_hides_how_many_steps_it_has()
    {
        var tile = Overview().TileFor(FirstSecret(tracked: true).Id)!;

        Assert.Equal(AchievementsOverview.SecretTitle, tile.Title);
        Assert.Null(tile.Pips);
    }

    [Fact]
    public void a_secret_track_shows_its_steps_once_it_is_out()
    {
        var secret = FirstSecret(tracked: true);
        _tracker.MarkUnlocked(secret.Id, "2026-09-17 21:00");

        var tile = Overview().TileFor(secret.Id)!;

        Assert.NotNull(tile.Pips);
        Assert.Equal(1, tile.LevelsEarned);
        Assert.Equal(3, tile.LevelCount);
    }

    // --- the marks ---

    [Fact]
    public void a_mark_lights_up_as_soon_as_any_level_is_earned()
    {
        _tracker.MarkUnlocked("endurance_45", "2026-09-17 21:00");
        var overview = Overview();

        Assert.True(overview.TileFor("endurance_45")!.Unlocked);
        Assert.False(overview.TileFor("edges_10")!.Unlocked);
    }

    [Fact]
    public void a_tile_names_the_mark_it_is_drawn_with() =>
        Assert.Equal(AchievementCatalogue.IconPath(AchievementCatalogue.All[0]), Overview().Tiles[0].IconPath);
}
