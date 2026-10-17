using Godot;

namespace Gooner.Engine.Fx;

// The soft light everything in the effect layer glows with: one white radial falloff, tinted by
// whatever colour it is drawn in. On an additive layer it adds light; on a normal one, drawn in
// black, it is a soft shadow.
public static class Glow
{
    private static Texture2D? _texture;

    public static Texture2D Texture => _texture ??= Build();

    private static GradientTexture2D Build()
    {
        var gradient = new Gradient
        {
            Offsets = [0f, 0.14f, 0.38f, 1f],
            Colors = [new Color(1, 1, 1, 1), new Color(1, 1, 1, 1), new Color(1, 1, 1, 0.33f), new Color(1, 1, 1, 0)],
        };
        return new GradientTexture2D
        {
            Gradient = gradient,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
            Width = 128,
            Height = 128,
        };
    }

    // A glow of the given radius; squash flattens it into an ellipse lying on a floor.
    public static void Draw(CanvasItem canvas, Vector2 at, float radius, Color color, float squash = 1)
    {
        if (radius <= 0 || color.A <= 0) return;
        canvas.DrawTextureRect(Texture, new Rect2(at.X - radius, at.Y - radius * squash, radius * 2, radius * 2 * squash), false, color);
    }
}
