using System.Numerics;
using Gooner.Core.App;
using Gooner.Core.Characters;

namespace Gooner.Core.Tests.Characters;

// The colours the app's own toy comes in, picked under Settings > Look: skin from dark to rosy, and
// colours no skin has - an ogre's, an orc's, pink, blue, purple (the user's list, 2026-10-24).
public sealed class ToyColoursTests
{
    private static float Luma(Vector3 c) => 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;

    [Fact]
    public void skin_runs_from_dark_to_rosy()
    {
        var skin = ToyColours.Skin.Select(name => Luma(ToyColours.Of(name).Body)).ToList();
        Assert.Equal(["ebony", "dark", "brown", "tan", "olive", "fair", "rosy"], ToyColours.Skin);
        for (var i = 1; i < skin.Count; i++) Assert.True(skin[i] > skin[i - 1], $"{ToyColours.Skin[i]} is not lighter than {ToyColours.Skin[i - 1]}");
    }

    [Fact]
    public void and_then_the_colours_no_skin_has() =>
        Assert.Equal(["ogre", "orc", "pink", "blue", "purple"], ToyColours.Fantasy);

    // The settings offer exactly these, skin first, under the names they are stored by.
    [Fact]
    public void the_settings_offer_every_one()
    {
        Assert.Equal([.. ToyColours.Skin, .. ToyColours.Fantasy], Look.Choices["toy_color"].Select(o => o.Value));
        Assert.Equal("fair", Look.Defaults["toy_color"]);
    }

    // In every one the head is a shade of its own and the veins are darker than the skin they run in.
    [Fact]
    public void every_one_has_a_head_and_veins_that_show()
    {
        foreach (var name in ToyColours.Names)
        {
            var tint = ToyColours.Of(name);
            Assert.True(Vector3.Distance(tint.Head, tint.Body) > 0.05f, $"{name}: the head does not show");
            Assert.True(Luma(tint.Vein) < Luma(tint.Body), $"{name}: the veins do not show");
            foreach (var c in new[] { tint.Body, tint.BodyLow, tint.Head, tint.Vein, tint.Sac })
                Assert.True(c.X is >= 0 and <= 1 && c.Y is >= 0 and <= 1 && c.Z is >= 0 and <= 1, name);
        }
    }

    // How a vertex is coloured from what part it is - what the toy's shader does, here to be tested and
    // to render a preview with.
    [Fact]
    public void a_part_takes_its_colour()
    {
        var tint = ToyColours.Of("orc");
        Assert.Equal(tint.Body, tint.Albedo(Vector4.Zero));
        Assert.Equal(tint.Head, tint.Albedo(new Vector4(1, 0, 0, 0)));
        Assert.Equal(tint.Sac, tint.Albedo(new Vector4(0, 0, 1, 0)));
        Assert.Equal(tint.BodyLow, tint.Albedo(new Vector4(0, 0, 0, 1)));
    }

    // A name this build does not know - written by a later one - is the default.
    [Fact]
    public void an_unknown_name_is_the_default() =>
        Assert.Equal(ToyColours.Of("fair"), ToyColours.Of("chrome"));
}
