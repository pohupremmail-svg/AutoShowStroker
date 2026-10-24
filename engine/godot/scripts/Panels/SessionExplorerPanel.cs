using System;
using System.Collections.Generic;
using System.IO;
using Gooner.Core.App;
using Gooner.Core.Localization;
using Gooner.Core.Media;
using Gooner.Core.Panels;
using Gooner.Core.Timing;
using Gooner.Engine.Stage;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine.Panels;

// SessionExplorerDialog: the session drawn end to end along the bottom, scrubbed by moving over it;
// above it whatever was on screen at that moment, and the line saying what was playing.
public partial class SessionExplorerPanel : AppPanel
{
    private const int PreviewMinHeight = 220;

    private readonly SessionExplorer _explorer;
    private readonly PanelHost _host;
    private readonly VideoThumbnailer _thumbnails;
    private readonly Dictionary<string, ImageTexture?> _videoFrames = [];
    private readonly TextureRect _picture;
    private readonly Label _placeholder;
    private readonly Label _moment;
    private readonly Label _file;
    private readonly Button _reveal;
    private readonly Button _play;
    private readonly TimelineBar _bar;
    private Button? _save;

    public SessionExplorerPanel(SessionExplorer explorer, PanelHost host, ClockScheduler scheduler)
        : base(L10n.T("Session Explorer"), new Vector2(940, 640))
    {
        _explorer = explorer;
        _host = host;
        _thumbnails = new VideoThumbnailer(() =>
        {
            var grabber = new FrameGrabber();
            AddChild(grabber);
            return grabber;
        }, scheduler);

        var heading = new HBoxContainer();
        var title = new Label { Text = explorer.Heading, ThemeTypeVariation = "SectionTitle", SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center };
        heading.AddChild(title);
        if (explorer.CanSave)
        {
            _save = new Button
            {
                Text = L10n.T("Save this session"),
                ThemeTypeVariation = "PrimaryButton",
                FocusMode = FocusModeEnum.None,
                TooltipText = L10n.T("Keeps the beats, the climax and the media pacing so this session can be played again."),
            };
            _save.Pressed += Save;
            heading.AddChild(_save);
        }
        Body.AddChild(heading);

        var preview = new PanelContainer { ThemeTypeVariation = "Well", SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, PreviewMinHeight) };
        _picture = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _placeholder = new Label
        {
            Text = L10n.T("Move along the timeline below to see what was on screen."),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        preview.AddChild(_picture);
        preview.AddChild(_placeholder);
        Body.AddChild(preview);

        _moment = Widgets.Text("", 13, Palette.Accent, bold: true);
        Body.AddChild(_moment);
        _bar = new TimelineBar(explorer);
        _bar.Scrubbed += ScrubTo;
        Body.AddChild(_bar);

        var actions = new HBoxContainer();
        _file = new Label { ThemeTypeVariation = "Hint" };
        _file.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _file.ClipText = true;
        _play = new Button { Text = L10n.T("Open in player"), Visible = false, FocusMode = FocusModeEnum.None };
        _play.Pressed += () =>
        {
            if (_explorer.OpenablePath is { } path) OS.ShellOpen(path);
        };
        _reveal = new Button { Text = L10n.T("Show in folder"), Disabled = true, FocusMode = FocusModeEnum.None };
        _reveal.Pressed += () =>
        {
            if (_explorer.RevealFolder is { } folder) OS.ShellOpen(folder);
        };
        actions.AddChild(_file);
        actions.AddChild(_play);
        actions.AddChild(_reveal);
        Body.AddChild(actions);

        // Anything still decoding is for a panel that is going away.
        Closed += _thumbnails.CancelAll;
        AddButton(L10n.T("Close"), Close, primary: true);
    }

    // A smoke run has no mouse to scrub with.
    public void ScrubForSmoke(double offset) => Callable.From(() => ScrubTo(offset)).CallDeferred();

    private void ScrubTo(double offset)
    {
        var moment = _explorer.ScrubTo(offset);
        _bar.QueueRedraw();
        _moment.Text = moment.Text;
        _reveal.Disabled = !_explorer.CanReveal;
        _play.Visible = moment.IsVideo && _explorer.OpenablePath is not null;
        if (moment.Path is not { } path)
        {
            _file.Text = "";
            ShowText(L10n.T("Nothing was on screen yet at this point."));
            return;
        }
        _file.Text = Path.GetFileName(path);
        if (!moment.IsVideo)
        {
            ShowPicture(Stills.Load(path, _picture.Size) is { } image ? ImageTexture.CreateFromImage(image) : null, path);
            return;
        }
        if (moment.RequestFrame) _thumbnails.Request(path, VideoFrameReady);
        if (_videoFrames.TryGetValue(path, out var frame)) ShowPicture(frame, path);
        else ShowText(Path.GetFileName(path) + "\n\n" + L10n.T("Grabbing a frame..."));
    }

