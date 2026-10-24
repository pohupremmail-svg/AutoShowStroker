using Godot;

namespace Gooner.Engine.Fx;

// shaders/stage_filter.gdshader over the stage, under the note track: censoring round the Hypno
// Ring, and the climax's colour split. Off - not drawn at all, so the screen is not copied - while
// there is nothing to do.
public partial class StageFilter : ColorRect
{
    private readonly ShaderMaterial _material;

    public StageFilter()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/stage_filter.gdshader") };
        Material = _material;
        Visible = false;
    }

    // censor is a Look.Censor value; null when nothing is censored. focus and radius are in this
    // control's own pixels.
    public void Apply(string? censor, Vector2 focus, float radius, float aberration)
    {
        var mode = censor switch { "blur" => 1, "mosaic" => 2, "dark" => 3, _ => 0 };
        Visible = mode != 0 || aberration > 0.3f;
        if (!Visible) return;
        var viewport = GetViewportRect().Size;
        var global = GetGlobalTransformWithCanvas() * focus;
        _material.SetShaderParameter("mode", mode);
        _material.SetShaderParameter("focus", new Vector2(global.X / viewport.X, global.Y / viewport.Y));
        _material.SetShaderParameter("radius", radius);
        _material.SetShaderParameter("aberration", aberration);
    }
}
