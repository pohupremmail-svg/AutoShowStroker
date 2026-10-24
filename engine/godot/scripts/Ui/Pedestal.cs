using System;
using Gooner.Core.App;
using Gooner.Core.Characters;
using Gooner.Engine.Fx;
using Godot;

namespace Gooner.Engine.Ui;

// The start screen's stage: a spotlight falling on a floor of slowly turning rings, the logo
// floating above it - or, when there is one, a character standing on it, now and then saying a line in
// a bubble that floats beside her head (the user's design: its tail starts at the height of her head,
// it fades in slowly and out again, as in a game).
public partial class Pedestal : Control
{
    private readonly PaintLayer _light = new(additive: true);
    private readonly Texture2D? _logo;
    private readonly CharacterView? _character;
    private readonly SpeechBubble _bubble = new() { FadeIn = 0.9f, FadeOut = 1.4f };
    private string? _saying;
    private double _time;

    public Pedestal(Texture2D? logo, string? character)
    {
        _logo = logo;
        MouseFilter = MouseFilterEnum.Ignore;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        _light.Paint = Paint;
        AddChild(_light);
        if (character is not null) AddChild(_character = new CharacterView(character));
        AddChild(_bubble);
    }

    public FigureState State => _character?.State ?? FigureState.None;

    // The line she says now, or null: typed into her bubble with her mouth moving to it, faded out
    // when it is over.
    public void Say(string? line)
    {
        if (line == _saying) return;
        _saying = line;
        _bubble.Say(line);
        if (line is not null) _character?.Speak(line);
    }

    public override void _Process(double delta)
    {
        _time += delta;
        _light.QueueRedraw();
        // Hung beside her head, so it goes where she sways; bobbing on its own a little, and rising
        // the last few pixels into place as it fades in.
        if (_character?.BesideHead() is { } tip)
        {
            var drift = new Vector2(0, MathF.Sin((float)_time * 1.1f) * 4 + (1 - _bubble.Modulate.A) * 10);
            _bubble.Place(tip + drift, GetViewportRect().Size);
        }
    }

    private void Paint(PaintLayer c)
    {
        var accent = UiTheme.Of(Palette.Accent);
        var cool = UiTheme.Of(Palette.Cool);
        var cx = Size.X / 2;
        var floor = Size.Y * 0.84f;
        var reach = Math.Min(Size.X * 0.46f, Size.Y * 0.5f);

        c.DrawPolygon(
            [new(cx - reach * 0.12f, 0), new(cx + reach * 0.12f, 0), new(cx + reach, floor), new(cx - reach, floor)],
            [new Color(accent, 0.32f), new Color(accent, 0.32f), new Color(accent, 0), new Color(accent, 0)]);
        Glow.Draw(c, new Vector2(cx, floor), reach * 1.05f, new Color(accent, 0.45f), 0.24f);

        var turn = (float)_time;
        c.DrawEllipse(new Vector2(cx, floor), reach, reach * 0.22f, new Color(accent, 0.75f), 2, 96);
        c.DrawDashedEllipse(new Vector2(cx, floor), reach * 0.74f, reach * 0.74f * 0.22f, new Color(accent, 0.7f), 2, 36, -turn * 0.5f);
        c.DrawDashedEllipse(new Vector2(cx, floor), reach * 0.48f, reach * 0.48f * 0.22f, new Color(cool, 0.8f), 2, 18, turn * 0.8f);

        if (_logo is null || _character?.Loaded == true) return;
        var side = Math.Min(reach * 1.1f, Size.Y * 0.55f);
        var centre = new Vector2(cx, floor - side * 0.75f + MathF.Sin(turn * 1.2f) * 10);
        Glow.Draw(c, centre, side * 0.75f, new Color(accent, 0.3f));
        c.DrawTextureRect(_logo, new Rect2(centre - new Vector2(side, side) / 2, new Vector2(side, side)), false, new Color(1, 1, 1, 0.95f));
    }
}
