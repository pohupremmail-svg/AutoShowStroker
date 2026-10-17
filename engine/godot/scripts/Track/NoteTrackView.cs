using System;
using System.Collections.Generic;
using Gooner.Core.App;
using Gooner.Core.Beat;
using Gooner.Core.Randomness;
using Gooner.Core.Track;
using Gooner.Engine.Fx;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine.Track;

// The note track of the effect layer, laid over the whole picture in the style the look picks:
// Highway, Hypno Ring, Stream or Tunnel (Gooner.Core.Track has their geometry). Every frame it is
// handed BeatHandler.Forecast - the exact moments the click is mixed against, and the stretches of
// the plan they fall in - so a note lands on its click, a coming pause flies in like the notes do,
// and a change of rhythm arrives at the moment it happens.
//
// Stateless where it matters, like the track before it: nothing is kept about where a note is, so
// nothing can drift. What it keeps is decoration - how bright the hit zone still is, how far into a
// pause the colours have faded - and the count of notes that have landed, which keeps a note on the
// same spiral arm all the way in.
//
// A finale is drawn like any other rhythm: a run-in that looked different would tell the user the
// next climax is the real one, and a fake only works while nothing gives it away.
public partial class NoteTrackView : Control
{
    // How far ahead the track shows. Fixed, so a faster beat reads as denser notes.
    public const double LeadTimeSec = 2.5;
    private const double ShowFrom = 0.0;

    private readonly Look _look;
    private readonly Particles _particles;
    private readonly WanderingFocus _focus;
    private readonly PaintLayer _floor = new(additive: false);
    private readonly PaintLayer _light = new(additive: true);
    private readonly Random _flicker = new();
    private TrackForecast _forecast = new([], []);
    private double _now;
    private long _landed;
    private float _hit, _pauseMix, _power = 1, _denied;
    private double _flickerLeft;

    public NoteTrackView(Look look, Particles particles, IRandomSource random)
    {
        _look = look;
        _particles = particles;
        _focus = new WanderingFocus(random);
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _floor.Paint = PaintFloor;
        _light.Paint = PaintLight;
        AddChild(_floor);
        AddChild(_light);
    }

    // A new rhythm or a pause arriving on the hit zone - the HUD names it.
    public event Action<string>? RhythmArrived;

    // A note landing, 1 for a long one and less for a short one - the picture pulses with it.
    public event Action<float>? NoteLanded;

    private string Style => _look.NoteTrack;

    private float Strength => (float)_look.EffectScale;

    // Where the Hypno Ring is, in pixels, and the radius around it the picture stays clear in.
    public Vector2 Focus => new((float)_focus.X * Size.X, (float)_focus.Y * Size.Y);

    public float FocusRadius => new HypnoRing(Size.X, Size.Y, _look.FocusWanders).Radius;

    public bool ShowsRing => Style == "ring";

    // How bright the hit zone still is from the last note: 1 on the beat, fading.
    public float Flare => _hit;

    // A new session: no note has landed yet, and nothing is switched off.
    public void Reset()
    {
        _forecast = new TrackForecast([], []);
        _landed = 0;
        _denied = 0;
        _power = 1;
        _flickerLeft = 0;
    }

    public void Show(TrackForecast forecast, double now, double delta)
    {
        // What the last frame still had coming and is due by now has landed - this frame, where it
        // was drawn, so the burst comes with the click.
        foreach (var note in _forecast.Notes)
        {
            if (!note.Audible || note.At > now) continue;
            Landed(note, now);
        }
        for (var i = 1; i < _forecast.Spans.Count; i++)
        {
            var span = _forecast.Spans[i];
            if (span.Start > _now && span.Start <= now) Arrived(span, now);
        }
        _now = now;
        _forecast = forecast;
        _focus.Step(delta, now, wander: _look.FocusWanders && Style == "ring");

        var dt = (float)delta;
        _hit *= MathF.Exp(-dt * 9);
        var paused = forecast.Spans.Count > 0 && forecast.Spans[0].Kind == "pause" && forecast.Spans[0].Start <= now;
        _pauseMix += ((paused ? 1 : 0) - _pauseMix) * Math.Min(1, dt * 4);
        if (_flickerLeft > 0)
        {
            _flickerLeft -= delta;
            _power = _flickerLeft <= 0 ? 0.12f : _flicker.NextDouble() < 0.5 ? 0.15f : 0.7f * (float)Math.Clamp(_flickerLeft, 0, 1);
        }
        _floor.QueueRedraw();
        _light.QueueRedraw();
    }

