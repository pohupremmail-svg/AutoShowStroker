using System.Collections.Generic;
using Gooner.Core.App;
using Godot;

namespace Gooner.Engine.Ui;

// The look's frosted glass (shaders/glass.gdshader) for panels, the dock and the captions: a rounded
// StyleBoxFlat drawn in marker colours the shader turns into blurred, tinted backdrop, rim and
// shadow. Only for containers - on a Button the shader would recolour its text as well.
//
// One material per opacity, shared by everything that uses it, so a change of colour scheme
// reaches every pane of glass at once (Refresh).
public static class Glass
{
    private static Shader? _shader;
    private static readonly Dictionary<float, ShaderMaterial> Materials = [];

    // The shape: body white, rim black, shadow blue - what the shader reads them as.
    public static StyleBoxFlat Box(int radius, float margin, bool shadow = true)
    {
        var box = new StyleBoxFlat { BgColor = Colors.White, BorderColor = Colors.Black, AntiAliasing = true };
        box.SetBorderWidthAll(1);
        box.SetCornerRadiusAll(radius);
        box.SetContentMarginAll(margin);
        if (shadow)
        {
            box.ShadowColor = new Color(0, 0, 1, 0.35f);
            box.ShadowSize = 18;
            box.ShadowOffset = new Vector2(0, 8);
        }
        return box;
    }

    // `opacity` is how much of the tint covers the blurred picture behind: a caption lets more of
    // it through than a panel full of text.
    public static ShaderMaterial Material(float opacity = 0.45f)
    {
        if (Materials.TryGetValue(opacity, out var material)) return material;
        material = new ShaderMaterial { Shader = _shader ??= GD.Load<Shader>("res://shaders/glass.gdshader") };
        Materials[opacity] = material;
        Tint(material, opacity);
        return material;
    }

    // Puts a container behind glass.
    public static void Apply(Control control, int radius, float margin, float opacity = 0.45f, string stylebox = "panel", bool shadow = true)
    {
        control.AddThemeStyleboxOverride(stylebox, Box(radius, margin, shadow));
        control.Material = Material(opacity);
    }

    // After a change of colour scheme.
    public static void Refresh()
    {
        foreach (var (opacity, material) in Materials) Tint(material, opacity);
    }

    private static void Tint(ShaderMaterial material, float opacity)
    {
        material.SetShaderParameter("tint", UiTheme.Of(Palette.Background, opacity));
        material.SetShaderParameter("rim", UiTheme.Of(Palette.Text, 0.18f));
    }
}
