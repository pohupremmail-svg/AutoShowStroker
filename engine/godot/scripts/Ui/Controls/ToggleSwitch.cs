using Gooner.Core.App;
using Godot;

namespace Gooner.Engine.Ui.Controls;

// A switch instead of a check box (the redesign, 2026-10-24): a pill whose knob slides over and lights
// up in the accent when on.
public partial class ToggleSwitch : Button
{
    private float _at;

    public ToggleSwitch(bool on)
    {
        ToggleMode = true;
        Flat = true;
        FocusMode = FocusModeEnum.None;
        CustomMinimumSize = new Vector2(46, 26);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        SetPressedNoSignal(on);
        _at = on ? 1 : 0;
        // The theme's flat button would still draw a hover box.
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
            AddThemeStyleboxOverride(state, new StyleBoxEmpty());
    }

    public override void _Process(double delta)
    {
        var target = ButtonPressed ? 1f : 0f;
        if (Mathf.IsEqualApprox(_at, target)) return;
        _at = Mathf.MoveToward(_at, target, (float)delta * 7);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = new Vector2(46, 26);
        var origin = (Size - size) / 2;
        var rect = new Rect2(origin, size);
        var accent = UiTheme.Of(Palette.Accent);
        var off = UiTheme.Of(Palette.SurfaceDarkest);
        var track = off.Lerp(accent, _at);
        if (_at > 0.01f) Fx.Glow.Draw(this, rect.GetCenter(), 30, new Color(accent, 0.35f * _at));
        var box = UiTheme.Box(track, 13);
        box.SetBorderWidthAll(1);
        box.BorderColor = new Color(1, 1, 1, 0.13f).Lerp(accent, _at);
        box.AntiAliasing = true;
        DrawStyleBox(box, rect);
        var knob = origin + new Vector2(13 + 20 * _at, 13);
        DrawCircle(knob, 9, new Color(1, 1, 1, 0.6f + 0.4f * _at), antialiased: true);
        if (Disabled) DrawStyleBox(UiTheme.Box(new Color(0, 0, 0, 0.45f), 13), rect);
    }
}
