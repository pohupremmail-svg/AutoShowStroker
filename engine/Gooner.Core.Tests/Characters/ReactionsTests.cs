using Gooner.Core.Callouts;
using Gooner.Core.Characters;

namespace Gooner.Core.Tests.Characters;

// How the character's face answers a callout: an expression for what happened, shaded by the voice
// the line was written in.
public sealed class ReactionsTests
{
    private static readonly string[] Presets = ["happy", "angry", "sad", "relaxed", "surprised"];

    [Fact]
    public void every_trigger_has_a_reaction_in_a_vrm_preset()
    {
        foreach (var trigger in CalloutHandler.TriggerKeys)
        {
            var reaction = Reactions.For(trigger, null);
            Assert.Contains(reaction.Expression, Presets);
            Assert.InRange(reaction.Strength, 0.1, 1.0);
        }
    }

    // A fake climax speaks with the real one's lines until it is revealed, so it gets the real one's
    // face as well: nothing on screen may tell them apart.
    [Fact]
    public void the_climax_she_announces_is_met_with_delight()
    {
        Assert.Equal(new Reaction("happy", 1.0), Reactions.For("climax_real", null));
        Assert.Equal("happy", Reactions.For("fake_climax_reveal", null).Expression);
    }

    [Fact]
    public void a_shy_voice_is_gentler_and_never_cross()
    {
        var bold = Reactions.For("media_skipped", null);
        var shy = Reactions.For("media_skipped", "shy");

        Assert.Equal("angry", bold.Expression);
        Assert.Equal("sad", shy.Expression);
        Assert.True(shy.Strength < bold.Strength);
    }

    [Theory]
    [InlineData("dominant")]
    [InlineData("sadistic")]
    [InlineData("degrading")]
    [InlineData("hard_degrading")]
    public void a_commanding_voice_is_stern_where_others_are_at_ease(string tone)
    {
        Assert.Equal("relaxed", Reactions.For("pause_start", null).Expression);
        Assert.Equal("angry", Reactions.For("pause_start", tone).Expression);
    }

    [Fact]
    public void a_girlfriend_pouts_instead_of_glaring()
    {
        Assert.Equal("sad", Reactions.For("media_skipped", "girlfriend").Expression);
    }

    [Fact]
    public void an_unknown_trigger_gets_a_calm_face() =>
        Assert.Equal("relaxed", Reactions.For("no_such_trigger", null).Expression);
}
