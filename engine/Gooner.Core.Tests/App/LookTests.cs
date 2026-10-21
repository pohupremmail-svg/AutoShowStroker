using Gooner.Core.App;
using Gooner.Core.Data;
using Gooner.Core.Settings;

namespace Gooner.Core.Tests.App;

// The engine's own look: which note track, which colours, how strong the effects, and what the
// Hypno Ring does with the picture around it. None of it exists on the Python side, so there is
// nothing to port; these pin what the window and the settings rely on.
public sealed class LookTests : IDisposable
{
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-look-");

    public void Dispose() => _tmp.Delete(recursive: true);

    private JsonSettingsStore Store() => new(new UserDataStore(_tmp.FullName));

    [Fact]
    public void unset_it_is_the_highway_in_neon_at_normal_strength()
    {
        var look = new Look(null);

        Assert.Equal("highway", look.NoteTrack);
        Assert.Equal("neon", look.ColorScheme);
        Assert.Equal("normal", look.Effects);
        Assert.True(look.FocusWanders);
        Assert.Equal("off", look.Censor);
        Assert.Equal("oval", look.TelepathyFrame);
    }

    [Fact]
    public void what_was_saved_is_what_it_reads()
    {
        var store = Store();
        store.SetValue("Look/note_track", "tunnel");
        store.SetValue("Look/color_scheme", "boudoir");
        store.SetValue("Look/effects", "subtle");
        store.SetValue("Look/focus_wanders", false);
        store.SetValue("Look/censor", "mosaic");
        store.SetValue("Look/telepathy_frame", "crt");

        var look = new Look(store);

        Assert.Equal("tunnel", look.NoteTrack);
        Assert.Equal("boudoir", look.ColorScheme);
        Assert.Equal("subtle", look.Effects);
        Assert.False(look.FocusWanders);
        Assert.Equal("mosaic", look.Censor);
        Assert.Equal("crt", look.TelepathyFrame);
    }

    // A later build may know more of them; this one reads what it cannot draw as its default.
    [Fact]
    public void a_choice_it_does_not_know_reads_as_the_default()
    {
        var store = Store();
        store.SetValue("Look/note_track", "spiral");
        store.SetValue("Look/censor", "pixels");

        var look = new Look(store);

        Assert.Equal("highway", look.NoteTrack);
        Assert.Equal("off", look.Censor);
    }

    [Fact]
    public void every_default_is_one_of_its_own_choices()
    {
        foreach (var (key, options) in Look.Choices)
            Assert.Contains(Look.Defaults[key], options.Select(o => (object)o.Value));
    }

    [Fact]
    public void the_four_note_tracks_are_offered_in_order()
    {
        Assert.Equal(["highway", "ring", "stream", "tunnel"], Look.Choices["note_track"].Select(o => o.Value));
    }

    [Fact]
    public void the_effects_scale_with_their_strength()
    {
        var look = new Look(null) { Effects = "subtle" };
        var subtle = look.EffectScale;
        look.Effects = "normal";
        var normal = look.EffectScale;
        look.Effects = "wild";

        Assert.Equal(1.0, normal);
        Assert.True(subtle < normal && normal < look.EffectScale);
    }
}
