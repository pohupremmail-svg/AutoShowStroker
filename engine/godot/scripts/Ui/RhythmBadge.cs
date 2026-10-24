using System;
using Gooner.Core.App;
using Godot;

namespace Gooner.Engine.Ui;

// The name of a rhythm as it arrives - or "PAUSE" - flaring up where the note track meets it and
// fading again.
public partial class RhythmBadge : Label
{
    private Tween? _tween;

    public RhythmBadge()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        HorizontalAlignment = HorizontalAlignment.Center;
        AddThemeFontOverride("font", UiTheme.Bold);
        AddThemeFontSizeOverride("font_size", 15);
        AddThemeColorOverride("font_color", Colors.White);
        AddThemeConstantOverride("shadow_outline_size", 10);
        AddThemeConstantOverride("shadow_offset_x", 0);
        AddThemeConstantOverride("shadow_offset_y", 0);
        Modulate = new Color(1, 1, 1, 0);
    }

    // Where its middle sits.
    public Vector2 At { get; set; }

    public void Announce(string name)
    {
        Text = string.Join(" ", name.ToUpperInvariant().ToCharArray());
        AddThemeColorOverride("font_shadow_color", UiTheme.Of(Palette.Accent, 0.9f));
        _tween?.Kill();
        Scale = new Vector2(1.35f, 1.35f);
        _tween = CreateTween();
        _tween.SetParallel().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        _tween.TweenProperty(this, "modulate:a", 1f, 0.25f);
        _tween.TweenProperty(this, "scale", Vector2.One, 0.4f);
        _tween.Chain().TweenInterval(1.2f);
        _tween.Chain().TweenProperty(this, "modulate:a", 0f, 0.5f);
    }

    public override void _Process(double delta)
    {
        Size = GetMinimumSize();
        PivotOffset = Size / 2;
        Position = (At - Size / 2).Round();
    }
}
