using System;
using Godot;

namespace Gooner.Engine.Fx;

// A full-window canvas whose drawing is someone else's: the note track paints a normal layer (the
// darkened floor, labels) and an additive one (everything that glows) through two of these.
public partial class PaintLayer : Control
{
    public PaintLayer(bool additive)
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        if (additive) Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
    }

    public Action<PaintLayer>? Paint { get; set; }

    public override void _Draw() => Paint?.Invoke(this);

    // An ellipse as a closed outline.
    public void DrawEllipse(Vector2 centre, float rx, float ry, Color color, float width, int points = 64)
    {
        if (rx <= 0 || ry <= 0 || color.A <= 0) return;
        DrawPolyline(Ellipse(centre, rx, ry, points), color, width, true);
    }

    public void FillEllipse(Vector2 centre, float rx, float ry, Color color, int points = 64)
    {
        if (rx <= 0 || ry <= 0 || color.A <= 0) return;
        var outline = Ellipse(centre, rx, ry, points);
        DrawColoredPolygon(outline[..^1], color);
    }

    // A ring between two radii, as a band; both may be ellipses of the same squash.
    public void FillBand(Vector2 centre, float inner, float outer, float squash, Color color, int points = 64)
    {
        if (outer <= inner || color.A <= 0) return;
        var mid = (inner + outer) / 2;
        DrawSetTransform(centre, 0, new Vector2(1, squash));
        DrawArc(Vector2.Zero, mid, 0, MathF.Tau, points, color, outer - inner, true);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    // Dashes round an ellipse, turning with `offset` (radians).
    public void DrawDashedEllipse(Vector2 centre, float rx, float ry, Color color, float width, int dashes, float offset)
    {
        if (rx <= 0 || ry <= 0 || color.A <= 0) return;
        for (var i = 0; i < dashes; i++)
        {
            var a0 = offset + i * MathF.Tau / dashes;
            var a1 = a0 + MathF.Tau / dashes * 0.55f;
            var points = new Vector2[6];
            for (var k = 0; k < points.Length; k++)
            {
                var a = a0 + (a1 - a0) * k / (points.Length - 1);
                points[k] = centre + new Vector2(MathF.Cos(a) * rx, MathF.Sin(a) * ry);
            }
            DrawPolyline(points, color, width, true);
        }
    }

    public static Vector2[] Ellipse(Vector2 centre, float rx, float ry, int points)
    {
        var outline = new Vector2[points + 1];
        for (var i = 0; i <= points; i++)
        {
            var a = i * MathF.Tau / points;
            outline[i] = centre + new Vector2(MathF.Cos(a) * rx, MathF.Sin(a) * ry);
        }
        return outline;
    }

    // Text centred on a point.
    public void DrawCentred(Font font, Vector2 at, string text, int size, Color color)
    {
        var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        DrawString(font, new Vector2(at.X - width / 2, at.Y), text, HorizontalAlignment.Left, -1, size, color);
    }
}
