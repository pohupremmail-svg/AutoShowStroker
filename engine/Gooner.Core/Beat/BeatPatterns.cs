namespace Gooner.Core.Beat;

// The built-in rhythms, port of BeatHandler.BEAT_PATTERNS_MAP. Each step is the weight of one
// note; a negative step is a silent step of the same length. Order matters - it is the order
// the settings list them in.
public static class BeatPatterns
{
    public static readonly OrderedDictionary<string, int[]> BuiltIn = new()
    {
        // --- Standard & Simple ---
        ["Standard Beat"] = [1],

        // --- Grooves & Swings ---
        ["Quick Swing"] = [1, 2, 2, -1, -1],
        ["Simple Bounce"] = [1, 1, -1, 1, 1, -1],
        ["Double Tap"] = [2, 2, -2, 2, 2, -2],
        ["Syncopated 4/4"] = [1, -1, 1, -1, 1, 1, 1],

        // --- Long Pauses & Gaps ---
        ["Slow Pulse"] = [1, 1, -1, -1, -1, -1, -1, -1],
        ["Held Breath"] = [3, -1, -1, -1, -1, -1, 3],
        ["Double Tap Pause"] = [2, 2, -1, -1, -1, -1],

        // --- Complex & Off-Beat ---
        ["Delayed Swing"] = [1, -2, 2, -2, 1, -2, 2, -2],
        ["Triple Quick Tap"] = [1, 4, 4, -2, -2],
        ["Missing Third"] = [1, 1, -3, 1],

        // --- Accelerating & Decelerating ---
        ["Build Up"] = [1, 2, 3, 4, -4, -4],
        ["Slow Down"] = [4, 3, 2, 1, -2],
        ["Speed Change"] = [1, 1, 1, 1, 2, 2, 2, 2],
        ["Suspense Build"] = [2, -4, -3, -2, -1, 3],
    };
}
