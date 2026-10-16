using System;
using System.IO;
using Gooner.Core.App;
using Gooner.Core.Logging;
using Gooner.Core.Media;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine.Stage;

// What is on screen: the engine's side of Gooner.Core's PlaylistPlayer. A picture, a GIF, or a line
// where the picture goes; each Show replaces whatever was up before it.
//
// Video is not part of this build yet: the FFmpeg extension it needs brings native libraries the
// repo has no home for so far. A clip is reported as unplayable, which is the recovery path the
// player already has for a file the backend cannot open - the session moves on a second later.
public partial class MediaStage : Control, IMediaStage
{
    private const string Log = "MediaStage";

    private readonly Label _message;
    private Control? _shown;
    private ImageTexture? _picture;

    public MediaStage()
    {
        ClipContents = true;
        SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(new Panel
        {
            AnchorRight = 1,
            AnchorBottom = 1,
            MouseFilter = MouseFilterEnum.Ignore,
        });
        GetChild<Panel>(0).AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Of(Palette.SurfaceDark), 12));
        _message = new Label
        {
            Text = "No Gooning files selected yet.",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorRight = 1,
            AnchorBottom = 1,
        };
        _message.AddThemeFontSizeOverride("font_size", 20);
        AddChild(_message);
    }

    // Where a clip's end or failure is reported. Set once the player exists.
    public PlaylistPlayer? Player { get; set; }

    // What is up right now, for a check of the running engine: "image", "gif", "message" or null.
    public string? Showing { get; private set; } = "message";

    public void ShowImage(string path)
    {
        Clear();
        var image = new Image();
        if (image.Load(path) != Error.Ok)
        {
            // The file name only - never the folder.
            AppLog.Warning(Log, "Could not show {0}", Path.GetFileName(path));
            image.Dispose();
            ShowMessage("This picture could not be opened.");
            return;
        }
        FitInto(image, Size);
        _picture = ImageTexture.CreateFromImage(image);
        image.Dispose();
        Show(new TextureRect
        {
            Texture = _picture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            AnchorRight = 1,
            AnchorBottom = 1,
        }, "image");
    }

    public void ShowGif(string path)
    {
        Clear();
        var view = new GifView(path, new Vector2I((int)Size.X, (int)Size.Y));
        Show(view, "gif");
        if (!view.Started) ShowMessage("This GIF could not be opened.");
    }

    public void PlayVideo(string path, double volume)
    {
        Clear();
        ShowMessage("Video comes in a later build.");
        // Deferred, as a real backend's error would arrive: after the player has finished loading.
        Callable.From(() => Player?.VideoFailed("video playback is not part of this build yet")).CallDeferred();
    }

    public void ReplayVideo() { }

    public void ShowMessage(string text)
    {
        Clear();
        _message.Text = text;
        _message.Visible = true;
        Showing = "message";
    }

    // What the Python player leaves up when a session stops: the picture stays, a GIF holds its
    // frame. Nothing moves until the next session shows something else.
    public void StopAll()
    {
        if (_shown is GifView gif) gif.Frozen = true;
    }

    public void SetMuted(bool muted) { }

    private void Show(Control control, string kind)
    {
        _message.Visible = false;
        _shown = control;
        AddChild(control);
        Showing = kind;
    }

    private void Clear()
    {
        if (_shown is not null)
        {
            RemoveChild(_shown);
            _shown.QueueFree();
            _shown = null;
        }
        _picture?.Dispose();
        _picture = null;
        Showing = null;
    }

    // Down to the size it is shown at, never up: a 6000-pixel photo held at full size is 140 MB of
    // video memory for what fills a fifth of it.
    private static void FitInto(Image image, Vector2 size)
    {
        if (size.X < 1 || size.Y < 1) return;
        var scale = Math.Min(size.X / image.GetWidth(), size.Y / image.GetHeight());
        if (scale >= 1) return;
        image.Resize(Math.Max(1, (int)(image.GetWidth() * scale)), Math.Max(1, (int)(image.GetHeight() * scale)), Image.Interpolation.Lanczos);
    }
}
