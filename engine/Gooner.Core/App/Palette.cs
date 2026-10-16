namespace Gooner.Core.App;

// The "Cyber-Erotic Neon" palette of src/theme.py, derived from the logo: neon pink line-art on a
// near-black purple. Hex, so the engine can turn it into whatever colour type it draws with.
public static class Palette
{
    public const string Background = "#2D1D3A";
    public const string Accent = "#FF00BF";
    public const string AccentHover = "#FF33CC";
    public const string Text = "#FFC2ED";
    public const string Secondary = "#5A3D73";
    public const string SecondaryHover = "#6E4A8C";
    public const string Surface = "#3A2650";
    public const string SurfaceDark = "#241730";
    public const string SurfaceDarkest = "#150D1D";
    public const string DisabledBg = "#3A2A4A";
    public const string DisabledText = "#7A6288";

    // The strokemeter's pause, distinct from Accent so a pause does not read as a beat flash.
    public const string Pause = "#8C3D73";

    // "cum" reuses Accent; ruined and denied stay on different hues, or a colour-coded banner
    // would not be one.
    public const string Ruined = "#FFA733";
    public const string RuinedDim = "#CC7A00";
    public const string Denied = "#FF3B3B";
    public const string DeniedDim = "#B31212";
}
