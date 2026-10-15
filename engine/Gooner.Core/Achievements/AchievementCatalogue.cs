using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gooner.Core.Sessions;

namespace Gooner.Core.Achievements;

using Rule = (Func<JsonObject, IReadOnlyList<JsonObject>, bool> Check, Func<JsonObject, IReadOnlyList<JsonObject>, (double Current, double Target)> Progress);

// What the app makes of your sessions, beyond counting them. Port of the catalogue in
// src/achievements.py.
//
// Deliberately separate from ScoreTracker: that aggregates numbers, this judges them, and a rule
// can be retuned without touching the code that writes session_history.json. Entries written by
// older builds lack the newer keys - every reader here defaults rather than indexing, so a history
// from before outcomes were reported still counts towards everything it legitimately can.
public static class AchievementCatalogue
{
    // Relative to the repository (or the exported app's) root; the host resolves it.
    public const string IconDir = "res/icons/achievements";

    // Demanded -> what obeying it looks like when the user reports back. "real" is deliberately
    // absent: being told to come and coming is not obedience, it is doing what you were going to
    // do anyway, and letting it count would make the whole track free.
    public static readonly IReadOnlyDictionary<string, string> ObedientAnswer = new Dictionary<string, string>
    {
        ["denied"] = "stopped",
        ["ruined"] = "ruined",
    };

    public static string IconPath(Achievement achievement)
    {
        ArgumentNullException.ThrowIfNull(achievement);
        return $"{IconDir}/{achievement.Icon}.svg";
    }

    public static IReadOnlyList<Achievement> All { get; } =
    [
        .. Tiers(
            "endurance",
            "Endurance",
            t => $"{Minutes(t)} minutes",
            t => $"Last {Minutes(t)} minutes in a single session.",
            "candle",
            t => SessionAtLeast("total_dur_sec", t),
            [45 * 60, 90 * 60, 120 * 60],
            idFor: t => Minutes(t).ToString(CultureInfo.InvariantCulture)),
        .. Tiers(
            "beats",
            "Strokes In One Session",
            Spaced,
            t => $"Take {Spaced(t)} beats in a single session.",
            "note_run",
            t => SessionAtLeast("total_num_beat", t),
            [5_000, 12_000, 20_000]),
        .. Tiers(
            "lifetime_beats",
            "Strokes All Told",
            t => Suffix(t).ToUpperInvariant(),
            t => $"Take {Spaced(t)} beats across every session you have ever played.",
            "endless_loop",
            t => LifetimeAtLeast("total_num_beat", t),
            [100_000, 500_000, 2_000_000]),
        .. Tiers(
            "returner",
            "Back For More",
            t => $"{t} sessions",
            t => $"Come back and play {t} sessions.",
            "hooked",
            SessionsAtLeast,
            [5, 25, 100]),
        .. Tiers(
            "obedient",
            "Good Boy",
            t => t > 1 ? $"{t} times" : "once",
            t => t > 1
                ? $"Do as you are told {t} times when you are denied or told to ruin it. Being told to come and coming does not count."
                : "Do as you are told when you are denied or told to ruin it. Being told to come and coming does not count.",
            "collar",
            t => HistoryCountAtLeast(Obedient, t),
            [1, 10, 50]),
        .. Tiers(
            "disobedient",
            "Couldn't Help It",
            t => t > 1 ? $"{t} times" : "once",
            t => t > 1 ? $"Come anyway after being denied, {t} times." : "Come anyway after being denied.",
            "snapped_leash",
            t => HistoryCountAtLeast(Disobedient, t),
            [1, 10, 50],
            // Never advertised. Listing "come anyway after being denied" as a goal is an
            // invitation rather than a record of one - it is found, not aimed at.
            secret: true),
        .. Tiers(
            "fakeouts",
            "Not Falling For It",
            t => t > 1 ? $"{t} in one session" : "one",
            t => t > 1
                ? $"Let {t} fake climax cues pass in a single session without acting on one of them."
                : "Let a fake climax cue pass without acting on it.",
            "silent_bell",
            UnfooledBy,
            [1, 3, 5],
            // Secret for a different reason: the condition gives the mechanic away. Listed as a
            // goal it tells the user that fake cues exist and that the winning move is to hesitate
            // at every climax announcement - and a fake-out only works while it is
            // indistinguishable from the real thing.
            secret: true),
        Standalone(
            "edges_10",
            "Ten Times Close",
            "Reach your edge 10 times in one session and say so every time.",
            "gauge_max",
            SessionAtLeast("edge_count", 10)),
        new Achievement(
            "edges_none",
            "Never Asked",
            "Last 45 minutes without once reaching for the edge button.",
            "hourglass",
            (played, _) => OrZero(played["total_dur_sec"]) >= 45 * 60 && !PyJson.Truthy(played["edge_count"])),
        new Achievement(
            "replayed_a_session",
            "Again, From The Top",
            "",
            "endless_loop",
            (played, _) => PyJson.Truthy(played["was_replay"]),
            Secret: true),
    ];

