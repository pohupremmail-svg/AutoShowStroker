using System;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine.Panels;

// What a QDialog was on the Python side, as a panel on the window itself: a title, a body, and a
// row of buttons under it. It is shown and taken down by the PanelHost; nothing here waits for it,
// which is what the nested event loop of QDialog.exec() used to do.
//
// Rejecting it - Esc, the cross in its corner - runs OnReject, which closes it unless a panel
// says otherwise. That is QMessageBox's escape button: the Stop question answers "Rather not say".
//
// It is frosted glass, so the session behind shows through, and it can be picked up by its title
// and moved - or thrown: it tilts with the pull, and drifts on and bounces off the window's edges
// when let go.
public partial class AppPanel : PanelContainer
{
    private readonly Vector2 _wanted;
    private Vector2? _grab;
    private Vector2 _velocity;
    private bool _drifting;

    public AppPanel(string title, Vector2 size, bool scrolls = false)
    {
        _wanted = size;
        CustomMinimumSize = size;
        Glass.Apply(this, 22, 18, opacity: Glass.PanelOpacity);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        AddChild(column);

        var header = new HBoxContainer();
        var heading = new Label { Text = title, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        heading.ThemeTypeVariation = "PanelTitle";
        header.AddChild(heading);
        var cross = new Button { Text = "✕", Flat = true, FocusMode = FocusModeEnum.None, TooltipText = "Close (Esc)" };
        cross.Pressed += Reject;
        header.AddChild(cross);
        header.MouseFilter = MouseFilterEnum.Stop;
        header.MouseDefaultCursorShape = CursorShape.Move;
        header.GuiInput += Drag;
        column.AddChild(header);

        Body = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        Body.AddThemeConstantOverride("separation", 10);
        // A panel of a set height never outgrows a small window: its body scrolls instead. One that
        // sizes itself to its text does not need to.
        if (scrolls || size.Y > 0)
        {
            var scroll = new ScrollContainer
            {
                SizeFlagsVertical = SizeFlags.ExpandFill,
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            };
            scroll.AddChild(Body);
            column.AddChild(scroll);
        }
        else
        {
            column.AddChild(Body);
        }

        Buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        Buttons.AddThemeConstantOverride("separation", 8);
        column.AddChild(Buttons);
    }

    public VBoxContainer Body { get; }

    public HBoxContainer Buttons { get; }

    // What Esc and the cross do. Closing, unless a panel has an answer of its own for it.
    public Action? OnReject { get; set; }

    public event Action? Closed;

    internal PanelHost? Host { get; set; }

    public bool IsOpen => Host is not null;

    public void Close() => Host?.Close(this);

    public void Reject()
    {
        if (OnReject is { } reject) reject();
        else Close();
    }

    public Button AddButton(string text, Action pressed, bool primary = false)
    {
        var button = new Button { Text = text };
        if (primary) button.ThemeTypeVariation = "PrimaryButton";
        button.Pressed += pressed;
        Buttons.AddChild(button);
        return button;
    }

    public Label AddText(string text, int size = 15, bool wrap = false)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        if (wrap) label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Body.AddChild(label);
        return label;
    }

    // Whether it has been moved by hand: until then the host keeps it centred.
    internal bool Moved { get; private set; }

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
                    Moved = true;
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
                AcceptEvent();
                break;
        }
    }

    public override void _Process(double delta)
    {
        // Tilting with the pull, and back to level.
        var tilt = _grab is not null || _drifting ? Math.Clamp(_velocity.X * 0.004f, -0.08f, 0.08f) : 0f;
        Rotation = Mathf.Lerp(Rotation, tilt, 0.25f);
        if (!_drifting || GetParent() is not Control room) return;
        Position += _velocity;
        _velocity *= 0.9f;
        var max = room.Size - Size;
        if (Position.X < 0 || Position.X > max.X) _velocity.X = -_velocity.X * 0.5f;
        if (Position.Y < 0 || Position.Y > max.Y) _velocity.Y = -_velocity.Y * 0.5f;
        Position = new Vector2(Math.Clamp(Position.X, 0, Math.Max(0, max.X)), Math.Clamp(Position.Y, 0, Math.Max(0, max.Y)));
        if (_velocity.Length() < 0.3f) _drifting = false;
    }

    // A panel never outgrows the window it is drawn on: what it asked for, down to what fits.
    internal void FitInto(Vector2 room)
    {
        CustomMinimumSize = new Vector2(Math.Min(_wanted.X, room.X), Math.Min(_wanted.Y, room.Y));
        PivotOffset = Size / 2;
    }

    internal void RaiseClosed() => Closed?.Invoke();
}