    // A denial: the beat stops dead, and what was still coming shatters where it was.
    public void Shatter()
    {
        foreach (var (note, ordinal) in Coming())
        {
            var (at, _, _) = NoteAt(note, ordinal);
            _particles.Emit(at, NoteColor(note.Weight), 10 * Strength, 260, ParticleKind.Shard, gravity: 700, life: 1.4f, size: 5, drag: 1);
        }
        _forecast = new TrackForecast([], []);
        _denied = 1;
        _flickerLeft = 1.0;
    }

    // --- what lands, what arrives ---

    private void Landed(ScheduledNote note, double now)
    {
        var ordinal = _landed++;
        var (at, direction, squash) = HitPoint(note, ordinal, now);
        var color = NoteColor(note.Weight);
        var big = note.Weight <= 1;
        var q = Strength;
        _particles.Emit(at, color, (big ? 18 : 10) * q, big ? 430 : 300, direction: direction,
            spread: Style == "ring" ? 2.2f : MathF.Tau * 0.8f, life: 0.5f, gravity: Style == "highway" ? 500 : 0);
        _particles.Emit(at, Colors.White, 5 * q, 200, ParticleKind.Dot, life: 0.35f, size: 3);
        if (big && q > 0.6f)
            _particles.Emit(at, color, 4 * q, 280, ParticleKind.Shard, gravity: 900, life: 0.9f, size: 5, drag: 1);
        if (Style == "ring")
            _particles.Ring(Focus, color, 260, 1.3f, 1, 2.2f, FocusRadius);
        else
            _particles.Ring(at, color, big ? 520 : 380, 0.45f, squash, big ? 4 : 2.5f);
        _hit = 1;
        NoteLanded?.Invoke(big ? 1 : 0.6f);
    }

    private void Arrived(ForecastSpan span, double now)
    {
        if (span.Kind == "pause")
        {
            RhythmArrived?.Invoke("Pause");
            return;
        }
        RhythmArrived?.Invoke(span.PatternName ?? "");
        var (at, _, _) = HitPoint(new ScheduledNote(now, true, 1), _landed, now);
        _particles.Emit(at, Of(Palette.Cool), 26 * Strength, 520, direction: -MathF.PI / 2, spread: MathF.PI * 1.2f, life: 0.7f);
    }

    // The audible notes still to come within the lead time, each with its ordinal.
    private IEnumerable<(ScheduledNote Note, long Ordinal)> Coming()
    {
        var ordinal = _landed;
        foreach (var note in _forecast.Notes)
        {
            if (!note.Audible) continue;
            if (note.At - _now is >= ShowFrom and <= LeadTimeSec) yield return (note, ordinal);
            ordinal++;
        }
    }

    private double Ahead(double at) => (at - _now) / LeadTimeSec;

    private (Vector2 At, float Direction, float Squash) HitPoint(ScheduledNote note, long ordinal, double now)
    {
        var size = NoteSize(note.Weight);
        switch (Style)
        {
            case "ring":
                var ring = new HypnoRing(Size.X, Size.Y, _look.FocusWanders);
                var p = V(ring.At(0, (int)(ordinal & 1), now, P(Focus)));
                return (p, (p - Focus).Angle(), 1);
            case "stream":
                var stream = new StreamLane(Size.X, Size.Y);
                return (new Vector2(stream.HitX, stream.CenterY), MathF.PI, 1);
            case "tunnel":
                var tunnel = new TunnelLane(Size.X, Size.Y);
                var t = V(tunnel.At(0, note.At, now));
                return (t, (t - new Vector2(tunnel.CenterX, tunnel.CenterY)).Angle(), 1);
            default:
                var lane = new HighwayLane(Size.X, Size.Y);
                return (new Vector2(lane.CenterX, lane.HitY - 42 * size * 0.7f), -MathF.PI / 2, 0.28f);
        }
    }

