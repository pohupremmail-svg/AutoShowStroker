using System;
using System.IO;
using Godot;

namespace Gooner.Engine.Ui;

// The achievement marks: monochrome stroked SVGs from res/icons/achievements, tinted at draw time
// so one file serves both the grey locked state and the lit one - tinted_mark on the Python side.
// The marks are a single stroke with no fill, so painting the colour straight through the alpha
// gives either version from the same file, and the two can never drift apart.
public static class Marks
{
    public static Image? Tinted(string path, Color colour, int size)
    {
        string svg;
        try
        {
            svg = File.ReadAllText(path);
        }
        catch (IOException)
        {
            // A typo in an icon name is invisible until the panel opens; it must not be the thing
            // that breaks it. The tile goes without its mark, as Qt's invalid renderer drew nothing.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        var image = new Image();
        // Rendered at the size it is drawn at, rather than scaled after: a stroke stays crisp.
        if (image.LoadSvgFromString(svg, size / 64f) != Error.Ok || image.IsEmpty()) return null;
        image.Convert(Image.Format.Rgba8);
        for (var y = 0; y < image.GetHeight(); y++)
        {
            for (var x = 0; x < image.GetWidth(); x++)
            {
                var alpha = image.GetPixel(x, y).A;
                image.SetPixel(x, y, new Color(colour, alpha * colour.A));
            }
        }
        return image;
    }

    // The glow an earned mark wears, as QGraphicsDropShadowEffect drew it: the mark's own shape,
    // blurred, in the same colour, a little larger than the mark on every side.
    public static Image Glow(Image mark, int spread)
    {
        int width = mark.GetWidth() + 2 * spread, height = mark.GetHeight() + 2 * spread;
        var alpha = new float[width * height];
        for (var y = 0; y < mark.GetHeight(); y++)
            for (var x = 0; x < mark.GetWidth(); x++)
                alpha[(y + spread) * width + x + spread] = mark.GetPixel(x, y).A;
        // Three box blurs come close enough to a Gaussian.
        var radius = Math.Max(1, spread / 3);
        for (var pass = 0; pass < 3; pass++)
        {
            alpha = Blur(alpha, width, height, radius, horizontal: true);
            alpha = Blur(alpha, width, height, radius, horizontal: false);
        }
        var colour = mark.GetPixel(mark.GetWidth() / 2, mark.GetHeight() / 2);
        for (var y = 0; y < mark.GetHeight() && colour.A == 0; y++)
            for (var x = 0; x < mark.GetWidth() && colour.A == 0; x++)
                colour = mark.GetPixel(x, y);
        var glow = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        for (var i = 0; i < alpha.Length; i++)
            glow.SetPixel(i % width, i / width, new Color(colour.R, colour.G, colour.B, Math.Min(1f, alpha[i] * 1.6f)));
        return glow;
    }

    private static float[] Blur(float[] source, int width, int height, int radius, bool horizontal)
    {
        var result = new float[source.Length];
        var span = 2 * radius + 1;
        for (var line = 0; line < (horizontal ? height : width); line++)
        {
            var length = horizontal ? width : height;
            float Read(int i) => i < 0 || i >= length ? 0 : source[horizontal ? line * width + i : i * width + line];
            var sum = 0f;
            for (var i = -radius; i <= radius; i++) sum += Read(i);
            for (var i = 0; i < length; i++)
            {
                result[horizontal ? line * width + i : i * width + line] = sum / span;
                sum += Read(i + radius + 1) - Read(i - radius);
            }
        }
        return result;
    }
}
