using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Gooner.Core.App;
using Gooner.Core.Localization;
using Gooner.Core.Panels;
using Godot;

namespace Gooner.Engine.Ui;

// The line pyqtgraph drew in the long-term statistics: one point per session, joined, over a
// "Session #" axis and the metric's own, in the accent straight on the panel's glass (the redesign):
// faint grid lines, glowing points. Drawn by hand - a chart library for one line was half of the
// Python app's cold start.
public partial class LineChart : Control
{
    private const float Left = 72, Right = 20, Top = 16, Bottom = 52;
    private const int FontSize = 12;
    private static readonly double[] Steps = [1, 2, 5, 10];

    private IReadOnlyList<ChartPoint> _points = [];
    private string _yLabel = "";

    public LineChart()
    {
        CustomMinimumSize = new Vector2(300, 220);
        SizeFlagsVertical = SizeFlags.ExpandFill;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
    }

    public void Plot(IReadOnlyList<ChartPoint> points, string yLabel)
    {
        _points = points;
        _yLabel = yLabel;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var font = GetThemeDefaultFont();
        var text = UiTheme.Of(Palette.Text, 0.6f);
        var axis = new Color(1, 1, 1, 0.18f);
        var plot = new Rect2(Left, Top, Size.X - Left - Right, Size.Y - Top - Bottom);
        if (plot.Size.X <= 0 || plot.Size.Y <= 0) return;

        var (xMin, xMax) = _points.Count > 0 ? (_points[0].Session, _points[^1].Session) : (0, 1);
        var (yMin, yMax) = Range(_points.Select(p => p.Value));
        var xTicks = Ticks(xMin, xMax == xMin ? xMin + 1 : xMax, integers: true);
        var yTicks = Ticks(yMin, yMax, integers: false);
        double x0 = xTicks[0], x1 = xTicks[^1], y0 = yTicks[0], y1 = yTicks[^1];
        Vector2 At(double x, double y) => new(
            plot.Position.X + (float)((x - x0) / (x1 - x0)) * plot.Size.X,
            plot.End.Y - (float)((y - y0) / (y1 - y0)) * plot.Size.Y);

        foreach (var tick in yTicks)
        {
            var at = At(x0, tick);
            DrawLine(at, new Vector2(plot.End.X, at.Y), new Color(1, 1, 1, 0.06f));
            DrawString(font, new Vector2(4, at.Y + FontSize / 3f), Number(tick), HorizontalAlignment.Right, Left - 12, FontSize, text);
        }
        foreach (var tick in xTicks)
        {
            var at = At(tick, y0);
            DrawLine(at, at + new Vector2(0, 5), axis);
            DrawString(font, at + new Vector2(-30, 20), Number(tick), HorizontalAlignment.Center, 60, FontSize, text);
        }
        DrawLine(new Vector2(plot.Position.X, plot.End.Y), plot.End, axis);
        DrawLine(plot.Position, new Vector2(plot.Position.X, plot.End.Y), axis);
        DrawString(font, new Vector2(plot.Position.X, Size.Y - 8), L10n.T("Session #"), HorizontalAlignment.Center, plot.Size.X, FontSize, text);
        // The y label, turned on its side as pyqtgraph had it.
        DrawSetTransform(new Vector2(14, plot.Position.Y + plot.Size.Y), -Mathf.Pi / 2, Vector2.One);
        DrawString(font, Vector2.Zero, _yLabel, HorizontalAlignment.Center, plot.Size.Y, FontSize, text);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        var accent = UiTheme.Of(Palette.Accent);
        var line = _points.Select(p => At(p.Session, p.Value)).ToArray();
        if (line.Length > 1)
        {
            DrawPolyline(line, new Color(accent, 0.25f), 7, antialiased: true);
            DrawPolyline(line, accent, 2.5f, antialiased: true);
        }
        foreach (var point in line)
        {
            Fx.Glow.Draw(this, point, 18, new Color(accent, 0.45f));
            DrawCircle(point, 4.5f, Colors.White, antialiased: true);
            DrawCircle(point, 3, accent, antialiased: true);
        }
    }

    // The data's own span, padded a little, as an auto-ranged plot shows it.
    private static (double Min, double Max) Range(IEnumerable<double> values)
    {
        var list = values.ToList();
        if (list.Count == 0) return (0, 1);
        double min = list.Min(), max = list.Max();
        if (max - min < 1e-9) return (min - 1, max + 1);
        var pad = (max - min) * 0.05;
        return (min - pad, max + pad);
    }

    // Round steps of 1, 2 or 5 times a power of ten, covering the range.
    private static List<double> Ticks(double min, double max, bool integers)
    {
        var rough = (max - min) / 6;
        var power = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var step = Steps.Select(f => f * power).First(s => s >= rough);
        if (integers) step = Math.Max(1, Math.Round(step));
        var ticks = new List<double>();
        for (var tick = Math.Floor(min / step) * step; tick <= max + step * 0.999; tick += step)
            ticks.Add(Math.Round(tick, 10));
        return ticks;
    }

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
