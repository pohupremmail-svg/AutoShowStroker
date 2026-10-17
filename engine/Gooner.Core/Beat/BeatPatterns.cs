using Gooner.Core.Localization;

namespace Gooner.Core.Beat;

// The built-in rhythms, port of BeatHandler.BEAT_PATTERNS_MAP. Each step is the weight of one
// note; a negative step is a silent step of the same length. Order matters - it is the order
// the settings list them in.
public static class BeatPatterns
{
    public static readonly OrderedDictionary<string, int[]> BuiltIn = new()
    {
        // --- Standard & Simple ---
        [L10n.N("Standard Beat")] = [1],

        // --- Grooves & Swings ---
        [L10n.N("Quick Swing")] = [1, 2, 2, -1, -1],
        [L10n.N("Simple Bounce")] = [1, 1, -1, 1, 1, -1],
        [L10n.N("Double Tap")] = [2, 2, -2, 2, 2, -2],
        [L10n.N("Syncopated 4/4")] = [1, -1, 1, -1, 1, 1, 1],

        // --- Long Pauses & Gaps ---
        [L10n.N("Slow Pulse")] = [1, 1, -1, -1, -1, -1, -1, -1],
        [L10n.N("Held Breath")] = [3, -1, -1, -1, -1, -1, 3],
        [L10n.N("Double Tap Pause")] = [2, 2, -1, -1, -1, -1],

        // --- Complex & Off-Beat ---
        [L10n.N("Delayed Swing")] = [1, -2, 2, -2, 1, -2, 2, -2],
        [L10n.N("Triple Quick Tap")] = [1, 4, 4, -2, -2],
        [L10n.N("Missing Third")] = [1, 1, -3, 1],

        // --- Accelerating & Decelerating ---
        [L10n.N("Build Up")] = [1, 2, 3, 4, -4, -4],
        [L10n.N("Slow Down")] = [4, 3, 2, 1, -2],
        [L10n.N("Speed Change")] = [1, 1, 1, 1, 2, 2, 2, 2],
        [L10n.N("Suspense Build")] = [2, -4, -3, -2, -1, 3],
    };

    // A built-in rhythm is shown under its translated name, one the user named as they named it.
    // Either is stored, recorded and replayed under the name it has here.
    public static string DisplayName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return BuiltIn.ContainsKey(name) ? L10n.T(name) : name;
    }
}