    // Where a coming note is drawn: position, radius, opacity (fading in at the far end).
    private (Vector2 At, float Radius, float Alpha) NoteAt(ScheduledNote note, long ordinal)
    {
        var ahead = Ahead(note.At);
        var size = NoteSize(note.Weight);
        switch (Style)
        {
            case "ring":
                var ring = new HypnoRing(Size.X, Size.Y, _look.FocusWanders);
                return (V(ring.At(ahead, (int)(ordinal & 1), _now, P(Focus))), (9 + 17 * (1 - (float)ahead)) * size, FadeIn(ahead, 0.15));
            case "stream":
                var stream = new StreamLane(Size.X, Size.Y);
                return (new Vector2(stream.X(ahead), stream.CenterY), 17 * size, FadeIn(ahead, 0.07));
            case "tunnel":
                var tunnel = new TunnelLane(Size.X, Size.Y);
                return (V(tunnel.At(ahead, note.At, _now)), 40 * size / (1 + (float)ahead * TunnelLane.Depth), FadeIn(ahead, 0.15));
            default:
                var lane = new HighwayLane(Size.X, Size.Y).At(ahead, _now);
                var r = 42 * lane.Scale * size;
                return (new Vector2(lane.X, lane.Y - r * 0.7f), r, FadeIn(ahead, 0.18));
        }
    }

    private static float FadeIn(double ahead, double edge) => ahead > 1 - edge ? (float)Math.Clamp((1 - ahead) / edge, 0, 1) : 1;

    private static float NoteSize(int weight) => weight switch { <= 1 => 1f, 2 => 0.78f, 3 => 0.64f, _ => 0.56f };

    // Long notes in the accent, shorter ones lighter, the shortest in the scheme's second colour.
    private static Color NoteColor(int weight) => weight switch
    {
        <= 1 => Of(Palette.Accent),
        2 => Of(Palette.AccentHover),
        _ => Of(Palette.Cool),
    };

    private Color Rail()
    {
        var rail = Of(Palette.Accent).Lerp(Of(Palette.Pause), _pauseMix);
        return _denied > 0 ? rail.Lerp(Of(Palette.Denied), _denied) : rail;
    }

    private IEnumerable<ForecastSpan> Pauses()
    {
        foreach (var span in _forecast.Spans)
            if (span.Kind == "pause" && span.End > _now && span.Start < _now + LeadTimeSec) yield return span;
    }

    // Where a later rhythm takes over within the lead time.
    private IEnumerable<ForecastSpan> Changes()
    {
        for (var i = 1; i < _forecast.Spans.Count; i++)
        {
            var span = _forecast.Spans[i];
            if (span.Kind != "pause" && span.Start >= _now && span.Start <= _now + LeadTimeSec) yield return span;
        }
    }

    private ForecastSpan? RunningPause =>
        _forecast.Spans.Count > 0 && _forecast.Spans[0] is { Kind: "pause" } pause && pause.Start <= _now ? pause : null;

    // --- painting ---

    private void PaintFloor(PaintLayer c)
    {
        switch (Style)
        {
            case "ring": RingFloor(c); break;
            case "stream": StreamFloor(c); break;
            case "tunnel": TunnelFloor(c); break;
            default: HighwayFloor(c); break;
        }
    }

    private void PaintLight(PaintLayer c)
    {
        switch (Style)
        {
            case "ring": RingLight(c); break;
            case "stream": StreamLight(c); break;
            case "tunnel": TunnelLight(c); break;
            default: HighwayLight(c); break;
        }
    }

    // A: Highway.

    private const int LaneSteps = 36;

    private LanePoint[] LanePoints(HighwayLane lane)
    {
        var points = new LanePoint[LaneSteps + 1];
        for (var i = 0; i <= LaneSteps; i++) points[i] = lane.At(i / (double)LaneSteps, _now);
        return points;
    }

