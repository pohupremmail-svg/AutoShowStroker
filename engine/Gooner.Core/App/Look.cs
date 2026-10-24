using Gooner.Core.Characters;
using Gooner.Core.Localization;
using Gooner.Core.Settings;

namespace Gooner.Core.App;

// One option of a look setting: what is stored, and what the settings call it.
public sealed record LookOption(string Value, string Label);

// How the engine looks: which note track, which colours, how strong the effects, and what the
// Hypno Ring does with the picture around it. The Python app had one track and one palette, so
// none of this exists there. A choice is stored by name; one this build does not know - written by
// a later one - reads as the default rather than as nothing.
public sealed class Look : ISettingsOwner
{
    public static string SettingsGroup => "Look";

    public static IReadOnlyDictionary<string, object> Defaults { get; } = new Dictionary<string, object>
    {
        ["note_track"] = "highway",
        ["color_scheme"] = "neon",
        ["effects"] = "normal",
        ["focus_wanders"] = true,
        ["censor"] = "off",
        ["telepathy_frame"] = "oval",
        ["toy_color"] = ToyColours.Default,
    };

    // Every choice, in the order the settings offer it; the settings show the label translated.
    public static IReadOnlyDictionary<string, IReadOnlyList<LookOption>> Choices { get; } = new Dictionary<string, IReadOnlyList<LookOption>>
    {
        ["note_track"] = [new("highway", L10n.N("Highway")), new("ring", L10n.N("Hypno Ring")), new("stream", L10n.N("Stream")), new("tunnel", L10n.N("Tunnel"))],
        ["color_scheme"] = [new("neon", L10n.N("Neon Pink")), new("boudoir", L10n.N("Boudoir"))],
        ["effects"] = [new("subtle", L10n.N("Subtle")), new("normal", L10n.N("Normal")), new("wild", L10n.N("Wild"))],
        ["censor"] = [new("off", L10n.N("Clear")), new("blur", L10n.N("Blur")), new("mosaic", L10n.N("Mosaic")), new("dark", L10n.N("Dark"))],
        ["telepathy_frame"] = [new("oval", L10n.N("Neon Oval")), new("crt", L10n.N("Tube Screen"))],
        ["toy_color"] = [.. ToyColours.Names.Select(name => new LookOption(name, ToyColours.Labels[name]))],
    };

    public Look(ISettingsStore? settings)
    {
        NoteTrack = Choice(settings, "note_track");
        ColorScheme = Choice(settings, "color_scheme");
        Effects = Choice(settings, "effects");
        Censor = Choice(settings, "censor");
        TelepathyFrame = Choice(settings, "telepathy_frame");
        ToyColor = Choice(settings, "toy_color");
        var wanders = (bool)Defaults["focus_wanders"];
        FocusWanders = settings?.GetBool($"{SettingsGroup}/focus_wanders", wanders) ?? wanders;
    }

    private static string Choice(ISettingsStore? settings, string name)
    {
        var fallback = (string)Defaults[name];
        var stored = settings?.GetString($"{SettingsGroup}/{name}", fallback) ?? fallback;
        return Choices[name].Any(o => o.Value == stored) ? stored : fallback;
    }

    public string NoteTrack { get; set; }

    public string ColorScheme { get; set; }

    public string Effects { get; set; }

    // Whether the Hypno Ring drifts across the picture, drawing the eye with it.
    public bool FocusWanders { get; set; }

    // What the Hypno Ring does to the picture around it: nothing, or blurs, pixelates or darkens it.
    public string Censor { get; set; }

    // The window the character is seen in during a session: a neon oval, or an old tube screen.
    public string TelepathyFrame { get; set; }

    // The colour of the toy in her hand - the app's own; a model in res/props keeps its own colours.
    public string ToyColor { get; set; }

    // What the effects are scaled by: how many particles, how hard the shake and the flashes.
    public double EffectScale => Effects switch
    {
        "subtle" => 0.5,
        "wild" => 1.7,
        _ => 1.0,
    };
}
