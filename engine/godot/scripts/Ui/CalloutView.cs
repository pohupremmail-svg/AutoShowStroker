using System;
using Gooner.Core.App;
using Gooner.Engine.Fx;
using Godot;

namespace Gooner.Engine.Ui;

// What she says, in big glowing letters over the picture rather than in a box at its foot: each
// line types itself in and lifts away when it is done. A soft shadow behind keeps it readable on a
// bright picture.
public partial class CalloutView : Control
{
    private readonly Label _label;
    private readonly TextureRect _shadow, _halo;
    private string? _text;
    private Tween? _tween;
    private float _bottom = 400;
    private float _lift;

    public CalloutView()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _shadow = new TextureRect
        {
            Texture = Glow.Texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
            SelfModulate = new Color(0, 0, 0, 0.42f),
        };
        AddChild(_shadow);
        // A soft light in the accent behind the words - the glow a label's hard shadow cannot give.
        _halo = new TextureRect
        {
            Texture = Glow.Texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
        };
        AddChild(_halo);
        _label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            VisibleCharactersBehavior = TextServer.VisibleCharactersBehavior.CharsAfterShaping,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_label);
        Modulate = new Color(1, 1, 1, 0);
    }

    // Where the bottom of the text sits: above the note track, wherever that is.
    public void Place(float bottom) => _bottom = bottom;

    public void Show(string? text)
    {
        if (text == _text) return;
        _text = text;
        _tween?.Kill();
        _tween = CreateTween().SetParallel();
        if (text is null)
        {
            _tween.TweenProperty(this, "modulate:a", 0f, 0.6f);
            _tween.TweenMethod(Callable.From<float>(v => _lift = v), _lift, -18f, 0.6f);
            return;
        }
        Style();
        _label.Text = text;
        _label.VisibleRatio = 0;
        _lift = 16;
        _tween.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
        _tween.TweenProperty(this, "modulate:a", 1f, 0.25f);
        _tween.TweenMethod(Callable.From<float>(v => _lift = v), 16f, 0f, 0.5f);
        _tween.TweenProperty(_label, "visible_ratio", 1f, Math.Clamp(text.Length * 0.022f, 0.35f, 1.2f)).SetTrans(Tween.TransitionType.Linear);
    }

    private void Style()
    {
        _label.AddThemeFontOverride("font", UiTheme.Display);
        _label.AddThemeFontSizeOverride("font_size", (int)Math.Clamp(Size.X * 0.026f, 26, 46));
        _label.AddThemeColorOverride("font_color", Colors.White);
        _label.AddThemeColorOverride("font_shadow_color", UiTheme.Of(Palette.Accent, 0.55f));
        _label.AddThemeConstantOverride("shadow_outline_size", 8);
        _label.AddThemeConstantOverride("shadow_offset_x", 0);
        _label.AddThemeConstantOverride("shadow_offset_y", 0);
        _label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.45f));
        _label.AddThemeConstantOverride("outline_size", 3);
        _halo.SelfModulate = UiTheme.Of(Palette.Accent, 0.22f);
    }

    public override void _Process(double delta)
    {
        var width = Size.X * 0.72f;
        _label.Size = new Vector2(width, 0);
        var height = _label.GetMinimumSize().Y;
        _label.Position = new Vector2((Size.X - width) / 2, _bottom - height + _lift);
        _label.Size = new Vector2(width, height);
        var spread = new Vector2(width * 0.12f, height * 0.6f);
        _shadow.Position = _label.Position - spread;
        _shadow.Size = _label.Size + spread * 2;
        _halo.Position = _shadow.Position;
        _halo.Size = _shadow.Size;
    }
}