    private void HighwayFloor(PaintLayer c)
    {
        var lane = new HighwayLane(Size.X, Size.Y);
        var points = LanePoints(lane);
        // The floor darkens the picture under the lane, so the notes read on any image.
        for (var i = 0; i < LaneSteps; i++)
        {
            var (a, b) = (points[i], points[i + 1]);
            var z = i / (float)LaneSteps;
            c.DrawColoredPolygon(
                [new(a.X - a.HalfWidth, a.Y), new(a.X + a.HalfWidth, a.Y), new(b.X + b.HalfWidth, b.Y), new(b.X - b.HalfWidth, b.Y)],
                Shade(0.5f * (1 - z * 0.92f) * (0.55f + 0.45f * _power)));
        }
        var p0 = points[0];
        c.DrawPolygon(
            [new(p0.X - p0.HalfWidth, lane.HitY), new(p0.X + p0.HalfWidth, lane.HitY), new(lane.CenterX + p0.HalfWidth * 1.25f, Size.Y), new(lane.CenterX - p0.HalfWidth * 1.25f, Size.Y)],
            [Shade(0.5f), Shade(0.5f), Shade(0), Shade(0)]);

        // Pauses fly in as slabs on the lane.
        foreach (var pause in Pauses())
        {
            var z0 = Math.Max(0, Ahead(pause.Start));
            var z1 = Math.Min(1, Ahead(pause.End));
            if (z1 - z0 < 0.002) continue;
            const int steps = 12;
            var slab = new Vector2[(steps + 1) * 2];
            for (var i = 0; i <= steps; i++)
            {
                var p = lane.At(z0 + (z1 - z0) * i / steps, _now);
                slab[i] = new Vector2(p.X - p.HalfWidth, p.Y);
                slab[slab.Length - 1 - i] = new Vector2(p.X + p.HalfWidth, p.Y);
            }
            c.DrawColoredPolygon(slab, Fade(Of(Palette.Pause), 0.3f));
            if (z0 > 0.001)
            {
                var front = lane.At(z0, _now);
                c.DrawCentred(UiTheme.Bold, new Vector2(front.X, front.Y - 8 * front.Scale), "PAUSE", (int)Math.Max(9, 30 * front.Scale), Fade(Colors.White, 0.85f * (1 - (float)z0)));
            }
        }
        foreach (var change in Changes())
        {
            var z = Ahead(change.Start);
            var p = lane.At(z, _now);
            c.DrawString(UiTheme.Bold, new Vector2(p.X + p.HalfWidth * 1.2f, p.Y + 4), (change.PatternName ?? "").ToUpperInvariant(),
                HorizontalAlignment.Left, -1, (int)Math.Max(9, 22 * p.Scale), Fade(Colors.White, 0.8f * (1 - (float)z)));
        }
        Countdown(c, new Vector2(lane.CenterX, lane.HitY - 78), centred: false);
    }

    private void HighwayLight(PaintLayer c)
    {
        var lane = new HighwayLane(Size.X, Size.Y);
        var points = LanePoints(lane);
        var rail = Rail();
        // A grid scrolling towards you: time moves even while nothing plays.
        for (var k = Math.Ceiling(_now / 0.5) * 0.5; k < _now + LeadTimeSec; k += 0.5)
        {
            var z = Ahead(k);
            var p = lane.At(z, _now);
            c.DrawLine(new(p.X - p.HalfWidth, p.Y), new(p.X + p.HalfWidth, p.Y), Fade(rail, 0.07f * (1 - (float)z) * _power), Math.Max(1, 2 * p.Scale), true);
        }
        // A fret for every coming note: the rhythm reads before it arrives.
        foreach (var (note, _) in Coming())
        {
            var z = Ahead(note.At);
            var p = lane.At(z, _now);
            c.DrawLine(new(p.X - p.HalfWidth, p.Y), new(p.X + p.HalfWidth, p.Y), Fade(NoteColor(note.Weight), 0.24f * (1 - (float)z) * _power), Math.Max(1, 3 * p.Scale), true);
        }
        // A change of rhythm is a gate of light.
        foreach (var change in Changes())
        {
            var z = (float)Ahead(change.Start);
            var p = lane.At(z, _now);
            var cool = Of(Palette.Cool);
            c.DrawPolylineColors([new(p.X - p.HalfWidth * 1.15f, p.Y), new(p.X, p.Y), new(p.X + p.HalfWidth * 1.15f, p.Y)],
                [Fade(cool, 0), Fade(cool, 0.9f * (1 - z)), Fade(cool, 0)], Math.Max(2, 7 * p.Scale), true);
        }
        // The rails, fading into the distance.
        foreach (var side in new[] { -1f, 1f })
        {
            var edge = new Vector2[points.Length];
            var colors = new Color[points.Length];
            for (var i = 0; i < points.Length; i++)
            {
                edge[i] = new Vector2(points[i].X + side * points[i].HalfWidth, points[i].Y);
                colors[i] = Fade(rail, _power * (1 - i / (float)LaneSteps));
            }
            c.DrawPolylineColors(edge, Array.ConvertAll(colors, col => Fade(col, col.A * (0.16f + 0.3f * _hit))), 12, true);
            c.DrawPolylineColors(edge, Array.ConvertAll(colors, col => Fade(col, col.A * 0.95f)), 2.6f, true);
        }
        // The hit zone: a ring on the floor that flares on every beat.
        var rx = lane.HalfWidth * 0.62f * (1 + 0.12f * _hit);
        var hit = new Vector2(lane.CenterX, lane.HitY);
        Glow.Draw(c, hit, rx * 1.5f, Fade(Of(Palette.Accent), (0.25f + 0.6f * _hit) * _power), 0.22f);
        c.FillEllipse(hit, rx, rx * 0.2f, Fade(rail, (0.1f + 0.35f * _hit) * _power));
        c.DrawEllipse(hit, rx, rx * 0.2f, Fade(rail, 0.95f * _power), 3);
        // The notes, far to near.
        var coming = new List<(ScheduledNote Note, long Ordinal)>(Coming());
        for (var i = coming.Count - 1; i >= 0; i--)
        {
            var (note, ordinal) = coming[i];
            var (at, r, alpha) = NoteAt(note, ordinal);
            var color = NoteColor(note.Weight);
            var back = lane.At(Math.Min(1, Ahead(note.At) + 0.07), _now);
            var behind = new Vector2(back.X, back.Y - 42 * back.Scale * NoteSize(note.Weight) * 0.7f);
            c.DrawPolylineColors([behind, at], [Fade(color, 0), Fade(color, 0.55f * alpha)], r * 0.9f, true);
            NoteGlow(c, at, r, color, alpha);
        }
    }

