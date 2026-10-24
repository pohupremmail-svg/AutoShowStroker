using System;
using Godot;

namespace Gooner.Engine.Ui.Controls;

// A round dot of a colour - a status at a glance.
public partial class Dot : Control
{
    private Color _colour;

    public Dot(float size = 10)
    {
        CustomMinimumSize = new Vector2(size, size);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public Color Colour
    {
        get => _colour;
        set
        {
            _colour = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var centre = Size / 2;
        var radius = Math.Min(Size.X, Size.Y) / 2;
        Fx.Glow.Draw(this, centre, radius * 3.2f, new Color(_colour, 0.35f));
        DrawCircle(centre, radius, _colour, antialiased: true);
    }
}
