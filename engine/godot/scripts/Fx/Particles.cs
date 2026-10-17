using System;
using System.Collections.Generic;
using Godot;

namespace Gooner.Engine.Fx;

public enum ParticleKind
{
    Spark,
    Dot,
    Shard,
}

// The effect layer's particles and shock waves - sparks with a streak behind them, soft glowing
// dots, tumbling shards - drawn additively over the picture. Physics only after the fact: a particle
// is born where something already happened, so nothing it does can be late for a beat.
//
// It runs on its own time, which the climax slows down for a moment; the session clock never is.
public partial class Particles : Control
{
    private const int MaxParticles = 5000;

    private struct Particle
    {
        public Vector2 Position, Velocity;
        public float Life, MaxLife, Size, Rotation, Spin, Gravity, Drag;
        public Color Color;
        public ParticleKind Kind;
    }

    private struct Wave
    {
        public Vector2 Position;
        public Color Color;
        public float Radius, Speed, Life, MaxLife, Squash, Width;
    }

    private readonly List<Particle> _particles = [];
    private readonly List<Wave> _waves = [];
    private readonly Random _random = new();

    public Particles()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
    }

    // 1 is real time; the climax slows the decoration down and lets it come back.
    public float TimeScale { get; set; } = 1;

    public int Count => _particles.Count;

    // `count` of them flying out from `at` within `spread` radians around `direction`.
    public void Emit(Vector2 at, Color color, float count, float speed, ParticleKind kind = ParticleKind.Spark,
        float direction = 0, float spread = MathF.Tau, float life = 0.6f, float size = 3, float gravity = 0, float drag = 2.2f)
    {
        var n = (int)MathF.Round(count);
        for (var i = 0; i < n; i++)
        {
            var angle = direction + (float)(_random.NextDouble() - 0.5) * spread;
            var velocity = speed * (0.35f + (float)_random.NextDouble() * 0.85f);
            Add(at, new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * velocity, color, kind,
                life * (0.6f + (float)_random.NextDouble() * 0.7f), size * (0.6f + (float)_random.NextDouble() * 0.9f), gravity, drag);
        }
    }

    public void Add(Vector2 at, Vector2 velocity, Color color, ParticleKind kind, float life, float size, float gravity = 0, float drag = 2.2f)
    {
        if (_particles.Count >= MaxParticles) _particles.RemoveAt(0);
        _particles.Add(new Particle
        {
            Position = at, Velocity = velocity, Color = color, Kind = kind, MaxLife = life, Size = size,
            Gravity = gravity, Drag = drag, Rotation = (float)_random.NextDouble() * MathF.Tau, Spin = (float)(_random.NextDouble() - 0.5) * 16,
        });
    }

    // A ring of light spreading from `at`; squash flattens it onto a floor.
    public void Ring(Vector2 at, Color color, float speed, float life, float squash = 1, float width = 3, float radius = 8) =>
        _waves.Add(new Wave { Position = at, Color = color, Speed = speed, MaxLife = life, Squash = squash, Width = width, Radius = radius });

    public void Clear()
    {
        _particles.Clear();
        _waves.Clear();
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta * TimeScale;
        for (var i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Life += dt;
            if (p.Life >= p.MaxLife)
            {
                _particles[i] = _particles[^1];
                _particles.RemoveAt(_particles.Count - 1);
                continue;
            }
            var drag = MathF.Exp(-p.Drag * dt);
            p.Velocity = new Vector2(p.Velocity.X * drag, p.Velocity.Y * drag + p.Gravity * dt);
            p.Position += p.Velocity * dt;
            p.Rotation += p.Spin * dt;
            _particles[i] = p;
        }
        for (var i = _waves.Count - 1; i >= 0; i--)
        {
            var w = _waves[i];
            w.Life += dt;
            if (w.Life >= w.MaxLife)
            {
                _waves.RemoveAt(i);
                continue;
            }
            w.Radius += w.Speed * dt * (1 - 0.6f * w.Life / w.MaxLife);
            _waves[i] = w;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var w in _waves)
        {
            var k = 1 - w.Life / w.MaxLife;
            DrawSetTransform(w.Position, 0, new Vector2(1, w.Squash));
            DrawArc(Vector2.Zero, w.Radius, 0, MathF.Tau, 64, Fade(w.Color, k * 0.85f), w.Width * k + 0.5f, true);
        }
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        foreach (var p in _particles)
        {
            var k = 1 - p.Life / p.MaxLife;
            switch (p.Kind)
            {
                case ParticleKind.Spark:
                    var speed = p.Velocity.Length();
                    var tail = speed > 0 ? p.Velocity / speed * (speed * 0.035f + 2) : Vector2.Zero;
                    DrawLine(p.Position, p.Position - tail, Fade(p.Color, k), p.Size * k + 0.4f, true);
                    break;
                case ParticleKind.Dot:
                    Glow.Draw(this, p.Position, p.Size * 4 * k + 1, Fade(p.Color, k));
                    break;
                default:
                    DrawSetTransform(p.Position, p.Rotation, Vector2.One);
                    DrawRect(new Rect2(-p.Size, -p.Size * 0.35f, p.Size * 2, p.Size * 0.7f), Fade(p.Color, k));
                    DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                    break;
            }
        }
    }

    private static Color Fade(Color color, float alpha) => new(color.R, color.G, color.B, color.A * Math.Clamp(alpha, 0, 1));
}
