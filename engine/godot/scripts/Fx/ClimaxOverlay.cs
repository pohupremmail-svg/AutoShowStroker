using System;
using System.Collections.Generic;
using Gooner.Core.App;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine.Fx;

// The climax, over the picture: ClimaxBanner's word - CUM, RUINED, DENIED - slamming in huge, with
// a white flash, the screen shaking, its colours tearing apart, a burst of particles and the
// decoration slowing down for a moment (never the beat). Then each outcome its own way: fireworks
// for a climax, a glitching word for a ruined one, the note track shattering and going dark for a
// denial.
//
// It reacts to the banner and nothing else, so a fake climax - "cum" until it is revealed - runs
// exactly the real one's path until then; at the reveal the word breaks apart. When the session is
// over the word stays, up and out of the way of the questions that follow.
public partial class ClimaxOverlay : Control
{
    private readonly Particles _particles;
    private readonly Label _banner;
    private readonly ColorRect _flash;
    private readonly Random _random = new();
    private readonly List<(double At, Action Run)> _later = [];
    private string? _shown;
    private bool _parked;
    private double _time, _fireworksLeft, _nextFirework, _glitchLeft;
    private Tween? _tween;

    public ClimaxOverlay(Particles particles)
    {
        _particles = particles;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _flash = new ColorRect { Color = new Color(1, 1, 1, 0), MouseFilter = MouseFilterEnum.Ignore };
        _flash.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_flash);
        _banner = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        AddChild(_banner);
    }

    // The note track's denial: it shatters what was still coming.
    public event Action? Denied;

    // How hard the screen shakes, in pixels, and how far the picture's colours are pulled apart.
    public float Shake { get; private set; }

    public float Aberration { get; private set; }

    public float Strength { get; set; } = 1;

    // Whether the word is up: the callout steps down below it.
    public bool Showing => _shown is not null && !_parked;

    // Once a frame, after the app's frame: what the banner says now, and whether a session runs.
    public void Sync(ClimaxBanner banner, bool running)
    {
        var text = banner.Text;
        if (text is not null && _shown is null) Enter(text);
        else if (text is null && _shown is not null)
        {
            if (running) Shatter();
            else Leave();
        }
        else if (text is not null && text != _shown) Enter(text);
        if (_shown is not null && banner.Color is { } color)
        {
            _banner.AddThemeColorOverride("font_color", UiTheme.Of(color));
            _banner.AddThemeColorOverride("font_shadow_color", UiTheme.Of(color, 0.5f));
        }
        if (_shown is not null && !running && !_parked) Park();
    }

    private Vector2 Centre => new(Size.X / 2, Size.Y * 0.4f);

    private void Enter(string text)
    {
        _shown = text;
        _parked = false;
        var q = Strength;
        var color = UiTheme.Of(text switch { "RUINED" => Palette.Ruined, "DENIED" => Palette.Denied, _ => Palette.Accent });
        Style(text, color);

        _tween?.Kill();
        _banner.Visible = true;
        _banner.Modulate = new Color(1, 1, 1, 0);
        _banner.Scale = new Vector2(2.6f, 2.6f);
        _tween = CreateTween().SetParallel().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
        _tween.TweenProperty(_banner, "modulate:a", 1f, 0.3f);
        _tween.TweenProperty(_banner, "scale", Vector2.One, 0.52f);
        _flash.Color = new Color(1, 1, 1, 0.85f * Math.Min(1, q));
        CreateTween().TweenProperty(_flash, "color:a", 0f, 0.52f).SetEase(Tween.EaseType.Out);

        Shake = 24 * q;
        Aberration = 16 * q;
        _particles.TimeScale = 0.18f;
        var at = Centre;
        _particles.Emit(at, color, 170 * q, 950, life: 1.5f, drag: 1.5f);
        _particles.Emit(at, Colors.White, 40 * q, 600, life: 1.1f, drag: 1.8f);
        _particles.Emit(at, color, 60 * q, 400, ParticleKind.Dot, life: 1.6f, size: 4, drag: 1.2f);
        _particles.Emit(at, color, 30 * q, 700, ParticleKind.Shard, life: 1.8f, size: 7, gravity: 500, drag: 1);
        _particles.Ring(at, color, 1500, 1.1f, 1, 6);
        Later(0.13, () => _particles.Ring(Centre, Colors.White, 1200, 1.1f, 1, 4.5f));
        Later(0.26, () => _particles.Ring(Centre, Colors.White, 900, 1.1f, 1, 3));

        _fireworksLeft = text == "CUM" ? 3.0 : 0;
        _glitchLeft = text == "RUINED" ? 1.5 : 0;
        if (text == "DENIED") Denied?.Invoke();
    }

    private void Style(string text, Color color)
    {
        _banner.Text = text;
        _banner.AddThemeFontOverride("font", UiTheme.Display);
        _banner.AddThemeFontSizeOverride("font_size", (int)Math.Clamp(Size.X * 0.14f, 90, 250));
        _banner.AddThemeColorOverride("font_color", color);
        _banner.AddThemeColorOverride("font_shadow_color", new Color(color, 0.5f));
        _banner.AddThemeConstantOverride("shadow_outline_size", 14);
        _banner.AddThemeConstantOverride("shadow_offset_x", 0);
        _banner.AddThemeConstantOverride("shadow_offset_y", 0);
    }

    // A fake revealed: the word breaks into pieces that fall away.
    private void Shatter()
    {
        _shown = null;
        _fireworksLeft = 0;
        var rect = _banner.GetGlobalRect();
        var color = _banner.GetThemeColor("font_color");
        var centre = rect.GetCenter();
        for (var i = 0; i < 160 * Strength; i++)
        {
            var at = rect.Position + new Vector2((float)_random.NextDouble() * rect.Size.X, (float)_random.NextDouble() * rect.Size.Y) - GlobalPosition;
            var away = (at + GlobalPosition - centre).Normalized() * (200 + (float)_random.NextDouble() * 500) + new Vector2(0, -120);
            _particles.Add(at, away, color, ParticleKind.Shard, 1 + (float)_random.NextDouble() * 0.8f, 3 + (float)_random.NextDouble() * 6, gravity: 900, drag: 0.8f);
        }
        Aberration = Math.Max(Aberration, 8 * Strength);
        _glitchLeft = 0.5;
        _tween?.Kill();
        _tween = CreateTween().SetParallel();
        _tween.TweenProperty(_banner, "modulate:a", 0f, 0.26f);
        _tween.TweenProperty(_banner, "scale", new Vector2(1.15f, 1.15f), 0.26f);
        _tween.Chain().TweenCallback(Callable.From(() => _banner.Visible = false));
    }

    // The session is over and the word stays - small, up top, out of the questions' way.
    private void Park()
    {
        _parked = true;
        _fireworksLeft = 0;
        _tween?.Kill();
        _tween = CreateTween().SetParallel().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        _tween.TweenProperty(_banner, "scale", new Vector2(0.42f, 0.42f), 0.7f);
        _tween.TweenMethod(Callable.From<float>(y => _parkY = y), 0.4f, 0.16f, 0.7f);
    }

    private float _parkY = 0.4f;

    private void Leave()
    {
        _shown = null;
        _parked = false;
        _parkY = 0.4f;
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenProperty(_banner, "modulate:a", 0f, 0.3f);
        _tween.TweenCallback(Callable.From(() => _banner.Visible = false));
    }

    private void Later(double seconds, Action run) => _later.Add((_time + seconds, run));

    public override void _Process(double delta)
    {
        _time += delta;
        for (var i = _later.Count - 1; i >= 0; i--)
        {
            if (_later[i].At > _time) continue;
            var run = _later[i].Run;
            _later.RemoveAt(i);
            run();
        }
        var dt = (float)delta;
        Shake *= MathF.Exp(-dt * 5);
        if (Shake < 0.3f) Shake = 0;
        Aberration *= MathF.Exp(-dt * 2.2f);
        if (_glitchLeft > 0)
        {
            _glitchLeft -= delta;
            Aberration = Math.Max(Aberration, (float)_random.NextDouble() * 12 * Strength);
        }
        _particles.TimeScale += (1 - _particles.TimeScale) * Math.Min(1, dt * 1.2f);
        if (_fireworksLeft > 0)
        {
            _fireworksLeft -= delta;
            _nextFirework -= delta;
            if (_nextFirework <= 0)
            {
                _nextFirework = 0.33;
                Firework();
            }
        }

        // Centred on its spot; a ruined word jitters while it glitches.
        _banner.Size = _banner.GetMinimumSize();
        _banner.PivotOffset = _banner.Size / 2;
        var jitter = _glitchLeft > 0 ? new Vector2((float)(_random.NextDouble() - 0.5) * 14, 0) : Vector2.Zero;
        _banner.Position = (new Vector2(Size.X / 2, Size.Y * _parkY) - _banner.Size / 2 + jitter).Round();
    }

    private void Firework()
    {
        var at = new Vector2(Size.X * (0.15f + (float)_random.NextDouble() * 0.7f), Size.Y * (0.12f + (float)_random.NextDouble() * 0.4f));
        var colors = new[] { Palette.Accent, Palette.AccentHover, "#FFFFFF", Palette.Cool };
        var color = UiTheme.Of(colors[_random.Next(colors.Length)]);
        _particles.Emit(at, color, 46 * Strength, 380, life: 1.2f, gravity: 160, drag: 1.6f);
        _particles.Emit(at, color, 10 * Strength, 120, ParticleKind.Dot, life: 1, size: 3);
        _particles.Ring(at, color, 300, 0.6f, 1, 2);
    }
}
