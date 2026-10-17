using System;
using System.Collections.Generic;
using Gooner.Core.App;
using Gooner.Core.Localization;
using Gooner.Core.Panels;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine.Panels;

// PatternEditorDialog: the user's own patterns on the left, the step sequencer on the right - a
// bar to drag per step for its length, a button for Beat or Pause, and a preview that plays it.
public partial class PatternEditorPanel : AppPanel
{
    private readonly PatternEditor _editor;
    private readonly Action _click;
    private readonly ItemList _list;
    private readonly LineEdit _name;
    private readonly HBoxContainer _stepsRow;
    private readonly Label _error;
    private readonly Button _preview;
    private readonly Godot.Timer _timer;
    private readonly List<StepBar> _bars = [];

    public PatternEditorPanel(PatternEditor editor, Action click)
        : base(L10n.T("Manage Custom Patterns"), new Vector2(640, 480))
    {
        _editor = editor;
        _click = click;
        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 12);
        Body.AddChild(columns);

        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1 };
        left.AddChild(new Label { Text = L10n.T("Your Patterns") });
        _list = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill };
        _list.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Of(Palette.SurfaceDark), 6, 6));
        _list.ItemSelected += index =>
        {
            _editor.Select(_list.GetItemText((int)index));
            Pull();
        };
        left.AddChild(_list);
        var listButtons = new HBoxContainer();
        listButtons.AddChild(Button(L10n.T("New"), () =>
        {
            _editor.StartNew();
            Pull();
        }));
        listButtons.AddChild(Button(L10n.T("Delete"), () =>
        {
            _editor.DeleteSelected();
            Pull();
        }));
        left.AddChild(listButtons);
        columns.AddChild(left);

        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 2 };
        var nameRow = new HBoxContainer();
        nameRow.AddChild(new Label { Text = L10n.T("Name:") });
        _name = new LineEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _name.TextChanged += text => _editor.Name = text;
        nameRow.AddChild(_name);
        right.AddChild(nameRow);
        _stepsRow = new HBoxContainer();
        _stepsRow.AddThemeConstantOverride("separation", 6);
        var stepsScroll = new ScrollContainer { VerticalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 150) };
        stepsScroll.AddChild(_stepsRow);
        right.AddChild(stepsScroll);
        var stepButtons = new HBoxContainer();
        stepButtons.AddChild(Button(L10n.T("+ Step"), () =>
        {
            _editor.AddStep();
            PullSteps();
        }));
        stepButtons.AddChild(Button(L10n.T("- Step"), () =>
        {
            _editor.RemoveStep();
            PullSteps();
        }));
        right.AddChild(stepButtons);
        _error = Widgets.Text("", 13, Palette.Denied, wrap: true);
        right.AddChild(_error);
        var actions = new HBoxContainer();
        _preview = Button(L10n.T("Preview"), TogglePreview);
        actions.AddChild(_preview);
        var save = Button(L10n.T("Save"), () =>
        {
            _editor.Save();
            Pull();
        });
        save.ThemeTypeVariation = "PrimaryButton";
        actions.AddChild(save);
        right.AddChild(actions);
        columns.AddChild(right);

        _timer = new Godot.Timer { OneShot = true };
        _timer.Timeout += Tick;
        AddChild(_timer);
        // However it closes - Esc, the cross, Close - a preview left running would go on clicking.
        Closed += StopPreview;
        AddButton(L10n.T("Close"), Close);
        Pull();
    }

    private static Button Button(string text, Action pressed)
    {
        var button = new Button { Text = text, FocusMode = FocusModeEnum.None };
        button.Pressed += pressed;
        return button;
    }

    // The editor's state onto the widgets: the list, the name, the steps, the error.
    private void Pull()
    {
        if (!_editor.Previewing) StopPreview();
        _list.Clear();
        foreach (var name in _editor.PatternNames)
        {
            var index = _list.AddItem(name);
            if (name == _editor.Selected) _list.Select(index);
        }
        if (_name.Text != _editor.Name) _name.Text = _editor.Name;
        _error.Text = _editor.Error;
        _error.Visible = _error.Text.Length > 0;
        PullSteps();
    }

    private void PullSteps()
    {
        foreach (var child in _stepsRow.GetChildren()) child.QueueFree();
        _bars.Clear();
        foreach (var step in _editor.Steps)
        {
            var column = new VBoxContainer();
            column.AddThemeConstantOverride("separation", 4);
            var bar = new StepBar(step);
            _bars.Add(bar);
            column.AddChild(bar);
            var toggle = new Button { Text = step.ButtonText, ToggleMode = true, ButtonPressed = step.Muted, FocusMode = FocusModeEnum.None };
            toggle.AddThemeStyleboxOverride("pressed", UiTheme.Box(UiTheme.Of(Palette.Pause), 8, 6));
            toggle.Toggled += on =>
            {
                step.Muted = on;
                toggle.Text = step.ButtonText;
                bar.QueueRedraw();
            };
            column.AddChild(toggle);
            _stepsRow.AddChild(column);
        }
    }

    private void TogglePreview()
    {
        if (_editor.Previewing)
        {
            StopPreview();
            return;
        }
        _editor.StartPreview();
        _preview.Text = L10n.T("Stop");
        Tick();
    }

    private void StopPreview()
    {
        _editor.StopPreview();
        _timer.Stop();
        _preview.Text = L10n.T("Preview");
        foreach (var bar in _bars) bar.Lit = false;
    }

    private void Tick()
    {
        if (!_editor.Previewing || _editor.Steps.Count == 0)
        {
            StopPreview();
            return;
        }
        var tick = _editor.PreviewTick();
        for (var i = 0; i < _bars.Count; i++) _bars[i].Lit = i == tick.Step;
        if (tick.Click) _click();
        _timer.Start(tick.NextInMs / 1000.0);
    }

    // One step's bar: drag or click it for the step's length, the top for the longest. Purely a
    // length control - it never touches the Beat/Pause state.
    private sealed partial class StepBar : Control
    {
        private readonly PatternStep _step;
        private bool _lit;

        public StepBar(PatternStep step)
        {
            _step = step;
            CustomMinimumSize = new Vector2(64, 100);
            MouseDefaultCursorShape = CursorShape.PointingHand;
            TooltipText = step.Tooltip;
        }

        public bool Lit
        {
            get => _lit;
            set
            {
                _lit = value;
                QueueRedraw();
            }
        }

        public override void _GuiInput(InputEvent @event)
        {
            var y = @event switch
            {
                InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } press => press.Position.Y,
                InputEventMouseMotion motion when (motion.ButtonMask & MouseButtonMask.Left) != 0 => motion.Position.Y,
                _ => float.NaN,
            };
            if (float.IsNaN(y)) return;
            _step.Weight = PatternStep.WeightAt(y, Size.Y);
            TooltipText = _step.Tooltip;
            QueueRedraw();
            AcceptEvent();
        }

        public override void _Draw()
        {
            var track = new Rect2(2, 2, Size.X - 4, Size.Y - 4);
            DrawStyleBox(UiTheme.Box(UiTheme.Of(Palette.SurfaceDark), 6), track);
            var minimum = track.Size.Y * 0.15f;
            var height = minimum + (float)_step.BarRatio * (track.Size.Y - minimum);
            var bar = new Rect2(track.Position.X, track.End.Y - height, track.Size.X, height);
            DrawStyleBox(UiTheme.Box(UiTheme.Of(_step.Muted ? Palette.Pause : Palette.Accent), 6), bar);
            if (_lit)
            {
                var outline = new StyleBoxFlat { DrawCenter = false, BorderColor = UiTheme.Of(Palette.Text) };
                outline.SetBorderWidthAll(2);
                outline.SetCornerRadiusAll(6);
                DrawStyleBox(outline, track);
            }
        }
    }
}
