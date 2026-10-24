using System;
using Gooner.Core.App;
using Gooner.Core.Randomness;
using Gooner.Core.Track;
using Gooner.Engine.Fx;
using Gooner.Engine.Track;
using Godot;

namespace Gooner.Engine.Ui.Controls;

// The Look tab's live preview (the user's pick of the redesign mock, 2026-10-24): the real note track
// and stage filter of a session, in a small world of their own, playing the choices as they are being
// made - track, Hypno Ring and what it does around it, effect strength - over a stand-in picture,
// on a steady beat of its own (PreviewBeat). The colours are the ones in use until saved: the scheme is
// the whole window's.
public partial class LookPreview : SubViewportContainer
{
    private readonly SubViewport _viewport = new() { TransparentBg = false, Msaa2D = Viewport.Msaa.Msaa2X };
    private readonly Look _look = new(null);
    private readonly NoteTrackView _track;
    private readonly StageFilter _filter = new();
    private readonly Func<(string Track, string Censor, string Effects, bool Wanders)> _draft;
    private double _time;

    public LookPreview(Func<(string Track, string Censor, string Effects, bool Wanders)> draft)
    {
        _draft = draft;
        Stretch = true;
        CustomMinimumSize = new Vector2(0, 200);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_viewport);
        var picture = new TextureRect { Texture = StandIn(), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered };
        picture.SetAnchorsPreset(LayoutPreset.FullRect);
        var stage = new Control();
        stage.SetAnchorsPreset(LayoutPreset.FullRect);
        _viewport.AddChild(stage);
        stage.AddChild(picture);
        stage.AddChild(_filter);
        var particles = new Particles();
        _track = new NoteTrackView(_look, particles, new SystemRandomSource());
        stage.AddChild(_track);
        stage.AddChild(particles);
        // A screen's shape at any width.
        Resized += () => CustomMinimumSize = new Vector2(0, Mathf.Round(Size.X * 0.62f));
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;
        _time += delta;
        var (track, censor, effects, wanders) = _draft();
        _look.NoteTrack = track;
        _look.Censor = censor;
        _look.Effects = effects;
        _look.FocusWanders = wanders;
        _track.Show(PreviewBeat.Forecast(_time, NoteTrackView.LeadTimeSec), _time, delta);
        _filter.Apply(_track.ShowsRing ? censor : null, _track.Focus, _track.FocusRadius * 2.5f * (1 + 0.07f * _track.Flare), 0);
    }

    // Soft neon blobs with some lines and dots on them, so blur and mosaic show - nothing of anyone's.
    private static ImageTexture StandIn()
    {
        const int w = 480, h = 270;
        var image = Image.CreateEmpty(w, h, false, Image.Format.Rgb8);
        (float X, float Y, float R, Color C)[] blobs =
        [
            (0.25f, 0.68f, 0.42f, new Color(0.35f, 1f, 1f)),
            (0.72f, 0.7f, 0.45f, new Color(1f, 0.27f, 0.73f)),
            (0.6f, 0.25f, 0.35f, new Color(0.6f, 0.2f, 1f)),
            (0.12f, 0.2f, 0.28f, new Color(1f, 0.8f, 0.4f)),
        ];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var colour = new Color(0.1f, 0.05f, 0.14f);
                foreach (var (bx, by, r, c) in blobs)
                {
                    var d = new Vector2(x / (float)w - bx, (y / (float)h - by) * h / w).Length() / r;
                    colour += c * 0.8f * MathF.Exp(-d * d * 2.2f);
                }
                if ((x + y) % 22 < 2) colour += new Color(0.12f, 0.12f, 0.12f);
                if (x % 30 is >= 14 and <= 16 && y % 30 is >= 14 and <= 16) colour += new Color(0.3f, 0.3f, 0.3f);
                image.SetPixel(x, y, new Color(Math.Min(1, colour.R), Math.Min(1, colour.G), Math.Min(1, colour.B)));
            }
        }
        return ImageTexture.CreateFromImage(image);
    }
}
