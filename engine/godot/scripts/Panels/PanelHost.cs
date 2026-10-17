using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Gooner.Engine.Panels;

// The layer every panel is shown on, over the whole window - the start screen included, as a modal
// dialog blocked everything behind it. Panels stack: Settings opens the pattern editor over itself, Saved
// Sessions the explorer, and only the one on top takes input. The session behind goes on playing,
// as it did behind a QDialog.
//
// A panel comes in over a lightly dimmed window - the session is meant to show through its glass -
// rising and settling into place, and leaves the same way. It starts in the middle and stays there
// as the window changes, until it is moved by hand.
public partial class PanelHost : Control
{
    private const float FadeSec = 0.14f;
    private const float Margin = 24;

    private readonly List<(Control Layer, AppPanel Panel)> _open = [];

    public PanelHost()
    {
        AnchorRight = AnchorBottom = 1;
        MouseFilter = MouseFilterEnum.Ignore;
        Resized += () =>
        {
            foreach (var (_, panel) in _open)
            {
                panel.FitInto(Room);
                if (!panel.Moved) Centre(panel);
            }
        };
    }

    public bool AnyOpen => _open.Count > 0;

    public AppPanel? Top => _open.Count > 0 ? _open[^1].Panel : null;

    public IEnumerable<AppPanel> Open => _open.Select(entry => entry.Panel);

    private Vector2 Room => Size - new Vector2(2 * Margin, 2 * Margin);

    public void Show(AppPanel panel)
    {
        if (panel.IsOpen) return;
        var layer = new ColorRect { Color = new Color(0, 0, 0, 0.3f), AnchorRight = 1, AnchorBottom = 1 };
        layer.AddChild(panel);
        AddChild(layer);
        panel.Host = this;
        panel.FitInto(Room);
        panel.Resized += () =>
        {
            panel.PivotOffset = panel.Size / 2;
            if (!panel.Moved) Centre(panel);
        };
        Centre(panel);
        _open.Add((layer, panel));

        layer.Modulate = new Color(1, 1, 1, 0);
        panel.Scale = new Vector2(0.9f, 0.9f);
        panel.Rotation = -0.04f;
        var tween = CreateTween().SetParallel().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
        tween.TweenProperty(layer, "modulate:a", 1f, FadeSec);
        tween.TweenProperty(panel, "scale", Vector2.One, FadeSec * 2.2f);
        tween.TweenProperty(panel, "rotation", 0f, FadeSec * 2.2f);
    }

    private void Centre(AppPanel panel)
    {
        var size = panel.Size == Vector2.Zero ? panel.GetCombinedMinimumSize() : panel.Size;
        panel.Position = ((Size - size) / 2).Round();
    }

    public void Close(AppPanel panel)
    {
        var index = _open.FindIndex(entry => entry.Panel == panel);
        if (index < 0) return;
        var (layer, _) = _open[index];
        _open.RemoveAt(index);
        panel.Host = null;
        // Out of the way at once, so a click meant for what is under it is not caught by a panel
        // that is only still fading.
        layer.MouseFilter = MouseFilterEnum.Ignore;
        var tween = CreateTween().SetParallel().SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Cubic);
        tween.TweenProperty(layer, "modulate:a", 0f, FadeSec);
        tween.TweenProperty(panel, "scale", new Vector2(0.94f, 0.94f), FadeSec);
        tween.Chain().TweenCallback(Callable.From(layer.QueueFree));
        panel.RaiseClosed();
    }

    // Esc: the top panel's own answer to being dismissed. False when there was nothing to dismiss.
    public bool RejectTop()
    {
        if (Top is not { } top) return false;
        top.Reject();
        return true;
    }
}
