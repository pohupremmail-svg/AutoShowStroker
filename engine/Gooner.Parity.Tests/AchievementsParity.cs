using System.Text.Json.Nodes;
using Gooner.Core.Achievements;
using Gooner.Core.Data;

namespace Gooner.Parity.Tests;

// src/achievements.py vs Gooner.Core.Achievements: the catalogue word for word, its tiles, and
// what both trackers make of the same sessions - including the odd values a hand-edited history
// can hold.
public class AchievementsParity
{
    private const string Module = "achievements";

    public static IEnumerable<object[]> Evaluations() => Golden.Cases(Module, "evaluate");

    [Fact]
    public void catalogue()
    {
        var expected = Golden.Load(Module)["catalogue"]!;
        var actual = new JsonArray([.. AchievementCatalogue.All.Select(a => (JsonNode)new JsonObject
        {
            ["id"] = a.Id,
            ["name"] = a.Name,
            ["description"] = a.Description,
            ["icon"] = a.Icon,
            ["secret"] = a.Secret,
            ["track"] = a.Track,
            ["level"] = a.Level,
            ["has_progress"] = a.Progress is not null,
        })]);
        Assert.True(Golden.Equivalent(expected, actual), $"expected {expected.ToJsonString()}\nactual   {actual.ToJsonString()}");
    }

    [Fact]
    public void grouped()
    {
        var expected = Golden.Load(Module)["grouped"]!;
        var actual = new JsonArray([.. AchievementCatalogue.Grouped().Select(tile => (JsonNode)new JsonArray([.. tile.Select(a => (JsonNode)a.Id)]))]);
        Assert.True(Golden.Equivalent(expected, actual), $"expected {expected.ToJsonString()}\nactual   {actual.ToJsonString()}");
    }

    [Theory]
    [MemberData(nameof(Evaluations))]
    public void evaluate(string name, string json)
    {
        var c = JsonNode.Parse(json)!;
        var expected = c["expected"]!;
        var actual = Judged(c["played"]!.AsObject(), [.. c["history"]!.AsArray().Select(e => e!.AsObject())], c["stored"]);
        var message = $"{name}:\nexpected {expected.ToJsonString()}\nactual   {actual.ToJsonString()}";
        if (Golden.Allowed(Module + ".evaluate", name) is { } reason)
        {
            // A listed deviation is about the progress figures only: what unlocks still has to
            // agree, and the entry goes stale once the figures do too.
            Assert.True(Golden.Equivalent(expected["newly"], actual["newly"]) && Golden.Equivalent(expected["unlocked"], actual["unlocked"]), message);
            Assert.False(Golden.Equivalent(expected, actual), $"'{name}' is allowlisted but no longer differs: {reason}");
            return;
        }
        Assert.True(Golden.Equivalent(expected, actual), message);
    }

    // Mirrors parity/generate.py's _judged().
    private static JsonObject Judged(JsonObject played, List<JsonObject> history, JsonNode? stored)
    {
        var dir = Directory.CreateTempSubdirectory("gooner-parity-achievements-");
        try
        {
            var store = new UserDataStore(dir.FullName);
            if (stored is not null)
                store.Save(AchievementTracker.StoreKey, stored.DeepClone());
            var tracker = new AchievementTracker(store);
            var newly = tracker.Evaluate(played, history).Select(a => (JsonNode)a.Id);
            var progress = new JsonObject();
            foreach (var a in AchievementCatalogue.All)
                progress[a.Id] = AchievementTracker.ProgressFor(a, played, history) is { } p ? new JsonArray(p.Current, p.Target) : null;
            return new JsonObject
            {
                ["newly"] = new JsonArray([.. newly]),
                ["progress"] = progress,
                ["unlocked"] = new JsonArray([.. tracker.UnlockedIds.Order(StringComparer.Ordinal).Select(id => (JsonNode)id)]),
            };
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
