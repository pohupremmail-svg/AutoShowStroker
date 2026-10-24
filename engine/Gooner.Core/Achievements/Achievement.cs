using System.Text.Json.Nodes;

namespace Gooner.Core.Achievements;

// A rule over the session just played and the whole history behind it, that session included.
// Both are the JsonObjects ScoreTracker delivers and stores. Port of the Achievement NamedTuple in
// src/achievements.py.
//
// Progress gives (current, target) for the ones worth working towards; null where a number would
// be meaningless ("came anyway") rather than merely absent. Secret ones show as ??? until they
// fire. Three levels of the same thing are one achievement with three stages: Track is the tile's
// name and Level the step within it - null/0 for a standalone one.
public sealed record Achievement(
    string Id,
    string Name,
    string Description,
    string Icon,
    Func<JsonObject, IReadOnlyList<JsonObject>, bool> Check,
    Func<JsonObject, IReadOnlyList<JsonObject>, (double Current, double Target)>? Progress = null,
    bool Secret = false,
    string? Track = null,
    int Level = 0);
