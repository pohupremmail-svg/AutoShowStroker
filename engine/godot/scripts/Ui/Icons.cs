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
        ["device"] = "<rect x='7' y='2' width='10' height='20' rx='5'/><path d='M12 7v3'/>",
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
