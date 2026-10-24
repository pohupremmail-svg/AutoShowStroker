using System;
using Gooner.Core.App;
using Godot;

namespace Gooner.Engine.Ui.Controls;

// The little picture on a tile (the redesign, 2026-10-24): a note track playing in miniature, a colour
// scheme's colours under its climax word, a telepathy frame's shape. Drawn small and by hand - the
// Look tab's live preview beside the tiles is the real track.
public partial class Glyph : Control
{
    private static readonly System.Collections.Generic.Dictionary<string, SystemFont> Fonts = [];
    private readonly string _setting, _option;
    private double _time;

    public Glyph(string setting, string option)
    {
        _setting = setting;
        _option = option;
        CustomMinimumSize = new Vector2(0, 58);
        ClipContents = true;
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;
        _time += delta;
        // A track plays; the rest is drawn again for a moment, until its font has loaded.
        if (_setting == "note_track" || _time < 1.5) QueueRedraw();
    }

    public override void _Draw()
    {
        DrawStyleBox(UiTheme.Box(new Color(0, 0, 0, 0.35f), 9), new Rect2(Vector2.Zero, Size));
        switch (_setting)
        {
            case "note_track":
                Track();
                break;
            case "color_scheme":
                Scheme();
                break;
            case "telepathy_frame":
                Frame();
                break;
        }
    }

    // Notes every half second, `ahead` 1 at the far end and 0 on the beat.
    private void Notes(Action<float> each)
    {
        for (var k = Math.Floor(_time / 0.5) + 1; k * 0.5 < _time + 1.6; k++)
            each((float)((k * 0.5 - _time) / 1.6));
    }

