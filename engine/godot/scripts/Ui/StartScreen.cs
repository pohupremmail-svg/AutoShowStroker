using System;
using System.Collections.Generic;
using System.IO;
using Gooner.Core.App;
using Gooner.Core.Localization;
using Gooner.Engine.Fx;
using Godot;

namespace Gooner.Engine.Ui;

// One of the start screen's big cards: what it says, the line under it (asked for each time the
// screen comes up), and what it does.
public sealed record StartCard(string Title, string Icon, Func<string?> Subtitle, Action Pressed, bool Primary = false);

// What is between sessions, in place of the menu bar: the name in lights, a handful of big cards for
// what one comes here to do, and every other way in along the foot - over a dark, drifting stage.
// It fades away when a session starts and comes back when it ends.
public partial class StartScreen : Control
{
    private readonly List<(StartCard Card, Label Subtitle)> _cards = [];
    private readonly List<Button> _buttons = [];
    private VBoxContainer _column = null!;
    private readonly PaintLayer _bokeh = new(additive: true);
    private readonly (float X, float Y, float Depth, float Speed, int Hue)[] _motes = new (float, float, float, float, int)[46];
    private readonly Random _random = new();
    private readonly Label _logoLeft, _logoRight, _tagline;
    private readonly Func<string?> _line;
    private Tween? _tween;
    private double _time;

    // The line under the name when the phrase files have none for the language and tones picked.
    public static string DefaultLine => L10n.T("Lean back. I set the pace tonight.");

