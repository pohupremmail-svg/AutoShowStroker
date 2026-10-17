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
    };

    // Every choice, in the order the settings offer it.
    public static IReadOnlyDictionary<string, IReadOnlyList<LookOption>> Choices { get; } = new Dictionary<string, IReadOnlyList<LookOption>>
    {
        ["note_track"] = [new("highway", "Highway"), new("ring", "Hypno Ring"), new("stream", "Stream"), new("tunnel", "Tunnel")],
        ["color_scheme"] = [new("neon", "Neon Pink"), new("boudoir", "Boudoir")],
        ["effects"] = [new("subtle", "Subtle"), new("normal", "Normal"), new("wild", "Wild")],
        ["censor"] = [new("off", "Clear"), new("blur", "Blur"), new("mosaic", "Mosaic"), new("dark", "Dark")],
    };

    public Look(ISettingsStore? settings)
    {
        NoteTrack = Choice(settings, "note_track");
        ColorScheme = Choice(settings, "color_scheme");
        Effects = Choice(settings, "effects");
        Censor = Choice(settings, "censor");
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

    // What the effects are scaled by: how many particles, how hard the shake and the flashes.
    public double EffectScale => Effects switch
    {
        "subtle" => 0.5,
        "wild" => 1.7,
        _ => 1.0,
    };
}
