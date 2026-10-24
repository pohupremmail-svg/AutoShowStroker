using System;
using Gooner.Core.Panels;
using Godot;

namespace Gooner.Engine.Ui.Controls;

// A slider's value beside it, with its unit - and a field to type it into once clicked (the user's
// call, 2026-10-24: sliders, with a way to type the value). Enter or leaving the field takes what was
// typed, held to the slider; Esc keeps what it was. What is not a value is not taken.
public partial class ValueBadge : Control
{
    private readonly SliderSpec _spec;
    private readonly bool _range;
    private readonly Button _shown;
    private readonly LineEdit _field;
    private double _low, _high;

    public ValueBadge(SliderSpec spec, bool range)
    {
        _spec = spec;
        _range = range;
        // One width for single values and ranges alike, so the sliders above each other line up.
        CustomMinimumSize = new Vector2(150, 32);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _shown = new Button { ThemeTypeVariation = "Badge", FocusMode = FocusModeEnum.None, Alignment = HorizontalAlignment.Right, MouseDefaultCursorShape = CursorShape.Ibeam };
        _shown.SetAnchorsPreset(LayoutPreset.FullRect);
        _shown.Pressed += Edit;
        AddChild(_shown);
        _field = new LineEdit { Visible = false, Alignment = HorizontalAlignment.Right, SelectAllOnFocus = true };
        _field.SetAnchorsPreset(LayoutPreset.FullRect);
        _field.TextSubmitted += _ => Take();
        _field.FocusExited += Take;
        _field.GuiInput += @event =>
        {
            if (@event is not InputEventKey { Pressed: true, Keycode: Key.Escape }) return;
            _field.AcceptEvent();
            Leave();
        };
        AddChild(_field);
    }

    public bool Enabled
    {
        get => !_shown.Disabled;
        set => _shown.Disabled = !value;
    }

    // A typed value taken: the low end and the high one (the same for a single value).
    public event Action<double, double>? Typed;

    public void Show(double low, double high)
    {
        (_low, _high) = (low, high);
        _shown.Text = _range ? _spec.RangeText(low, high) : _spec.Text(low);
    }

    private void Edit()
    {
        _field.Text = _shown.Text;
        _shown.Visible = false;
        _field.Visible = true;
        _field.GrabFocus();
    }

    private void Take()
    {
        if (!_field.Visible) return;
        if (_range && _spec.ParseRange(_field.Text) is var (low, high)) Typed?.Invoke(low, high);
        else if (!_range && _spec.Parse(_field.Text) is { } value) Typed?.Invoke(value, value);
        Leave();
    }

    private void Leave()
    {
        _field.Visible = false;
        _shown.Visible = true;
        Show(_low, _high);
    }
}