    private void VideoFrameReady(string path, object? image)
    {
        ImageTexture? frame = null;
        if (image is Image picture)
        {
            Stills.FitInto(picture, _picture.Size);
            frame = ImageTexture.CreateFromImage(picture);
        }
        _videoFrames[path] = frame;
        // The scrub may have moved on while it decoded.
        if (_explorer.SelectedPath == path) ShowPicture(frame, path);
    }

    private void ShowPicture(Texture2D? texture, string path)
    {
        if (texture is null)
        {
            ShowText(Path.GetFileName(path) + "\n\n" + L10n.T("No preview available for this file."));
            return;
        }
        _picture.Texture = texture;
        _placeholder.Visible = false;
    }

    private void ShowText(string text)
    {
        _picture.Texture = null;
        _placeholder.Text = text;
        _placeholder.Visible = true;
    }

    private void Save()
    {
        if (!_explorer.Save())
        {
            Confirm.Tell(_host, L10n.T("Could not save"), SessionExplorer.SaveFailedMessage);
            return;
        }
        // Disabled rather than hidden: a button that vanishes reads as a failure.
        _save!.Disabled = true;
        _save.Text = L10n.T("Saved");
        Confirm.Tell(_host, L10n.T("Session saved"), SessionExplorer.SavedMessage);
    }

    // The session end to end: every segment a block as wide as it lasted, a seam at each boundary,
    // the fake-outs dashed, the climax outlined in its outcome's colour, and the playhead. Scrubbed
    // on hover, no click needed, as a video's seek bar is.
    private sealed partial class TimelineBar : Control
    {
        private const float Radius = 10;
        private readonly SessionExplorer _explorer;

        public TimelineBar(SessionExplorer explorer)
        {
            _explorer = explorer;
            CustomMinimumSize = new Vector2(0, 54);
            MouseDefaultCursorShape = CursorShape.PointingHand;
        }

        public event Action<double>? Scrubbed;

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseMotion or InputEventMouseButton { Pressed: true })
                Scrubbed?.Invoke(_explorer.TimeAtX(((InputEventMouse)@event).Position.X, Size.X));
        }

        private static Color KindColour(string kind) => UiTheme.Of(kind switch
        {
            "pause" => Palette.Pause,
            "finale" => Palette.Accent,
            _ => Palette.Secondary,
        });

        private static Color OutcomeColour(string? outcome) => UiTheme.Of(outcome switch
        {
            "ruined" => Palette.Ruined,
            "denied" => Palette.Denied,
            _ => Palette.Accent,
        });

        public override void _Draw()
        {
            var darkest = UiTheme.Of(Palette.SurfaceDarkest);
            DrawStyleBox(UiTheme.Box(darkest, (int)Radius), new Rect2(Vector2.Zero, Size));
            var width = Size.X;
            foreach (var segment in _explorer.Segments)
            {
                var left = (float)_explorer.XForTime(segment.Start, width);
                var right = (float)_explorer.XForTime(segment.End, width);
                // Never thinner than a pixel: a two-second pause in a thirty-minute session would
                // otherwise round away to nothing at all.
                DrawRect(new Rect2(left, 0, Math.Max(1, right - left), Size.Y), KindColour(segment.Kind));
            }
            // A seam at every boundary, or two beat segments run together into one block.
            for (var i = 1; i < _explorer.Segments.Count; i++)
            {
                var x = (int)_explorer.XForTime(_explorer.Segments[i].Start, width);
                DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), darkest);
            }
            // Fake-outs first, so the real climax is drawn over one that landed on top of it.
            foreach (var offset in _explorer.FakeOffsets)
            {
                var x = (int)_explorer.XForTime(offset, width);
                DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), darkest, 5);
                DrawDashedLine(new Vector2(x, 0), new Vector2(x, Size.Y), UiTheme.Of(Palette.Text), 2, 4);
            }
            if (_explorer.ClimaxOffset is { } climax)
            {
                // Outlined: a "real" outcome is the colour of the finale block it sits on.
                var x = (int)_explorer.XForTime(climax, width);
                DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), darkest, 7);
                DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), OutcomeColour(_explorer.Outcome), 3);
            }
            if (_explorer.PlayheadOffset is { } playhead)
            {
                var x = (int)_explorer.XForTime(playhead, width);
                DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), UiTheme.Of(Palette.Text), 2);
            }
        }
    }
}