    // The catalogue as tiles: a whole track is one entry, everything else is its own. Order is
    // preserved and nothing is dropped - the tiles are just how the same list is drawn.
    public static IReadOnlyList<IReadOnlyList<Achievement>> Grouped(IReadOnlyList<Achievement>? catalogue = null)
    {
        var tiles = new List<IReadOnlyList<Achievement>>();
        var current = new List<Achievement>();
        foreach (var achievement in catalogue ?? All)
        {
            if (achievement.Track is not null && current.Count > 0 && current[^1].Track == achievement.Track)
            {
                current.Add(achievement);
                continue;
            }
            if (current.Count > 0)
                tiles.Add(current);
            current = [achievement];
        }
        if (current.Count > 0)
            tiles.Add(current);
        return tiles;
    }

    // --- reading the history ---

    // Python's `entry.get(field) or 0`: missing, null and anything falsy read as 0, a bool as the
    // int it is. Anything else that is not a number throws, as comparing it would in Python - and
    // the tracker skips a rule that throws.
    internal static double OrZero(JsonNode? node)
    {
        if (node is JsonValue value && value.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
            return value.GetValueKind() == JsonValueKind.True ? 1 : 0;
        return PyJson.Truthy(node) ? PyJson.Number(node) : 0;
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? (string?)value : null;

    private static bool Obedient(JsonObject entry)
    {
        var demanded = Text(entry["climax_outcome"]);
        var reported = entry["reported_outcome"];
        return reported is not null && demanded is not null
            && ObedientAnswer.TryGetValue(demanded, out var answer) && Text(reported) == answer;
    }

    // Told to stop, or to ruin it, and came anyway.
    private static bool Disobedient(JsonObject entry) =>
        Text(entry["climax_outcome"]) is { } demanded && ObedientAnswer.ContainsKey(demanded)
        && Text(entry["reported_outcome"]) == "came";

    private static int Count(IReadOnlyList<JsonObject> history, Func<JsonObject, bool> predicate) =>
        history.Count(predicate);

    private static double Total(IReadOnlyList<JsonObject> history, string field) =>
        history.Sum(entry => OrZero(entry[field]));

    // --- rule builders, so the catalogue stays readable ---

    private static Rule SessionAtLeast(string field, double target) => (
        (played, _) => OrZero(played[field]) >= target,
        (played, _) => (Math.Min(OrZero(played[field]), target), target));

    private static Rule LifetimeAtLeast(string field, double target) => (
        (_, history) => Total(history, field) >= target,
        (_, history) => (Math.Min(Total(history, field), target), target));

    private static Rule SessionsAtLeast(int target) => (
        (_, history) => history.Count >= target,
        (_, history) => (Math.Min(history.Count, target), target));

    // Survived `target` fake cues in one session without acting on a single one. Counting the cues
    // alone was the old rule, and it handed "Not Falling For It" to people who fell for every one
    // of them. A session the user was fooled in shows no progress either - a bar filling to the
    // top on a session that earned nothing is a lie.
    private static Rule UnfooledBy(int target) => (
        (played, _) => OrZero(played["fakeout_count"]) >= target && !PyJson.Truthy(played["fakeouts_fallen_for"]),
        (played, _) => PyJson.Truthy(played["fakeouts_fallen_for"])
            ? (0, target)
            : (Math.Min(OrZero(played["fakeout_count"]), target), target));

    private static Rule HistoryCountAtLeast(Func<JsonObject, bool> predicate, int target) => (
        (_, history) => Count(history, predicate) >= target,
        (_, history) => (Math.Min(Count(history, predicate), target), target));

    // The levels of one track, in order. They share a name, a mark and a rule. idFor keeps the
    // stored id readable where the raw target is not: a 45 minute tier is "endurance_45", not
    // "endurance_2k" seconds. Ids end up in the user's data file, so they stay stable and legible.
    private static IEnumerable<Achievement> Tiers(
        string prefix, string track, Func<int, string> nameFor, Func<int, string> descriptionFor, string icon,
        Func<int, Rule> builder, int[] targets, Func<int, string>? idFor = null, bool secret = false)
    {
        idFor ??= Suffix;
        for (var i = 0; i < targets.Length; i++)
        {
            var target = targets[i];
            var (check, progress) = builder(target);
            yield return new Achievement(
                $"{prefix}_{idFor(target)}", nameFor(target), descriptionFor(target), icon, check, progress,
                Secret: secret, Track: track, Level: i + 1);
        }
    }

    private static Achievement Standalone(string id, string name, string description, string icon, Rule rule) =>
        new(id, name, description, icon, rule.Check, rule.Progress);

    private static string Suffix(int target) => target switch
    {
        >= 1_000_000 => $"{target / 1_000_000}m",
        >= 1000 => $"{target / 1000}k",
        _ => target.ToString(CultureInfo.InvariantCulture),
    };

    private static int Minutes(int seconds) => seconds / 60;

    // f"{t:,}".replace(",", " ") - thousands apart by a space.
    private static string Spaced(int value) => value.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ');
}
