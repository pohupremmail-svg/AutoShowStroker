using System;
using System.Collections.Generic;
using Godot;

namespace Gooner.Engine.Ui.Controls;

// A few options side by side in a dark well, the one picked lit (the redesign, 2026-10-24) - in place
// of a row of separate buttons.
public partial class Segments : PanelContainer
{
    private readonly List<Button> _buttons = [];
    private readonly List<string> _values;

    public Segments(IReadOnlyList<(string Value, string Label)> options, string value, Action<string> picked)
    {
        ThemeTypeVariation = "SegmentBar";
        SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 2);
        AddChild(row);
        var group = new ButtonGroup();
        var values = new List<string>();
        foreach (var (option, label) in options)
        {
            var button = new Button { Text = label, ToggleMode = true, ButtonGroup = group, ThemeTypeVariation = "Segment", FocusMode = FocusModeEnum.None };
            button.SetPressedNoSignal(option == value);
            button.Pressed += () => picked(option);
            row.AddChild(button);
            _buttons.Add(button);
            values.Add(option);
        }
        _values = values;
    }

    // After a reset: the picked one lit, without telling anyone.
    public void Show(string value)
    {
        for (var i = 0; i < _buttons.Count; i++) _buttons[i].SetPressedNoSignal(_values[i] == value);
    }

    public bool Enabled
    {
        set
        {
            foreach (var button in _buttons) button.Disabled = !value;
        }
    }
}