    // B: Hypno Ring.

    private void RingFloor(PaintLayer c)
    {
        var ring = new HypnoRing(Size.X, Size.Y, _look.FocusWanders);
        // A soft shadow under the spiral, so it reads on a bright picture.
        Glow.Draw(c, Focus, ring.Reach * 1.15f, new Color(0.03f, 0.01f, 0.05f, 0.55f));
        if (RunningPause is { } pause)
        {
            var k = Fading(pause);
            c.DrawCentred(UiTheme.Bold, Focus + new Vector2(0, ring.Radius * 0.2f), Math.Ceiling(pause.End - _now).ToString(System.Globalization.CultureInfo.InvariantCulture),
                (int)(ring.Radius * 0.55f), Fade(Colors.White, 0.9f * k));
            c.DrawCentred(UiTheme.Bold, Focus - new Vector2(0, ring.Radius * 0.3f), "PAUSE", 13, Fade(Colors.White, 0.8f * k));
        }
    }

    private void RingLight(PaintLayer c)
    {
        var ring = new HypnoRing(Size.X, Size.Y, _look.FocusWanders);
        var centre = Focus;
        var rail = Rail();
        foreach (var pause in Pauses())
        {
            var s0 = Math.Max(0, Ahead(pause.Start));
            var s1 = Math.Min(1, Ahead(pause.End));
            if (s1 <= s0) continue;
            c.FillBand(centre, ring.RadiusAt(s0), ring.RadiusAt(s1), 1, Fade(Of(Palette.Pause), 0.16f));
            if (s0 > 0.001) c.DrawArc(centre, ring.RadiusAt(s0), 0, MathF.Tau, 96, Fade(Of(Palette.Pause), 0.9f), 3, true);
        }
        foreach (var change in Changes())
        {
            var s = (float)Ahead(change.Start);
            var r = ring.RadiusAt(s);
            c.DrawDashedEllipse(centre, r, r, Fade(Of(Palette.Cool), 0.75f * (1 - s)), 3, 28, (float)(_now * 0.6));
        }
        foreach (var arm in new[] { 0, 1 })
        {
            var spiral = new Vector2[51];
            for (var i = 0; i <= 50; i++) spiral[i] = V(ring.At(i / 50.0, arm, _now, P(centre)));
            c.DrawPolyline(spiral, Fade(rail, 0.11f * _power), 2, true);
        }
        var rr = ring.Radius * (1 + 0.09f * _hit);
        Glow.Draw(c, centre, rr * 1.9f, Fade(Of(Palette.Accent), (0.18f + 0.45f * _hit) * _power));
        c.DrawArc(centre, rr, 0, MathF.Tau, 96, Fade(rail, 0.18f * _power), 16, true);
        c.DrawArc(centre, rr, 0, MathF.Tau, 96, Fade(rail, 0.95f * _power), 4, true);
        for (var i = 0; i < 24; i++)
        {
            var a = i / 24f * MathF.Tau + (float)(_now * 0.35);
            var (r1, r2) = (rr + 10, rr + (i % 6 == 0 ? 22 : 15));
            c.DrawLine(centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r1, centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r2, Fade(rail, 0.5f * _power), 2, true);
        }
        foreach (var (note, ordinal) in Coming())
        {
            var (at, r, alpha) = NoteAt(note, ordinal);
            var color = NoteColor(note.Weight);
            var ahead = Ahead(note.At);
            var trail = new Vector2[7];
            for (var k = 0; k < trail.Length; k++) trail[k] = V(ring.At(ahead + k * 0.012, (int)(ordinal & 1), _now, P(centre)));
            c.DrawPolyline(trail, Fade(color, 0.45f * alpha), r * 0.8f, true);
            NoteGlow(c, at, r, color, alpha);
        }
    }

