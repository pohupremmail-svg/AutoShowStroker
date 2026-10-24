using System;
using Gooner.Core.App;
using Gooner.Core.Callouts;
using Gooner.Core.Characters;
using Godot;

namespace Gooner.Engine.Ui;

// The character with the user through a session, as if over a telepathic channel: her bust in a
// glowing window at the top right, open from the session's start to its end. She moves to the beat
// (CharacterView.Rhythm); with each callout the window draws closer and brighter, she wears a face for
// what happened (Reactions), her mouth moves to the line (Visemes) and the line types itself into a
// speech bubble beside her (SpeechBubble), and afterwards it settles back.
// The window's look is one shader, telepathy.gdshader: a neon oval or an old tube screen.
public partial class TelepathyFrame : Control
{
    public enum Style
    {
        Oval = 0,
        TubeScreen = 1,
    }

    private static readonly Shader FrameShader = GD.Load<Shader>("res://shaders/telepathy.gdshader");

    // How much of the frame's rect is left round the window for its glow: the shader's `pad`, set
    // from here so that the bubble's tail can find the window's edge.
    private const float Pad = 0.08f;

    private readonly CharacterView _view;
    private readonly ShaderMaterial _material = new() { Shader = FrameShader };
    private readonly SpeechBubble _bubble = new();
    private readonly Style _style;
    private Tween? _fade, _closer;
    private float _near;
    private bool _open, _speaking;

    public TelepathyFrame(string path, Style style = Style.Oval)
    {
        _style = style;
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        _view = new CharacterView(path, Framing.Bust) { Material = _material };
        _material.SetShaderParameter("style", (int)style);
        _material.SetShaderParameter("fade", 0f);
        _material.SetShaderParameter("pad", Pad);
        AddChild(_view);
        AddChild(_bubble);
        SetAnchorsPreset(LayoutPreset.TopRight);
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        Fit();
        if (_view.Mouth() is { } mouth && _view.Size.X > 0) _bubble.Place(Beside(mouth), GetParentAreaSize());
        _material.SetShaderParameter("flare", _near);
        _material.SetShaderParameter("glow", UiTheme.Of(Palette.Accent));
    }

    // Off the top right corner: a quarter of the window's height at rest, a good third while she
    // speaks; an upright oval, or a screen a little wider.
    private void Fit()
    {
        var window = GetParentAreaSize();
        if (window.Y <= 0) return;
        var rest = Math.Clamp(window.Y * 0.25f, 150, 290);
        var near = Math.Clamp(window.Y * 0.38f, 210, 430);
        var height = Mathf.Lerp(rest, near, _near);
        var width = height * (_style == Style.Oval ? 0.72f : 0.8f);
        OffsetLeft = -width - 10;
        OffsetRight = -10;
        OffsetTop = 4;
        OffsetBottom = 4 + height;
    }

    // Her figure is in the frame, open: what she says goes in the bubble beside her rather than over
    // the picture.
    public bool Present => _open && Visible && _view.Loaded;

    // The line in her bubble, or null once it is over.
    public void Say(string? text) => _bubble.Say(text);

    // Where the bubble's tail points: at the height of her mouth, just off the window's edge there -
    // the oval narrows towards the top, the screen does not. `mouth` is in the view's own pixels, which
    // the shader draws into the window inside the pad.
    private Vector2 Beside(Vector2 mouth)
    {
        var y = Size.Y * Pad + mouth.Y / _view.Size.Y * Size.Y * (1 - 2 * Pad);
        var radii = Size * (0.5f - Pad);
        var edge = Size.X * Pad;
        if (_style == Style.Oval)
        {
            var across = (y - Size.Y / 2) / radii.Y;
            edge = Size.X / 2 - radii.X * MathF.Sqrt(Math.Max(0, 1 - across * across));
        }
        return new Vector2(edge - 4, y);
    }

    // The session has begun: the channel opens.
    public void Open()
    {
        if (_open) return;
        _open = true;
        Visible = true;
        Fit();
        _fade?.Kill();
        _fade = CreateTween();
        _fade.TweenMethod(Callable.From<float>(SetFade), 0f, 1f, 0.5f);
    }

    // The beat, for her to move to: the session clock now, the notes before and after it, and the one
    // beyond each of those.
    public void Rhythm(double now, double? noteBefore, double? lastNote, double? nextNote, double? noteAfter) =>
        _view.Rhythm(now, noteBefore, lastNote, nextNote, noteAfter);

    // A callout: closer, a face for what happened, the line on her lips.
    public void Speak(Callout callout)
    {
        ArgumentNullException.ThrowIfNull(callout);
        if (!_open) Open();
        _view.React(Reactions.For(callout.Category, callout.Tone));
        _view.Speak(callout.Text);
        if (_speaking) return;
        _speaking = true;
        DrawNear(1, 0.45f, Tween.TransitionType.Back);
    }

    // The callout is over: back to where she was.
    public void Close()
    {
        if (!_speaking) return;
        _speaking = false;
        _view.Calm();
        DrawNear(0, 0.6f, Tween.TransitionType.Cubic);
    }

    // At once, without a fade: panic, the end of a session.
    public void Cut()
    {
        _open = _speaking = false;
        _fade?.Kill();
        _closer?.Kill();
        _near = 0;
        _view.Calm();
        SetFade(0);
        Visible = false;
    }

    private void DrawNear(float to, float seconds, Tween.TransitionType how)
    {
        _closer?.Kill();
        _closer = CreateTween().SetEase(Tween.EaseType.Out).SetTrans(how);
        _closer.TweenMethod(Callable.From<float>(v => _near = v), _near, to, seconds);
    }

    private void SetFade(float fade) => _material.SetShaderParameter("fade", fade);
}
