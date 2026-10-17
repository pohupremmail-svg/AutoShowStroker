namespace Gooner.Core.App;

// One colour scheme of the look: every colour the window and its panels draw with, as hex, so the
// engine can turn it into whatever colour type it draws with; and the face the big words are set
// in - the logo, the callout, the climax banner.
public sealed record ColorScheme
{
    public required string Name { get; init; }
    public required string Background { get; init; }
    public required string Accent { get; init; }
    public required string AccentHover { get; init; }
    public required string Text { get; init; }
    public required string Secondary { get; init; }
    public required string SecondaryHover { get; init; }
    public required string Surface { get; init; }
    public required string SurfaceDark { get; init; }
    public required string SurfaceDarkest { get; init; }
    public required string DisabledBg { get; init; }
    public required string DisabledText { get; init; }
    public required string Pause { get; init; }

    // The second colour of the effects: short notes, a change of rhythm.
    public required string Cool { get; init; }
    public required string Ruined { get; init; }
    public required string RuinedDim { get; init; }
    public required string Denied { get; init; }
    public required string DeniedDim { get; init; }
    public required IReadOnlyList<string> DisplayFonts { get; init; }
    public bool DisplayItalic { get; init; }
}

// The colours in use. Neon is the "Cyber-Erotic Neon" palette of src/theme.py, derived from the
// logo: neon pink line-art on a near-black purple. Boudoir is the engine's own: lipstick red and
// champagne on wine, set in a serif. Everything asks here rather than holding a colour, so a
// change of scheme reaches whatever is drawn next.
public static class Palette
{
    public static IReadOnlyDictionary<string, ColorScheme> Schemes { get; } = new Dictionary<string, ColorScheme>
    {
        ["neon"] = new()
        {
            Name = "neon",
            Background = "#2D1D3A",
            Accent = "#FF00BF",
            AccentHover = "#FF33CC",
            Text = "#FFC2ED",
            Secondary = "#5A3D73",
            SecondaryHover = "#6E4A8C",
            Surface = "#3A2650",
            SurfaceDark = "#241730",
            SurfaceDarkest = "#150D1D",
            DisabledBg = "#3A2A4A",
            DisabledText = "#7A6288",
            // The strokemeter's pause, distinct from Accent so a pause does not read as a beat flash.
            Pause = "#8C3D73",
            Cool = "#9B5CFF",
            // "cum" reuses Accent; ruined and denied stay on different hues, or a colour-coded
            // banner would not be one.
            Ruined = "#FFA733",
            RuinedDim = "#CC7A00",
            Denied = "#FF3B3B",
            DeniedDim = "#B31212",
            DisplayFonts = ["Segoe UI", "sans-serif"],
        },
        ["boudoir"] = new()
        {
            Name = "boudoir",
            Background = "#2A1018",
            Accent = "#FF2D55",
            AccentHover = "#FF5C7A",
            Text = "#FFDCE2",
            Secondary = "#6B2438",
            SecondaryHover = "#84304A",
            Surface = "#3A1622",
            SurfaceDark = "#1E0B12",
            SurfaceDarkest = "#12060B",
            DisabledBg = "#3A2028",
            DisabledText = "#8A6670",
            Pause = "#7A2238",
            Cool = "#F3C27C",
            Ruined = "#FFA733",
            RuinedDim = "#CC7A00",
            // Red is the accent here, so a denial goes cold instead.
            Denied = "#E4E8FF",
            DeniedDim = "#8E93B8",
            DisplayFonts = ["Bodoni MT", "Didot", "Georgia", "serif"],
            DisplayItalic = true,
        },
    };

    public static ColorScheme Current { get; private set; } = Schemes["neon"];

    // Neon for a name it does not know.
    public static void Use(string name) => Current = Schemes.GetValueOrDefault(name) ?? Schemes["neon"];

    public static string Background => Current.Background;
    public static string Accent => Current.Accent;
    public static string AccentHover => Current.AccentHover;
    public static string Text => Current.Text;
    public static string Secondary => Current.Secondary;
    public static string SecondaryHover => Current.SecondaryHover;
    public static string Surface => Current.Surface;
    public static string SurfaceDark => Current.SurfaceDark;
    public static string SurfaceDarkest => Current.SurfaceDarkest;
    public static string DisabledBg => Current.DisabledBg;
    public static string DisabledText => Current.DisabledText;
    public static string Pause => Current.Pause;
    public static string Cool => Current.Cool;
    public static string Ruined => Current.Ruined;
    public static string RuinedDim => Current.RuinedDim;
    public static string Denied => Current.Denied;
    public static string DeniedDim => Current.DeniedDim;
}
