using System;
using System.Collections.Generic;
using Gooner.Core.App;
using Gooner.Core.Characters;
using Godot;

namespace Gooner.Engine.Ui.Controls;

// Options shown as what they are (the redesign, 2026-10-24): a tile per option with a picture of it
// above its name - a note track playing, a colour scheme's colours, a frame's shape. The picked one is
// lit.
public partial class Tiles : GridContainer
{
    private readonly List<BaseButton> _buttons = [];
    private readonly List<string> _values = [];

    public Tiles(string setting, IReadOnlyList<(string Value, string Label)> options, string value, Action<string> picked)
    {
        Columns = Math.Min(4, options.Count);
        AddThemeConstantOverride("h_separation", 10);
        AddThemeConstantOverride("v_separation", 10);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        Picks.Fill(this, _buttons, _values, options, value, picked, (option, label) => Tile(setting, option, label));
    }

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

    private static Button Tile(string setting, string option, string label)
    {
        var tile = new Button { ThemeTypeVariation = "Tile", SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 104) };
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.SetAnchorsPreset(LayoutPreset.FullRect);
        column.OffsetLeft = column.OffsetTop = 8;
        column.OffsetRight = column.OffsetBottom = -8;
        column.AddThemeConstantOverride("separation", 6);
        column.AddChild(new Glyph(setting, option) { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        var name = new Label { Text = label, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore, ClipText = true };
        name.AddThemeFontOverride("font", UiTheme.Bold);
        name.AddThemeFontSizeOverride("font_size", 13);
        column.AddChild(name);
        tile.AddChild(column);
        return tile;
    }
}

// The toy's colours as round swatches, the skin tones first and the fantasy ones a little apart.
public partial class SwatchRow : HFlowContainer
{
    private readonly List<BaseButton> _buttons = [];
    private readonly List<string> _values = [];

    public SwatchRow(IReadOnlyList<(string Value, string Label)> options, string value, Action<string> picked)
    {
        AddThemeConstantOverride("h_separation", 8);
        AddThemeConstantOverride("v_separation", 8);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        Picks.Fill(this, _buttons, _values, options, value, picked, (option, label) =>
        {
            if (option == ToyColours.Fantasy[0]) AddChild(new Control { CustomMinimumSize = new Vector2(8, 0) });
            return new Swatch(Swatch.Of(option)) { TooltipText = label };
        });
    }

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

internal static class Picks
{
    // One button per option in a group, the picked one pressed.
    public static void Fill(Control into, List<BaseButton> buttons, List<string> values, IReadOnlyList<(string Value, string Label)> options,
        string value, Action<string> picked, Func<string, string, BaseButton> make)
    {
        var group = new ButtonGroup();
        foreach (var (option, label) in options)
        {
            var button = make(option, label);
            button.ToggleMode = true;
            button.ButtonGroup = group;
            button.FocusMode = Control.FocusModeEnum.None;
            button.SetPressedNoSignal(option == value);
            button.Pressed += () => picked(option);
            into.AddChild(button);
            buttons.Add(button);
            values.Add(option);
        }
    }
}

// A round colour to pick, rimmed in the accent when picked.
public partial class Swatch : Button
{
    private readonly Color _colour;

    public Swatch(Color colour)
    {
        _colour = colour;
        CustomMinimumSize = new Vector2(34, 34);
        Flat = true;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
            AddThemeStyleboxOverride(state, new StyleBoxEmpty());
    }

    // The toy's own colour, as ToyTint mixes it for the shaft.
    public static Color Of(string toyColour)
    {
        var body = ToyColours.Of(toyColour).Body;
        return new Color(body.X, body.Y, body.Z);
    }

    public override void _Draw()
    {
        var centre = Size / 2;
        if (ButtonPressed)
        {
            var accent = UiTheme.Of(Palette.Accent);
            Fx.Glow.Draw(this, centre, 30, new Color(accent, 0.5f));
            DrawCircle(centre, 17, accent, antialiased: true);
            DrawCircle(centre, 15, UiTheme.Of(Palette.SurfaceDark), antialiased: true);
        }
        else if (IsHovered())
        {
            DrawCircle(centre, 17, new Color(1, 1, 1, 0.3f), antialiased: true);
        }
        DrawCircle(centre, ButtonPressed ? 13 : 15, _colour, antialiased: true);
    }
}
