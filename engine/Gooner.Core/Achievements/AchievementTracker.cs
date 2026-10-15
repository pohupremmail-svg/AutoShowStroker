using System.Globalization;
using System.Text.Json.Nodes;
using Gooner.Core.Data;
using Gooner.Core.Logging;

namespace Gooner.Core.Achievements;

// Holds what has been unlocked, and decides what the session just played adds to it. Port of
// AchievementTracker in src/achievements.py.
//
// The data store is optional for the same reason ScoreTracker's is: a tracker without one simply
// keeps nothing, rather than refusing to work.
public sealed class AchievementTracker
{
    public const string StoreKey = "achievements";
    private const string Log = "achievements";

    private readonly UserDataStore? _dataStore;
    private readonly TimeProvider _time;
    // id -> when it was unlocked, as stored; kept as read so a hand-edited file round-trips.
    private JsonObject _unlocked;

    public AchievementTracker(UserDataStore? dataStore, IReadOnlyList<Achievement>? catalogue = null, TimeProvider? time = null)
    {
        _dataStore = dataStore;
        Catalogue = catalogue ?? AchievementCatalogue.All;
        _time = time ?? TimeProvider.System;
        _unlocked = Load();
    }

    public IReadOnlyList<Achievement> Catalogue { get; }

    // --- reading ---

    public IReadOnlyList<string> UnlockedIds => [.. _unlocked.Select(kv => kv.Key)];

    public bool IsUnlocked(string achievementId) => _unlocked.ContainsKey(achievementId);

    public string? UnlockedAt(string achievementId) =>
        _unlocked.TryGetPropertyValue(achievementId, out var at) ? at?.ToString() : null;

    // (current, target) for a locked achievement, or null when it has no meaningful number to show.
    public static (double Current, double Target)? ProgressFor(Achievement achievement, JsonObject played, IReadOnlyList<JsonObject> history)
    {
        ArgumentNullException.ThrowIfNull(achievement);
        if (achievement.Progress is null) return null;
        try
        {
            return achievement.Progress(played, history);
        }
#pragma warning disable CA1031 // A progress bar is never worth taking the app down for.
        catch (Exception e)
#pragma warning restore CA1031
        {
            AppLog.Error(Log, "Could not read progress for achievement {0}: {1}", achievement.Id, e);
            return null;
        }
    }

    // --- judging ---

    // Everything the session just played has newly earned, in catalogue order. Called after the
    // session is already in the history, so a rule can count it.
    public IReadOnlyList<Achievement> Evaluate(JsonObject played, IReadOnlyList<JsonObject> history)
    {
        var newly = new List<Achievement>();
        foreach (var achievement in Catalogue)
        {
            if (IsUnlocked(achievement.Id)) continue;
            bool earned;
            try
            {
                earned = achievement.Check(played, history);
            }
#pragma warning disable CA1031 // One bad rule must not cost the user every other unlock of the session.
            catch (Exception e)
#pragma warning restore CA1031
            {
                AppLog.Error(Log, "Achievement {0} could not be evaluated: {1}", achievement.Id, e);
                continue;
            }
            if (earned)
            {
                _unlocked[achievement.Id] = _time.GetLocalNow().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                newly.Add(achievement);
            }
        }
        if (newly.Count > 0)
        {
            AppLog.Info(Log, "Achievements unlocked: {0}", string.Join(", ", newly.Select(a => a.Id)));
            _dataStore?.Save(StoreKey, _unlocked.DeepClone());
        }
        return newly;
    }

    public void Clear()
    {
        _unlocked = [];
        _dataStore?.Delete(StoreKey);
    }

    // --- storage ---

    private JsonObject Load()
    {
        switch (_dataStore?.Load(StoreKey))
        {
            case null:
                return [];
            case JsonObject stored:
                return stored.DeepClone().AsObject();
            default:
                AppLog.Warning(Log, "The achievements file did not contain a mapping - ignoring it.");
                return [];
        }
    }
}
