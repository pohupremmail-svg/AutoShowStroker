namespace Gooner.Core.Characters;

// A face to answer a callout with: a VRM preset expression (happy, angry, sad, relaxed, surprised) and
// how strongly.
public sealed record Reaction(string Expression, double Strength);

// What the character's face does when a line is said: an expression for what happened, shaded by the
// voice it was written in. A fake climax speaks with climax_real's lines until its reveal, so it gets
// the real climax's face too - nothing on screen may tell them apart.
public static class Reactions
{
    private static readonly Reaction Calm = new("relaxed", 0.4);

    private static readonly Dictionary<string, Reaction> ByTrigger = new()
    {
        ["session_started"] = new("happy", 0.8),
        ["beat_change_general"] = new("relaxed", 0.6),
        ["beat_change_faster"] = new("happy", 0.7),
        ["beat_change_slower"] = new("relaxed", 0.8),
        ["pause_start"] = new("relaxed", 0.9),
        ["pause_end"] = new("happy", 0.8),
        ["media_skipped"] = new("angry", 0.5),
        ["media_repeated"] = new("happy", 0.6),
        ["climax_real"] = new("happy", 1.0),
        ["climax_ruined"] = new("surprised", 0.7),
        ["climax_denied"] = new("happy", 0.9),
        ["fake_climax_reveal"] = new("happy", 1.0),
        ["fake_climax_fell_for"] = new("surprised", 0.8),
        ["edge_reached"] = new("relaxed", 0.8),
    };

    private static readonly HashSet<string> Commanding = ["dominant", "sadistic", "degrading", "hard_degrading"];
    private static readonly HashSet<string> Tender = ["girlfriend", "nurturing"];

    public static Reaction For(string trigger, string? tone)
    {
        var reaction = ByTrigger.GetValueOrDefault(trigger, Calm);
        if (tone == "shy")
            // Never cross, and nothing at full strength.
            return new(reaction.Expression == "angry" ? "sad" : reaction.Expression, reaction.Strength * 0.6);
        if (tone is not null && Commanding.Contains(tone) && reaction.Expression == "relaxed")
            return new("angry", 0.4);
        if (tone is not null && Tender.Contains(tone) && reaction.Expression == "angry")
            return new("sad", 0.5);
        return reaction;
    }
}
