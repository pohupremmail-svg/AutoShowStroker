using System;
using System.Collections.Generic;
using Gooner.Core.App;
using Gooner.Core.Characters;
using Godot;

namespace Gooner.Engine.Ui;

// What she says, in a bubble beside her telepathy frame - the user's pick of three mocks: dark glass
// with the frame's neon rim, its tail at the height of her mouth. The words type themselves in with
// her mouth, on the very timeline the mouth moves on (Visemes.Shown), and the bubble fades once the
// line is over. With no figure on screen the line is the big one over the picture instead
// (CalloutView), as it always was.
public partial class SpeechBubble : Control
{
    private const float TailReach = 16, TailHalf = 10, Corner = 16, PadX = 16, PadY = 12;

    private readonly Label _label;
    private readonly List<Vector2> _outline = [];
    private string? _text;
    private double _since;
    private Tween? _fade;
    private float _tailY;

    public SpeechBubble()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _label = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            VisibleCharactersBehavior = TextServer.VisibleCharactersBehavior.CharsAfterShaping,
            MouseFilter = MouseFilterEnum.Ignore,
            Position = new Vector2(PadX, PadY),
        };
        _label.AddThemeFontOverride("font", UiTheme.Bold);
        _label.AddThemeConstantOverride("shadow_outline_size", 6);
        _label.AddThemeConstantOverride("shadow_offset_x", 0);
        _label.AddThemeConstantOverride("shadow_offset_y", 0);
        AddChild(_label);
        Modulate = new Color(1, 1, 1, 0);
        Visible = false;
    }

    // The line she says, or null once it is over.
    public void Say(string? text)
    {
        if (text == _text) return;
        _text = text;
        _fade?.Kill();
        _fade = CreateTween();
        if (text is null)
        {
            _fade.TweenProperty(this, "modulate:a", 0f, 0.4f);
            _fade.TweenCallback(Callable.From(() => Visible = false));
            return;
        }
        _label.Text = text;
        _label.VisibleCharacters = 0;
        _since = 0;
        Visible = true;
        _fade.TweenProperty(this, "modulate:a", 1f, 0.2f);
    }

    // Beside the frame it belongs to (its parent): the tail's tip at `tip`, in the frame's own
    // coordinates, the bubble reaching left from it; sized to the window it is in.
    public void Place(Vector2 tip, Vector2 window)
    {
        if (_text is null) return;
        var width = Math.Clamp(window.X * 0.17f, 240, 420);
        var font = (int)Math.Clamp(window.Y * 0.019f, 16, 24);
        _label.AddThemeFontSizeOverride("font_size", font);
        _label.AddThemeColorOverride("font_color", Colors.White);
        _label.AddThemeColorOverride("font_shadow_color", UiTheme.Of(Palette.Accent, 0.6f));
        _label.Size = new Vector2(width - 2 * PadX, 0);
        // Measured with the whole line in it, so that the bubble does not grow while it types.
        var height = _label.GetMinimumSize().Y + 2 * PadY;
        _label.Size = new Vector2(width - 2 * PadX, height - 2 * PadY);
        // The tail high on the bubble: it starts about where her mouth is (the user's correction to
        // the mock, which had it lower).
        var top = Math.Max(0, tip.Y - Corner - TailHalf);
        Size = new Vector2(width, height);
        Position = new Vector2(tip.X - TailReach - width, top);
        _tailY = Math.Clamp(tip.Y - top, Corner + TailHalf, Math.Max(Corner + TailHalf, height - Corner - TailHalf));
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_text is null) return;
        _since += delta;
        _label.VisibleCharacters = Visemes.Shown(_text, _since);
    }

    public override void _Draw()
    {
        if (Size.X <= 0 || Size.Y <= 0) return;
        Outline();
        var accent = UiTheme.Of(Palette.Accent);
        var closed = new Vector2[_outline.Count + 1];
        _outline.CopyTo(closed);
        closed[^1] = _outline[0];
        // A soft neon glow outside the rim, the glass, then the rim itself.
        DrawPolyline(closed, accent with { A = 0.16f }, 9, antialiased: true);
        DrawPolyline(closed, accent with { A = 0.28f }, 5, antialiased: true);
        DrawColoredPolygon([.. _outline], new Color(0.07f, 0.04f, 0.12f, 0.86f));
        DrawPolyline(closed, accent, 2, antialiased: true);
    }

    // Round the box clockwise from its top left, the tail on the right side where her mouth is.
    private void Outline()
    {
        _outline.Clear();
        var (w, h) = (Size.X, Size.Y);
        Arc(new Vector2(w - Corner, Corner), -90, 0);
        _outline.Add(new Vector2(w, _tailY - TailHalf));
        _outline.Add(new Vector2(w + TailReach, _tailY));
        _outline.Add(new Vector2(w, _tailY + TailHalf));
        Arc(new Vector2(w - Corner, h - Corner), 0, 90);
        Arc(new Vector2(Corner, h - Corner), 90, 180);
        Arc(new Vector2(Corner, Corner), 180, 270);
    }

    private void Arc(Vector2 centre, float from, float to)
    {
        for (var step = 0; step <= 6; step++)
        {
            var angle = Mathf.DegToRad(Mathf.Lerp(from, to, step / 6f));
            _outline.Add(centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * Corner);
        }
    }
}