    // `line` is asked each time the screen comes up: a main_menu line of the callouts, in the
    // voice the app speaks in.
    public StartScreen(string? resources, IReadOnlyList<StartCard> cards, IReadOnlyList<(string Label, Action Pressed)> links,
        string version, Func<string?> line)
    {
        _line = line;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        for (var i = 0; i < _motes.Length; i++)
            _motes[i] = ((float)_random.NextDouble(), (float)_random.NextDouble(), 0.3f + (float)_random.NextDouble(), 0.004f + (float)_random.NextDouble() * 0.012f, _random.Next(10));
        _bokeh.Paint = PaintBokeh;
        AddChild(_bokeh);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var (side, value) in new[] { ("left", 90), ("right", 60), ("top", 50), ("bottom", 70) })
            margin.AddThemeConstantOverride($"margin_{side}", value);
        AddChild(margin);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 40);
        margin.AddChild(row);

        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 12);
        _column = column;
        row.AddChild(column);
        var logo = new HBoxContainer();
        logo.AddThemeConstantOverride("separation", 0);
        (_logoLeft, _logoRight) = (LogoPart("Gooner"), LogoPart("App"));
        logo.AddChild(_logoLeft);
        logo.AddChild(_logoRight);
        column.AddChild(logo);
        _tagline = new Label
        {
            Text = DefaultLine,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(560, 0),
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            Modulate = new Color(1, 1, 1, 0.8f),
        };
        column.AddChild(_tagline);
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 18) });
        foreach (var card in cards) column.AddChild(Card(card));

        row.AddChild(new Pedestal(LoadLogo(resources)) { CustomMinimumSize = new Vector2(380, 0) });

        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", 14);
        foot.AnchorTop = foot.AnchorBottom = 1;
        foot.OffsetLeft = 84;
        foot.OffsetTop = -54;
        foot.OffsetBottom = -24;
        var versionLabel = new Label { Text = $"v{version}" };
        versionLabel.Modulate = new Color(1, 1, 1, 0.45f);
        foot.AddChild(versionLabel);
        foreach (var (label, pressed) in links)
        {
            var link = new Button { Text = label, ThemeTypeVariation = "Link", FocusMode = FocusModeEnum.None };
            link.Pressed += pressed;
            foot.AddChild(link);
        }
        AddChild(foot);
        Restyle();
        Resized += Fit;
    }

    public bool Shown { get; private set; } = true;

    // The name and the cards shrink with a short window, so the last card never runs into the foot.
    private int LogoSize => (int)Math.Clamp(Size.Y * 0.12f, 64, 110);

    private void Fit()
    {
        if (Size.Y <= 0) return;
        var card = Math.Clamp(Size.Y * 0.078f, 52, 68);
        foreach (var button in _buttons) button.CustomMinimumSize = new Vector2(440, card);
        _column.AddThemeConstantOverride("separation", (int)Math.Clamp(Size.Y * 0.012f, 6, 12));
        foreach (var part in new[] { _logoLeft, _logoRight }) part.AddThemeFontSizeOverride("font_size", LogoSize);
    }

    private static Label LogoPart(string text) => new() { Text = text };

    // A new line under the name, faded in - when the voice it speaks in has changed.
    public void RefreshLine()
    {
        _tagline.Text = _line() ?? DefaultLine;
        _tagline.Modulate = new Color(1, 1, 1, 0);
        _tagline.CreateTween().TweenProperty(_tagline, "modulate:a", 0.8f, 0.5f);
    }

    // After a change of colour scheme, and each time it comes up - with a new line each time.
    public void Restyle()
    {
        _tagline.Text = _line() ?? DefaultLine;
        _tagline.AddThemeFontOverride("font", UiTheme.Display);
        _tagline.AddThemeFontSizeOverride("font_size", 21);
        foreach (var (part, color) in new[] { (_logoLeft, Colors.White), (_logoRight, UiTheme.Of(Palette.Accent)) })
        {
            part.AddThemeFontOverride("font", UiTheme.Display);
            part.AddThemeFontSizeOverride("font_size", LogoSize);
            part.AddThemeColorOverride("font_color", color);
            part.AddThemeColorOverride("font_shadow_color", UiTheme.Of(Palette.Accent, 0.55f));
            part.AddThemeConstantOverride("shadow_outline_size", 12);
            part.AddThemeConstantOverride("shadow_offset_x", 0);
            part.AddThemeConstantOverride("shadow_offset_y", 0);
        }
        foreach (var (card, subtitle) in _cards)
        {
            subtitle.Text = card.Subtitle() ?? "";
            subtitle.Visible = subtitle.Text.Length > 0;
        }
    }

    public void ShowScreen(bool shown)
    {
        if (shown == Shown) return;
        Shown = shown;
        if (shown) Restyle();
        MouseFilter = shown ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        _tween?.Kill();
        Visible = true;
        _tween = CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        _tween.TweenProperty(this, "modulate:a", shown ? 1f : 0f, shown ? 0.6f : 0.45f);
        if (!shown) _tween.TweenCallback(Callable.From(() => Visible = false));
    }

    private Button Card(StartCard card)
    {
        var button = new Button
        {
            ThemeTypeVariation = card.Primary ? "CardPrimary" : "Card",
            FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(440, 66),
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
        };
        button.Pressed += card.Pressed;
        var content = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        content.SetAnchorsPreset(LayoutPreset.FullRect);
        content.AddThemeConstantOverride("separation", 16);
        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right" }) margin.AddThemeConstantOverride($"margin_{side}", 18);
        margin.AddChild(content);
        content.AddChild(new TextureRect
        {
            Texture = Icons.Get(card.Icon, 24, card.Primary ? "#FFFFFF" : null),
            StretchMode = TextureRect.StretchModeEnum.KeepCentered,
            CustomMinimumSize = new Vector2(30, 0),
            MouseFilter = MouseFilterEnum.Ignore,
        });
        var text = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        text.AddThemeConstantOverride("separation", 0);
        var title = new Label { Text = card.Title, MouseFilter = MouseFilterEnum.Ignore };
        title.AddThemeFontOverride("font", UiTheme.Bold);
        title.AddThemeFontSizeOverride("font_size", 19);
        if (card.Primary) title.AddThemeColorOverride("font_color", Colors.White);
        var subtitle = new Label { MouseFilter = MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0.65f) };
        subtitle.AddThemeFontSizeOverride("font_size", 13);
        if (card.Primary) subtitle.AddThemeColorOverride("font_color", Colors.White);
        text.AddChild(title);
        text.AddChild(subtitle);
        content.AddChild(text);
        button.AddChild(margin);
        _cards.Add((card, subtitle));
        _buttons.Add(button);

        // Leaning in under the pointer.
        button.Resized += () => button.PivotOffset = button.Size / 2;
        button.MouseEntered += () => button.CreateTween().TweenProperty(button, "scale", new Vector2(1.03f, 1.03f), 0.18f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        button.MouseExited += () => button.CreateTween().TweenProperty(button, "scale", Vector2.One, 0.2f);
        return button;
    }

    private static ImageTexture? LoadLogo(string? resources)
    {
        if (resources is null || Image.LoadFromFile(Path.Combine(resources, "icons", "qUloN.png")) is not { } logo) return null;
        return ImageTexture.CreateFromImage(logo);
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _time += delta;
        for (var i = 0; i < _motes.Length; i++)
        {
            var m = _motes[i];
            m.Y -= m.Speed * (float)delta;
            if (m.Y < -0.1f) m = ((float)_random.NextDouble(), 1.1f, m.Depth, m.Speed, m.Hue);
            _motes[i] = m;
        }
        QueueRedraw();
        _bokeh.QueueRedraw();
    }

    // The stage behind: dark, with a glow where the light falls.
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), UiTheme.Of(Palette.SurfaceDarkest));
        Glow.Draw(this, new Vector2(Size.X * 0.3f, Size.Y * 0.42f), Size.X * 0.7f, UiTheme.Of(Palette.Background, 0.9f), 0.8f);
    }

    private void PaintBokeh(PaintLayer c)
    {
        var mouse = GetLocalMousePosition() / Size - new Vector2(0.5f, 0.5f);
        foreach (var (x, y, depth, _, hue) in _motes)
        {
            var at = new Vector2((x + mouse.X * 0.04f * depth) * Size.X, (y + mouse.Y * 0.03f * depth) * Size.Y);
            var color = UiTheme.Of(hue < 6 ? Palette.Accent : hue < 9 ? Palette.Cool : Palette.AccentHover);
            Glow.Draw(c, at, 10 + 26 * depth, new Color(color, 0.14f + 0.1f * MathF.Sin((float)_time + hue)));
        }
    }
}
