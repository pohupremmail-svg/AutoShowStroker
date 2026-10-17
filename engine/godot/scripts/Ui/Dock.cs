using System;
using Godot;
using Gooner.Core.Localization;

namespace Gooner.Engine.Ui;

// The session's controls, on a pane of glass floating over the picture instead of a row under it:
// Previous, Stop, Skip, the edge, sound, the device, the folders, Settings and the menu. It fades
// almost away while the pointer rests, and comes back the moment it moves. Picked up by its grip it
// goes wherever it is put - or thrown, drifting on and bouncing off the edges.
//
// Every button is NoFocus, as the old row was: a focused button takes Space for itself, and Space
// is Panic.
public partial class Dock : PanelContainer
{
    private const double IdleSec = 2.6;
    private const float IdleAlpha = 0.14f;

    private Vector2? _grab;
    private Vector2 _velocity;
    private bool _drifting, _placed, _muted;
    private double _idle;

    public Dock()
    {
        Glass.Apply(this, 20, 7, opacity: Glass.DockOpacity);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 2);
        AddChild(row);
        var grip = new TextureRect
        {
            Texture = Icons.Get("grip", 20),
            StretchMode = TextureRect.StretchModeEnum.KeepCentered,
            CustomMinimumSize = new Vector2(22, 44),
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.Move,
            TooltipText = L10n.T("Drag the controls anywhere - or throw them"),
            Modulate = new Color(1, 1, 1, 0.6f),
        };
        grip.GuiInput += Drag;
        row.AddChild(grip);

        Previous = Icon(row, "previous", L10n.T("Previous (Left Arrow)"));
        Stop = Icon(row, "stop", L10n.T("Stop (Ctrl+Space)"));
        Next = Icon(row, "next", L10n.T("Skip (Right Arrow)"));
        Separator(row);
        Edge = new Button { FocusMode = FocusModeEnum.None, ThemeTypeVariation = "EdgeButton", TooltipText = L10n.T("E - a pause now, and a gentler beat behind it") };
        row.AddChild(Edge);
        Separator(row);
        Mute = Icon(row, "sound", L10n.T("Sound (M)"));
        Device = new Button { FocusMode = FocusModeEnum.None, ThemeTypeVariation = "DockButton", Visible = false };
        row.AddChild(Device);
        Folder = Icon(row, "folder", L10n.T("Change Gooning Folder (Ctrl+O)"));
        Settings = Icon(row, "settings", L10n.T("Settings (Ctrl+S)"));
        Menu = Icon(row, "menu", L10n.T("Menu"));
    }

    public Button Previous { get; }
    public Button Stop { get; }
    public Button Next { get; }
    public Button Edge { get; }
    public Button Mute { get; }
    public Button Device { get; }
    public Button Folder { get; }
    public Button Settings { get; }
    public Button Menu { get; }

    // Whether it may fade while the pointer rests: during a session, with nothing else open.
    public bool MayFade { get; set; }

    // The pointer moved: back in full.
    public void Wake() => _idle = 0;

    public void ShowMuted(bool muted)
    {
        _muted = muted;
        Mute.Icon = Icons.Get(muted ? "muted" : "sound");
    }

    // The icons again, in the colour scheme now in use.
    public void Restyle()
    {
        foreach (var (button, icon) in new[] { (Previous, "previous"), (Stop, "stop"), (Next, "next"), (Folder, "folder"), (Settings, "settings"), (Menu, "menu") })
            button.Icon = Icons.Get(icon);
        ShowMuted(_muted);
    }

    private static Button Icon(HBoxContainer row, string icon, string tooltip)
    {
        var button = new Button
        {
            Icon = Icons.Get(icon),
            FocusMode = FocusModeEnum.None,
            ThemeTypeVariation = "DockButton",
            TooltipText = tooltip,
            CustomMinimumSize = new Vector2(44, 44),
            IconAlignment = HorizontalAlignment.Center,
        };
        row.AddChild(button);
        return button;
    }

    private static void Separator(HBoxContainer row) =>
        row.AddChild(new ColorRect { Color = new Color(1, 1, 1, 0.14f), CustomMinimumSize = new Vector2(1, 28), SizeFlagsVertical = SizeFlags.ShrinkCenter });

    private void Drag(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                if (button.Pressed)
                {
                    _grab = button.GlobalPosition - GlobalPosition;
                    _velocity = Vector2.Zero;
                    _drifting = false;
                }
                else if (_grab is not null)
                {
                    _grab = null;
                    _drifting = true;
                }
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _grab is { } grab:
                var target = motion.GlobalPosition - grab;
                _velocity = _velocity * 0.5f + (target - GlobalPosition) * 0.5f;
                GlobalPosition = target;
                _idle = 0;
                AcceptEvent();
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (GetParent() is not Control room || Size == Vector2.Zero) return;
        // Bottom centre until it is moved, and back inside whenever the window shrinks.
        if (!_placed)
        {
            Position = new Vector2((room.Size.X - Size.X) / 2, room.Size.Y - Size.Y - 18).Round();
            if (room.Size.X > 0) _placed = true;
        }
        PivotOffset = Size / 2;

        _idle += delta;
        var hovered = GetGlobalRect().HasPoint(GetGlobalMousePosition());
        var alpha = MayFade && !hovered && _grab is null && _idle > IdleSec ? IdleAlpha : 1f;
        Modulate = new Color(1, 1, 1, Mathf.Lerp(Modulate.A, alpha, (float)Math.Min(1, delta * (alpha < Modulate.A ? 2.5 : 12))));

        var tilt = _grab is not null || _drifting ? Math.Clamp(_velocity.X * 0.004f, -0.08f, 0.08f) : 0f;
        Rotation = Mathf.Lerp(Rotation, tilt, 0.25f);
        if (_drifting)
        {
            Position += _velocity;
            _velocity *= 0.9f;
            if (_velocity.Length() < 0.3f) _drifting = false;
        }
        var max = room.Size - Size;
        if (Position.X < 0 || Position.X > max.X) _velocity.X = -_velocity.X * 0.5f;
        if (Position.Y < 0 || Position.Y > max.Y) _velocity.Y = -_velocity.Y * 0.5f;
        Position = new Vector2(Math.Clamp(Position.X, 0, Math.Max(0, max.X)), Math.Clamp(Position.Y, 0, Math.Max(0, max.Y)));
    }
}