    private void Track()
    {
        var (w, h) = (Size.X, Size.Y);
        var accent = UiTheme.Of(Palette.Accent);
        var flash = (float)Math.Max(0, 1 - _time % 0.5 / 0.2);
        void Note(Vector2 at, float r)
        {
            Fx.Glow.Draw(this, at, r * 3, new Color(accent, 0.5f));
            DrawCircle(at, r, Colors.White, antialiased: true);
        }
        switch (_option)
        {
            case "highway":
                var top = new Vector2(w / 2, h * 0.12f);
                var hit = new Vector2(w / 2, h * 0.8f);
                DrawColoredPolygon([top + new Vector2(-4, 0), top + new Vector2(4, 0), new Vector2(w / 2 + w * 0.3f, h), new Vector2(w / 2 - w * 0.3f, h)], new Color(0, 0, 0, 0.45f));
                DrawLine(top + new Vector2(-4, 0), new Vector2(w / 2 - w * 0.3f, h), accent, 1.5f, true);
                DrawLine(top + new Vector2(4, 0), new Vector2(w / 2 + w * 0.3f, h), accent, 1.5f, true);
                Fx.Glow.Draw(this, hit, w * 0.25f, new Color(accent, 0.2f + 0.4f * flash), 0.3f);
                Notes(a => { var p = 1 - a; Note(top.Lerp(hit, p * p), 1.5f + 4 * p * p); });
                break;
            case "ring":
                var centre = new Vector2(w / 2, h / 2);
                var radius = h * 0.18f;
                Fx.Glow.Draw(this, centre, radius * 2.2f, new Color(accent, 0.2f + 0.4f * flash));
                DrawArc(centre, radius, 0, Mathf.Tau, 40, accent, 2, true);
                Notes(a =>
                {
                    foreach (var arm in new[] { 0f, MathF.PI })
                    {
                        var angle = arm + a * 4 + (float)_time * 0.4f;
                        Note(centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle) * 0.9f) * (radius + a * h * 0.6f), 2.5f);
                    }
                });
                break;
            case "stream":
                var y = h * 0.6f;
                var hitX = w * 0.16f;
                DrawRect(new Rect2(0, y - h * 0.17f, w, h * 0.34f), new Color(0, 0, 0, 0.4f));
                DrawLine(new Vector2(hitX, y - h * 0.17f), new Vector2(hitX, y + h * 0.17f), accent, 2, true);
                Fx.Glow.Draw(this, new Vector2(hitX, y), h * 0.4f, new Color(accent, 0.2f + 0.4f * flash));
                Notes(a => Note(new Vector2(hitX + a * (w - hitX), y), 3.5f));
                break;
            default:
                var middle = new Vector2(w / 2, h / 2);
                for (var i = 0; i < 6; i++)
                {
                    var d = (float)((i / 6.0 + _time * 0.3) % 1);
                    DrawArc(middle, 2 + d * d * w * 0.6f, 0, Mathf.Tau, 40, new Color(accent, 0.1f + 0.45f * d), 1 + d, true);
                }
                Notes(a => { var p = 1 - a; var angle = a * 6 + 1; Note(middle + new Vector2(MathF.Cos(angle), MathF.Sin(angle) * 0.9f) * (2 + p * p * w * 0.45f), 1 + 4 * p); });
                break;
        }
    }

    // The scheme's colours in stripes, its climax word on top in its own face.
    private void Scheme()
    {
        if (!Palette.Schemes.TryGetValue(_option, out var scheme)) return;
        string[] colours = [scheme.SurfaceDarkest, scheme.Surface, scheme.Secondary, scheme.Accent, scheme.Cool, scheme.Text];
        var stripe = Size.X / colours.Length;
        for (var i = 0; i < colours.Length; i++)
            DrawRect(new Rect2(i * stripe, 0, stripe + 1, Size.Y), UiTheme.Of(colours[i]));
        if (!Fonts.TryGetValue(_option, out var font))
            Fonts[_option] = font = new SystemFont { FontNames = [.. scheme.DisplayFonts], FontItalic = scheme.DisplayItalic, FontWeight = scheme.DisplayItalic ? 600 : 800 };
        var size = (int)(Size.Y * 0.42f);
        var width = font.GetStringSize("CUM", HorizontalAlignment.Left, -1, size).X;
        var at = new Vector2((Size.X - width) / 2, Size.Y / 2 + size * 0.35f);
        DrawString(font, at + new Vector2(0, 2), "CUM", HorizontalAlignment.Left, -1, size, new Color(0, 0, 0, 0.5f));
        DrawString(font, at, "CUM", HorizontalAlignment.Left, -1, size, Colors.White);
    }

    // The frame round a figure's bust: a neon oval, or an old tube screen.
    private void Frame()
    {
        var accent = UiTheme.Of(Palette.Accent);
        var centre = Size / 2;
        var h = Size.Y * 0.8f;
        if (_option == "crt")
        {
            var rect = new Rect2(centre - new Vector2(h * 0.5f, h * 0.5f), new Vector2(h, h));
            var box = UiTheme.Box(new Color(0, 0, 0, 0.2f), 10);
            box.SetBorderWidthAll(3);
            box.BorderColor = new Color(0.62f, 0.62f, 0.62f);
            DrawStyleBox(box, rect);
            for (var y = rect.Position.Y + 4; y < rect.End.Y - 4; y += 3)
                DrawLine(new Vector2(rect.Position.X + 4, y), new Vector2(rect.End.X - 4, y), new Color(1, 1, 1, 0.05f));
        }
        else
        {
            Fx.Glow.Draw(this, centre, h * 0.7f, new Color(accent, 0.25f));
            var points = new Vector2[41];
            for (var i = 0; i <= 40; i++)
            {
                var angle = i / 40f * Mathf.Tau;
                points[i] = centre + new Vector2(MathF.Cos(angle) * h * 0.36f, MathF.Sin(angle) * h * 0.5f);
            }
            DrawPolyline(points, accent, 2.5f, true);
        }
        // A bust inside: a head and shoulders.
        var skin = new Color(0.93f, 0.6f, 0.62f);
        DrawCircle(centre + new Vector2(0, -h * 0.12f), h * 0.12f, skin, antialiased: true);
        DrawRect(new Rect2(centre.X - h * 0.14f, centre.Y + h * 0.02f, h * 0.28f, h * 0.3f), skin);
    }
}
