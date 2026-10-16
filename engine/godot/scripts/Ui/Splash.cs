using System;
using System.IO;
using Gooner.Core.App;
using Godot;

namespace Gooner.Engine.Ui;

// SplashScreen: the logo and the app's name fading in, holding, and fading out - over the window
// rather than in a frameless one of its own before it, since the window is there from the first
// frame. Show startup splash animation in the settings switches it off; Finished is when What's New
// may follow, as main.py had it.
public partial class Splash : ColorRect
{
    private const int LogoSize = 320;
    private const float FadeInSec = 0.4f;
    private const float HoldSec = 1.0f;
    private const float FadeOutSec = 0.4f;

    public Splash(string? resources)
    {
        Color = UiTheme.Of(Palette.Background);
        AnchorRight = AnchorBottom = 1;
        // Nothing beneath it is reachable while it is up; the panic key still is (Main._Input).
        MouseFilter = MouseFilterEnum.Stop;
        Modulate = new Color(1, 1, 1, 0);

        var center = new CenterContainer { AnchorRight = 1, AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore };
        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        column.AddThemeConstantOverride("separation", 16);
        if (resources is not null && Image.LoadFromFile(Path.Combine(resources, "icons", "qUloN.png")) is { } logo)
        {
            var scale = Math.Min(LogoSize / (float)logo.GetWidth(), LogoSize / (float)logo.GetHeight());
            logo.Resize((int)(logo.GetWidth() * scale), (int)(logo.GetHeight() * scale), Image.Interpolation.Lanczos);
            column.AddChild(new TextureRect
            {
                Texture = ImageTexture.CreateFromImage(logo),
                StretchMode = TextureRect.StretchModeEnum.KeepCentered,
                MouseFilter = MouseFilterEnum.Ignore,
            });
        }
        var name = new Label { Text = "GoonerApp", HorizontalAlignment = HorizontalAlignment.Center };
        name.AddThemeFontOverride("font", UiTheme.Bold);
        name.AddThemeFontSizeOverride("font_size", 28);
        name.AddThemeColorOverride("font_color", UiTheme.Of(Palette.Text));
        // The glow the Qt side drew as a drop shadow.
        name.AddThemeColorOverride("font_shadow_color", UiTheme.Of(Palette.Accent, 0.8f));
        name.AddThemeConstantOverride("shadow_outline_size", 14);
        name.AddThemeConstantOverride("shadow_offset_x", 0);
        name.AddThemeConstantOverride("shadow_offset_y", 0);
        column.AddChild(name);
        center.AddChild(column);
        AddChild(center);
    }

    public event Action? Finished;

    public override void _Ready()
    {
        var tween = CreateTween().SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(this, "modulate:a", 1f, FadeInSec);
        tween.TweenInterval(HoldSec);
        tween.TweenProperty(this, "modulate:a", 0f, FadeOutSec);
        tween.Finished += () =>
        {
            QueueFree();
            Finished?.Invoke();
        };
    }
}
