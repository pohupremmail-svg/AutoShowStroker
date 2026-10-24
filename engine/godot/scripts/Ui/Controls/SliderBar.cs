using System;
using Gooner.Core.App;
using Gooner.Core.Panels;
using Godot;

namespace Gooner.Engine.Ui.Controls;

// A slider (the redesign, 2026-10-24): one handle, or two for a minimum and its maximum. Where a value
// sits, and the step it lands on, come from its SliderSpec - a wide range is logarithmic - so what it
// shows is what gets saved.
public partial class SliderBar : Control
{
    private const float Knob = 10;
    private readonly SliderSpec _spec;
    private readonly bool _range;
    private double _low, _high;
    private int _dragging = -1;

    public SliderBar(SliderSpec spec, double value, double? high = null)
    {
        _spec = spec;
        _range = high is not null;
        _low = value;
        _high = high ?? value;
        CustomMinimumSize = new Vector2(120, 30);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseDefaultCursorShape = CursorShape.PointingHand;
    }

    // The handle's value - the low end for a range - and the high end.
    public double Low => _low;

    public double High => _high;

    public bool Enabled { get; set; } = true;

    // Pulled, or set from a typed value.
    public event Action<double, double>? Changed;

    public void Set(double low, double high)
    {
        (_low, _high) = _range ? (Math.Min(low, high), Math.Max(low, high)) : (low, low);
        QueueRedraw();
    }

    private float Inner => Math.Max(1, Size.X - 2 * Knob);

    private float X(double value) => Knob + (float)_spec.Fraction(value) * Inner;

    public override void _GuiInput(InputEvent @event)
    {
        if (!Enabled) return;
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                if (button.Pressed)
                {
                    var at = _spec.At((button.Position.X - Knob) / Inner);
                    _dragging = !_range || Math.Abs(button.Position.X - X(_low)) <= Math.Abs(button.Position.X - X(_high)) && at <= _high ? 0 : 1;
                    Move(button.Position.X);
                }
                else
                {
                    _dragging = -1;
                }
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _dragging >= 0:
                Move(motion.Position.X);
                AcceptEvent();
                break;
        }
    }

    private void Move(float x)
    {
        var value = _spec.At((x - Knob) / Inner);
        if (!_range) _low = _high = value;
        else if (_dragging == 0) _low = Math.Min(value, _high);
        else _high = Math.Max(value, _low);
        QueueRedraw();
        Changed?.Invoke(_low, _high);
    }

    public override void _Draw()
    {
        var y = Size.Y / 2;
        var accent = UiTheme.Of(Palette.Accent);
        var rail = new Rect2(Knob, y - 2, Inner, 4);
        DrawStyleBox(UiTheme.Box(UiTheme.Of(Palette.SurfaceDarkest), 2), rail);
        var from = _range ? X(_low) : Knob;
        var to = _range ? X(_high) : X(_low);
        if (to > from)
        {
            DrawLine(new Vector2(from, y), new Vector2(to, y), new Color(accent, 0.25f), 9, true);
            DrawLine(new Vector2(from, y), new Vector2(to, y), accent, 4, true);
        }
        foreach (var value in _range ? new[] { _low, _high } : new[] { _low })
        {
            var at = new Vector2(X(value), y);
            Fx.Glow.Draw(this, at, 22, new Color(accent, 0.45f));
            DrawCircle(at, Knob, accent, antialiased: true);
            DrawCircle(at, Knob - 3, Colors.White, antialiased: true);
        }
        if (!Enabled) DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, 0));
    }
}