    // C: Stream.

    private void StreamFloor(PaintLayer c)
    {
        var lane = new StreamLane(Size.X, Size.Y);
        var strip = UiTheme.Box(new Color(0.06f, 0.025f, 0.1f, 0.45f), 24);
        strip.SetBorderWidthAll(1);
        strip.BorderColor = Fade(Of(Palette.Text), 0.16f);
        c.DrawStyleBox(strip, new Rect2(StreamLane.Left, lane.Top, lane.Right - StreamLane.Left, lane.Height));
        foreach (var pause in Pauses())
        {
            var x0 = lane.X(Math.Max(0, Ahead(pause.Start)));
            var x1 = lane.X(Math.Min(1, Ahead(pause.End)));
            if (x1 - x0 < 1) continue;
            var h = lane.Height * 0.6f;
            c.DrawStyleBox(UiTheme.Box(Fade(Of(Palette.Pause), 0.45f), 10), new Rect2(x0, lane.CenterY - h / 2, x1 - x0, h));
            if (x1 - x0 > 70) c.DrawString(UiTheme.Bold, new Vector2(x0 + 12, lane.CenterY + 5), "PAUSE", HorizontalAlignment.Left, -1, 13, Fade(Colors.White, 0.85f));
        }
        foreach (var change in Changes())
            c.DrawString(UiTheme.Bold, new Vector2(lane.X(Ahead(change.Start)) + 6, lane.Top - 8), (change.PatternName ?? "").ToUpperInvariant(),
                HorizontalAlignment.Left, -1, 12, Fade(Colors.White, 0.8f));
        Countdown(c, new Vector2(lane.HitX + 140, lane.Top - 30), centred: false);
    }

    private void StreamLight(PaintLayer c)
    {
        var lane = new StreamLane(Size.X, Size.Y);
        var rail = Rail();
        c.DrawLine(new(lane.HitX, lane.CenterY), new(lane.Right - 20, lane.CenterY), Fade(rail, 0.12f * _power), 2, true);
        foreach (var change in Changes())
        {
            var x = lane.X(Ahead(change.Start));
            var cool = Of(Palette.Cool);
            c.DrawPolylineColors([new(x, lane.Top - 10), new(x, lane.CenterY), new(x, lane.Top + lane.Height + 10)],
                [Fade(cool, 0), Fade(cool, 0.9f), Fade(cool, 0)], 4, true);
        }
        Glow.Draw(c, new Vector2(lane.HitX, lane.CenterY), lane.Height * 0.9f, Fade(Of(Palette.Accent), (0.3f + 0.6f * _hit) * _power));
        c.DrawRect(new Rect2(lane.HitX - 3, lane.Top - 6, 6, lane.Height + 12), Fade(rail, 0.95f * _power));
        foreach (var (note, ordinal) in Coming())
        {
            var (at, r, alpha) = NoteAt(note, ordinal);
            var color = NoteColor(note.Weight);
            var tail = r * 5 + 30;
            c.DrawPolylineColors([at + new Vector2(tail, 0), at], [Fade(color, 0), Fade(color, 0.6f * alpha)], r * 1.1f, true);
            NoteGlow(c, at, r, color, alpha);
        }
    }

    // G: Tunnel.

    private void TunnelFloor(PaintLayer c)
    {
        var tunnel = new TunnelLane(Size.X, Size.Y);
        var centre = new Vector2(tunnel.CenterX, tunnel.CenterY);
        // Darker towards the frame, where the notes land; the middle of the picture stays clear.
        c.FillBand(centre, tunnel.Frame * 0.8f, tunnel.Frame * 1.08f, TunnelLane.Squash, new Color(0.03f, 0.01f, 0.05f, 0.2f));
        Countdown(c, centre + new Vector2(0, tunnel.Frame * 0.5f), centred: false);
    }

