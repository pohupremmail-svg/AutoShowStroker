using System.Collections.Generic;
using Gooner.Core.App;
using Godot;

namespace Gooner.Engine.Ui;

// The dock's and the start screen's line icons: 24-unit strokes, rendered at the size they are shown
// at and in the colour scheme's text colour, so they stay crisp and change with the scheme.
public static class Icons
{
    private static readonly Dictionary<string, string> Strokes = new()
    {
        ["previous"] = "<path d='M19 20 9 12l10-8v16Z'/><path d='M5 19V5'/>",
        ["stop"] = "<rect x='6' y='6' width='12' height='12' rx='2'/>",
        ["next"] = "<path d='m5 4 10 8-10 8V4Z'/><path d='M19 5v14'/>",
        ["sound"] = "<path d='M11 5 6 9H2v6h4l5 4V5Z'/><path d='M15.5 8.5a5 5 0 0 1 0 7'/><path d='M19 5a10 10 0 0 1 0 14'/>",
        ["muted"] = "<path d='M11 5 6 9H2v6h4l5 4V5Z'/><path d='m22 9-6 6'/><path d='m16 9 6 6'/>",
        ["settings"] = "<path d='M4 21v-7M4 10V3M12 21v-9M12 8V3M20 21v-5M20 12V3M1 14h6M9 8h6M17 16h6'/>",
        ["menu"] = "<path d='M4 6h16M4 12h16M4 18h16'/>",
        ["folder"] = "<path d='M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7Z'/>",
        ["play"] = "<path d='M7 4v16l13-8L7 4Z'/>",
        ["saved"] = "<path d='M6 3h12v18l-6-4-6 4V3Z'/>",
        ["trophy"] = "<path d='M8 21h8M12 17v4M7 4h10v5a5 5 0 0 1-10 0V4Z'/><path d='M17 5h3v2a3 3 0 0 1-3 3M7 5H4v2a3 3 0 0 0 3 3'/>",
        ["stats"] = "<path d='M4 20V10M10 20V4M16 20v-7M22 20H2'/>",
        ["power"] = "<path d='M12 3v9'/><path d='M18.4 6.6a9 9 0 1 1-12.8 0'/>",
        ["rhythm"] = "<path d='M2 12h4l2-6 4 12 3-9 2 3h5'/>",
        ["climax"] = "<path d='M12 3l2.6 5.6 6.1.7-4.5 4.2 1.2 6L12 16.6l-5.4 2.9 1.2-6-4.5-4.2 6.1-.7Z'/>",
        ["voice"] = "<path d='M4 5h16v10H9l-5 4V5Z'/>",
        ["look"] = "<circle cx='12' cy='12' r='9'/><circle cx='8.5' cy='10' r='1.3'/><circle cx='13.5' cy='8' r='1.3'/><circle cx='15.5' cy='13' r='1.3'/>",
        ["device"] = "<rect x='7' y='2' width='10' height='20' rx='5'/><path d='M12 7v3'/>",
        ["person"] = "<circle cx='12' cy='7' r='4'/><path d='M4 21a8 8 0 0 1 16 0'/>",
        ["globe"] = "<circle cx='12' cy='12' r='9'/><path d='M3 12h18M12 3a14 14 0 0 1 0 18M12 3a14 14 0 0 0 0 18'/>",
        ["keyboard"] = "<rect x='2' y='6' width='20' height='12' rx='2'/><path d='M6 10h.01M10 10h.01M14 10h.01M18 10h.01M7 14h10'/>",
        ["lock"] = "<rect x='4' y='11' width='16' height='10' rx='2'/><path d='M8 11V7a4 4 0 0 1 8 0v4'/>",
        ["spark"] = "<path d='M12 2v4M12 18v4M2 12h4M18 12h4M5 5l2.5 2.5M16.5 16.5 19 19M5 19l2.5-2.5M16.5 7.5 19 5'/>",
        ["book"] = "<path d='M4 4h6a2 2 0 0 1 2 2v14a2 2 0 0 0-2-2H4V4Z'/><path d='M20 4h-6a2 2 0 0 0-2 2v14a2 2 0 0 1 2-2h6V4Z'/>",
        ["scroll"] = "<path d='M7 3h11v14a3 3 0 0 1-3 3H6a3 3 0 0 1-3-3v-1h11v1a3 3 0 0 0 3 3'/><path d='M7 3a2 2 0 0 0-2 2v11M10 8h5M10 12h5'/>",
        ["update"] = "<path d='M12 3v12M7 10l5 5 5-5M4 21h16'/>",
        ["history"] = "<path d='M3 12a9 9 0 1 0 3-6.7L3 8'/><path d='M3 3v5h5M12 7v5l3 3'/>",
        ["grip"] = "<circle cx='9' cy='6' r='1'/><circle cx='15' cy='6' r='1'/><circle cx='9' cy='12' r='1'/><circle cx='15' cy='12' r='1'/><circle cx='9' cy='18' r='1'/><circle cx='15' cy='18' r='1'/>",
    };

    private static readonly Dictionary<(string, int, string), Texture2D?> Cache = [];

    public static Texture2D? Get(string name, int size = 22, string? color = null)
    {
        color ??= Palette.Text;
        if (Cache.TryGetValue((name, size, color), out var cached)) return cached;
        var svg = $"<svg viewBox='0 0 24 24' width='24' height='24' fill='none' stroke='{color}' stroke-width='2' " +
            $"stroke-linecap='round' stroke-linejoin='round'>{Strokes[name]}</svg>";
        var image = new Image();
        Texture2D? texture = image.LoadSvgFromString(svg, size / 24f) == Error.Ok && !image.IsEmpty() ? ImageTexture.CreateFromImage(image) : null;
        Cache[(name, size, color)] = texture;
        return texture;
    }
}
