using System;
using Gooner.Core.App;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine.Fx;

// shaders/vignette.gdshader over the picture: darker edges, flaring in the accent with each note.
public partial class Vignette : ColorRect
{
    private readonly ShaderMaterial _material;
    private float _pulse;

    public Vignette()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/vignette.gdshader") };
        Material = _material;
    }

    // A note landed: `strength` already scaled by the look's effect strength.
    public void Pulse(float strength) => _pulse = Math.Max(_pulse, strength);

    public override void _Process(double delta)
    {
        _pulse *= MathF.Exp(-(float)delta * 6);
        _material.SetShaderParameter("pulse", _pulse);
        _material.SetShaderParameter("glow", UiTheme.Of(Palette.Accent));
    }
}