    private void TunnelLight(PaintLayer c)
    {
        var tunnel = new TunnelLane(Size.X, Size.Y);
        var centre = new Vector2(tunnel.CenterX, tunnel.CenterY);
        var rail = Rail();
        Vector2 Round(float radius, float angle) => centre + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius * TunnelLane.Squash);
        for (var i = 0; i < 16; i++)
        {
            var a = i / 16f * MathF.Tau + (float)(_now * 0.12);
            c.DrawPolylineColors([Round(tunnel.RadiusAt(1), a), Round(tunnel.Frame, a)], [Fade(rail, 0), Fade(rail, 0.14f * _power)], 1.5f, true);
        }
        for (var k = Math.Ceiling(_now / 0.25) * 0.25; k < _now + LeadTimeSec; k += 0.25)
        {
            var z = (float)Ahead(k);
            var r = tunnel.RadiusAt(z);
            c.DrawEllipse(centre, r, r * TunnelLane.Squash, Fade(rail, 0.16f * MathF.Pow(1 - z, 1.2f) * _power), 1.5f);
        }
        foreach (var pause in Pauses())
        {
            var z0 = Math.Max(0, Ahead(pause.Start));
            var z1 = Math.Min(1, Ahead(pause.End));
            if (z1 <= z0) continue;
            c.FillBand(centre, tunnel.RadiusAt(z1), tunnel.RadiusAt(z0), TunnelLane.Squash, Fade(Of(Palette.Pause), 0.16f));
        }
        foreach (var change in Changes())
        {
            var z = (float)Ahead(change.Start);
            var r = tunnel.RadiusAt(z);
            c.DrawDashedEllipse(centre, r, r * TunnelLane.Squash, Fade(Of(Palette.Cool), 0.85f * (1 - z)), 3, 24, (float)(_now * 0.8));
        }
        var frame = tunnel.Frame * (1 + 0.03f * _hit);
        c.DrawEllipse(centre, frame, frame * TunnelLane.Squash, Fade(rail, (0.16f + 0.3f * _hit) * _power), 16, 96);
        c.DrawEllipse(centre, frame, frame * TunnelLane.Squash, Fade(rail, 0.95f * _power), 3.5f, 96);
        var coming = new List<(ScheduledNote Note, long Ordinal)>(Coming());
        for (var i = coming.Count - 1; i >= 0; i--)
        {
            var (note, ordinal) = coming[i];
            var (at, r, alpha) = NoteAt(note, ordinal);
            var color = NoteColor(note.Weight);
            var back = V(tunnel.At(Math.Min(1, Ahead(note.At) + 0.06), note.At, _now));
            c.DrawPolylineColors([back, at], [Fade(color, 0), Fade(color, 0.55f * alpha)], r * 0.9f, true);
            NoteGlow(c, at, r, color, alpha);
        }
    }

    // --- shared pieces ---

    private static void NoteGlow(PaintLayer c, Vector2 at, float r, Color color, float alpha)
    {
        Glow.Draw(c, at, r * 3.3f, Fade(color, 0.95f * alpha));
        c.DrawCircle(at, r * 0.42f, Fade(Colors.White, 0.9f * alpha), true, -1, true);
    }

    // The seconds left of a running pause, faded in and out at its ends.
    private void Countdown(PaintLayer c, Vector2 at, bool centred)
    {
        if (RunningPause is not { } pause) return;
        var left = Math.Ceiling(pause.End - _now).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var text = centred ? left : $"PAUSE  {left}";
        c.DrawCentred(UiTheme.Bold, at, text, 26, Fade(Colors.White, 0.9f * Fading(pause)));
    }

    private float Fading(ForecastSpan pause) =>
        (float)Math.Clamp(Math.Min((_now - pause.Start) * 3, (pause.End - _now) * 3), 0, 1);

    private static Color Of(string hex) => UiTheme.Of(hex);

    private static Color Fade(Color color, float alpha) => new(color.R, color.G, color.B, Math.Clamp(alpha, 0, 1));

    private static Color Shade(float alpha) => new(0.04f, 0.016f, 0.07f, Math.Clamp(alpha, 0, 1));

    private static Vector2 V(TrackPoint p) => new(p.X, p.Y);

    private static TrackPoint P(Vector2 v) => new(v.X, v.Y);
}
