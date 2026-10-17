using System.Text.Json.Nodes;
using Gooner.Core.Achievements;
using Gooner.Core.Localization;

namespace Gooner.Core.Panels;

// Port of AchievementCard in src/AchievementsDialog.py: one tile - a mark, a name, how many of its
// levels are earned, and what the next one wants.
//
// A tiered track is a single achievement with three stages rather than three achievements that
// happen to look alike, so it gets one tile with three pips. A standalone one is the same tile
// with a single stage.
public sealed class AchievementTile
{
    public const char PipEarned = '●';
    public const char PipLocked = '○';

    internal AchievementTile(IReadOnlyList<Achievement> levels, AchievementTracker tracker, JsonObject played,
        IReadOnlyList<JsonObject> history, string secretTitle)
    {
        Levels = levels;
        var achievement = levels[0];
        var earned = levels.Where(a => tracker.IsUnlocked(a.Id)).ToList();
        LevelCount = levels.Count;
        LevelsEarned = earned.Count;
        Unlocked = earned.Count > 0;
        IconPath = AchievementCatalogue.IconPath(achievement);
        var next = levels.FirstOrDefault(a => !tracker.IsUnlocked(a.Id));
        // A secret stays hidden only while nothing of it is earned. A secret *track* hides its steps
        // as well until then: the pips would give away that there is more of it, which is half of
        // what was being kept back.
        var hidden = achievement.Secret && !Unlocked;

        Title = hidden ? secretTitle : L10n.T(achievement.Track ?? achievement.Name);
        Pips = LevelCount > 1 && !hidden
            ? new string(PipEarned, LevelsEarned) + new string(PipLocked, LevelCount - LevelsEarned)
            : null;
        Detail = hidden ? L10n.T("Not everything announces itself in advance.") : L10n.T((next ?? levels[^1]).Description);
        // The highest level's date, which is the most recent one: levels are only ever earned in
        // order, since a higher one implies every lower one.
        var when = earned.Count > 0 ? tracker.UnlockedAt(earned[^1].Id) ?? "" : "";
        Status = earned.Count == 0 ? ""
            : LevelCount == 1 ? L10n.F("Earned {0}", when)
            : L10n.F("Level {0} of {1} - earned {2}", LevelsEarned, LevelCount, when);
        if (next is not null && !hidden && AchievementTracker.ProgressFor(next, played, history) is { } progress)
            Progress = ((int)progress.Current, (int)progress.Target);
    }

    public IReadOnlyList<Achievement> Levels { get; }

    public int LevelCount { get; }

    public int LevelsEarned { get; }

    // Lit - in the accent, with its glow - as soon as any level is earned.
    public bool Unlocked { get; }

    public string IconPath { get; }

    public string Title { get; }

    // One dot per level, filled for the ones earned; null for a standalone one or a hidden track.
    public string? Pips { get; }

    // What the next level wants; the last level's text once the track is finished.
    public string Detail { get; }

    // When the highest earned level was earned, and how far up the track that is; empty before.
    public string Status { get; }

    // How far along the next level is, as the bar shows it; null when there is no next level or no
    // meaningful number for it.
    public (int Current, int Target)? Progress { get; }
}

// Port of AchievementsDialog: everything there is to earn, earned or not.
//
// The point of showing the locked ones at all: until it existed an achievement could only be seen
// by getting it, which made them discoveries rather than goals. Progress is read against the most
// recent session, because a per-session rule has to be measured against a session and the last
// one played is the only honest choice.
public sealed class AchievementsOverview
{
    public const string SecretTitle = "???";

    public AchievementsOverview(AchievementTracker tracker, IReadOnlyList<JsonObject> history)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(history);
        var latest = history.Count > 0 ? history[^1] : [];
        Tiles = [.. AchievementCatalogue.Grouped(tracker.Catalogue).Select(levels => new AchievementTile(levels, tracker, latest, history, SecretTitle))];
        Summary = L10n.F("{0} of {1} earned", tracker.Catalogue.Count(a => tracker.IsUnlocked(a.Id)), tracker.Catalogue.Count);
    }

    public string Summary { get; }

    public IReadOnlyList<AchievementTile> Tiles { get; }

    // The tile an achievement lives on - a whole track shares one.
    public AchievementTile? TileFor(string achievementId) =>
        Tiles.FirstOrDefault(tile => tile.Levels.Any(a => a.Id == achievementId));
}
