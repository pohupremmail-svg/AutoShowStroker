using System;
using System.Collections.Generic;
using Gooner.Core.App;
using Gooner.Core.Beat;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine.Track;

// The note highway of src/BeatTrackWidget.py, drawn the same way: notes flow right to left towards
// a hit zone near the left edge and land on the beat. One thing is different underneath: the notes
// come from BeatHandler.UpcomingNotes, the exact moments the click is mixed against, so a note
// lands on its click rather than within a millisecond or so of it.
//
// Stateless like the original - every frame asks for the coming notes afresh, so nothing can drift
// and a change of rhythm in flight corrects itself on the next frame. The flying, bursting notes
// of the effect layer come in phase 7.
public partial class BeatTrack : Control
{
    // How far ahead the track shows. Fixed, so a faster beat reads as denser notes.
    public const double LeadTimeSec = 2.5;
    private const float HitZoneXRatio = 0.12f;
    private const float HitZoneWidth = 6;
    private const float NoteRadius = 11;
    private const double FlashSec = 0.130;
    private const float TrackInset = 4;
    private const float PauseBarHeightRatio = 1.6f;
    private const float PauseBarAlpha = 85 / 255f;
    private const double ChangeFlashSec = 0.420;
    private const float SweepWidthRatio = 0.18f;

    private readonly BeatHandler _beat;
    private readonly Func<double> _now;
    private string _kind = "idle";
    private double _flashUntil = double.NegativeInfinity;
    private double? _changeStartedAt;
    private IReadOnlyList<ScheduledNote> _notes = [];

    public BeatTrack(BeatHandler beat, Func<double> now)
    {
        _beat = beat;
        _now = now;
        _beat.MeterUpdated += kind => _kind = kind;
        _beat.BeatHit += () => _flashUntil = _now() + FlashSec;
        _beat.BeatChanged += (_, _) => _changeStartedAt = _now();
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public string Kind => _kind;

    // The notes as the main loop just predicted them - the same list the click was given.
    public void Show(IReadOnlyList<ScheduledNote> notes)
    {
        _notes = notes;
        QueueRedraw();
    }

    private float HitZoneX => Size.X * HitZoneXRatio;

    private (float Top, float Height) Lane => (TrackInset, Math.Max(1, Size.Y - TrackInset * 2));

    private float CenterY => Lane.Top + Lane.Height / 2;

    // Shrinks rather than overflows when the banner squeezes the footer.
    private float Radius => Math.Min(NoteRadius, Math.Max(3, Lane.Height / 2 - 2));

    private float NoteX(double secondsUntil) => HitZoneX + (float)(secondsUntil / LeadTimeSec) * (Size.X - HitZoneX);

    public override void _Draw()
    {
        var now = _now();
        var track = new Rect2(2, 2, Size.X - 4, Size.Y - 4);
        var background = _kind == "pause" ? Palette.Pause : Palette.SurfaceDark;
        DrawStyleBox(UiTheme.Box(UiTheme.Of(background), 8), track);

        var flashing = now < _flashUntil;
        var (top, height) = Lane;
        DrawStyleBox(UiTheme.Box(UiTheme.Of(flashing ? Palette.Accent : Palette.Secondary), 3),
            new Rect2(HitZoneX - HitZoneWidth / 2, top, HitZoneWidth, height));
        if (flashing)
            DrawArc(new Vector2(HitZoneX, CenterY), Radius + 7, 0, MathF.Tau, 48, UiTheme.Of(Palette.Accent), 2, true);

        DrawPauseBar();

        // Silent steps are not drawn: as ghosts they read like extra beats you were meant to hit.
        // The rests still show, as the gaps they leave.
        if (_kind != "idle")
            foreach (var note in _notes)
                if (note.Audible && note.At - now is var until and >= 0 and <= LeadTimeSec)
                    DrawCircle(new Vector2(NoteX(until), CenterY), Radius, UiTheme.Of(Palette.Accent), true, -1, true);

        DrawChangeSweep(track, now);
    }

    // Spans the track when the pause has just begun and shrinks back towards the hit zone as it
    // runs out, so it travels the way the notes do and empties where they land.
    private void DrawPauseBar()
    {
        var progress = _beat.PauseProgress();
        if (_kind != "pause" || progress <= 0) return;
        var (top, height) = Lane;
        var barHeight = Math.Min(height, Radius * PauseBarHeightRatio);
        var rect = new Rect2(HitZoneX, top + height / 2 - barHeight / 2, Math.Max(1, (Size.X - HitZoneX) * (float)progress), barHeight);
        DrawStyleBox(UiTheme.Box(UiTheme.Of(Palette.Accent, PauseBarAlpha), (int)(barHeight / 2)), rect);
    }

    // A sweep of light across the track when the rhythm changes - an announcement only, since the
    // notes flow straight through the change.
    private void DrawChangeSweep(Rect2 track, double now)
    {
        if (_changeStartedAt is not { } started) return;
        var progress = (float)((now - started) / ChangeFlashSec);
        if (progress >= 1)
        {
            _changeStartedAt = null;
            return;
        }
        var width = track.Size.X * SweepWidthRatio;
        var center = track.Position.X - width + progress * (track.Size.X + width * 2);
        var peak = UiTheme.Of(Palette.Accent, 190 / 255f * (1 - progress));
        var edge = UiTheme.Of(Palette.Accent, 0);
        var (y0, y1) = (track.Position.Y, track.End.Y);
        var left = Math.Max(track.Position.X, center - width);
        var right = Math.Min(track.End.X, center + width);
        // Two quads, each fading from the middle out; clipped to the track.
        if (center > left)
            DrawPolygon([new(left, y0), new(Math.Min(center, right), y0), new(Math.Min(center, right), y1), new(left, y1)],
                [edge, peak, peak, edge]);
        if (right > center)
            DrawPolygon([new(Math.Max(center, left), y0), new(right, y0), new(right, y1), new(Math.Max(center, left), y1)],
                [peak, edge, edge, peak]);
    }
}
