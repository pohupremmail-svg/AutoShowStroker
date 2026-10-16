using System;
using Godot;

namespace Gooner.Engine.Panels;

// What a QDialog was on the Python side, as a panel on the window itself: a title, a body, and a
// row of buttons under it. It is shown and taken down by the PanelHost; nothing here waits for it,
// which is what the nested event loop of QDialog.exec() used to do.
//
// Rejecting it - Esc, the cross in its corner - runs OnReject, which closes it unless a panel
// says otherwise. That is QMessageBox's escape button: the Stop question answers "Rather not say".
public partial class AppPanel : PanelContainer
{
    private readonly Vector2 _wanted;

    public AppPanel(string title, Vector2 size, bool scrolls = false)
    {
        _wanted = size;
        CustomMinimumSize = size;
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
        column.AddChild(header);

        Body = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        Body.AddThemeConstantOverride("separation", 10);
        if (scrolls)
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

    // A panel never outgrows the window it is drawn on: what it asked for, down to what fits.
    internal void FitInto(Vector2 room)
    {
        CustomMinimumSize = new Vector2(Math.Min(_wanted.X, room.X), Math.Min(_wanted.Y, room.Y));
        PivotOffset = Size / 2;
    }

    internal void RaiseClosed() => Closed?.Invoke();
}
