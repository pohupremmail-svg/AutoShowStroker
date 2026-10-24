using System;
using System.IO;
using System.Threading.Tasks;
using Gooner.Core.App;
using Gooner.Core.Localization;
using Gooner.Core.Logging;
using Gooner.Core.Media;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine.Stage;

// What is on screen: the engine's side of Gooner.Core's PlaylistPlayer. A picture, a GIF, a clip, or
// a line where the picture goes; each Show replaces whatever was up before it. A clip's end and a
// clip that will never play go back to the player, which decides what comes next.
//
// A picture or a GIF is never read in the frame it is shown in. Done there, a large photo held that
// frame for most of a second - its JPEG decoded and scaled on the main thread, measured at 0.8 s for 24
// megapixels - and the note track and her hand jumped by as much. So the medium that comes next is got
// ready on a worker while this one is up (Prepare): a picture read and scaled, a GIF opened at its
// first frame. One that was not got ready (the first of a session, a key pressed) is read on a worker
// all the same, and goes up when it is ready, the one before it staying up meanwhile.
//
// A clip is neither: the FFmpeg extension makes its texture as it opens the file, which only the
// render thread may do (tried - texture_clear errors and a failing smoke run), and opening one costs
// 10-40 ms anyway.
public partial class MediaStage : Control, IMediaStage
{
    private const string Log = "MediaStage";

    private readonly Label _message;
    private Control? _shown;
    private ImageTexture? _picture;
    private bool _muted;
    // Got ready ahead of being asked for (Prepare), and asked for but still on its way.
    private (string Path, Task<object?> Ready)? _next;
    private (string Path, MediaKind Kind, Task<object?> Ready)? _coming;

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
            Text = IdleText,
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

    // What is up right now, for a check of the running engine: "image", "gif", "video", "message" or
    // null.
    public string? Showing { get; private set; } = "message";

    public void ShowImage(string path) => Await(path, MediaKind.Image);

    public void ShowGif(string path) => Await(path, MediaKind.Gif);

    public void PlayVideo(string path, double volume)
    {
        DropComing();
        Clear();
        var view = new VideoView(path, volume, _muted);
        view.Ended += () => Player?.VideoEnded();
        view.Failed += reason =>
        {
            // The reason only - the file's folder never reaches the log, and the player logs it.
            if (_shown == view) ShowMessage(L10n.T("This clip could not be played."));
            Player?.VideoFailed(reason);
        };
        Show(view, "video");
    }

    public void ReplayVideo() => (_shown as VideoView)?.Replay();

    // Gets `path` ready on a worker, in place of whatever was being got ready before it.
    public void Prepare(string path)
    {
        if (_next?.Path == path) return;
        if (_next is { } replaced) LetGo(replaced.Ready);
        _next = (path, Read(path, MediaKinds.Of(path)));
    }

    // Up once it is ready - at once, if it was got ready ahead.
    private void Await(string path, MediaKind kind)
    {
        DropComing();
        Task<object?> ready;
        if (_next is { } next && next.Path == path)
        {
            ready = next.Ready;
            _next = null;
        }
        else
        {
            ready = Read(path, kind);
        }
        _coming = (path, kind, ready);
        Arrive();
    }

    public override void _Process(double delta) => Arrive();

    private void Arrive()
    {
        if (_coming is not { } coming || !coming.Ready.IsCompleted) return;
        _coming = null;
        var ready = coming.Ready.IsCompletedSuccessfully ? coming.Ready.Result : null;
        if (coming.Ready.Exception is { } failed)
            AppLog.Warning(Log, "Could not read {0}: {1}", Path.GetFileName(coming.Path), failed.InnerException is { } inner ? AppLog.Describe(inner) : "unknown");
        Clear();
        if (coming.Kind == MediaKind.Image) Picture(coming.Path, ready as Image);
        else Gif(coming.Path, ready as GifView.Start);
    }

    private void Picture(string path, Image? image)
    {
        if (image is null)
        {
            // The file name only - never the folder.
            AppLog.Warning(Log, "Could not show {0}", Path.GetFileName(path));
            ShowMessage(L10n.T("This picture could not be opened."));
            return;
        }
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

    // GifView.Open has said in the log why a file would not open.
    private void Gif(string path, GifView.Start? start)
    {
        if (start is null)
        {
            ShowMessage(L10n.T("This GIF could not be opened."));
            return;
        }
        Show(new GifView(path, new Vector2I((int)Size.X, (int)Size.Y), start), "gif");
    }

    // A picture read and scaled, or a GIF opened at its first frame, on a worker. Null for a file that
    // is neither, or will not open.
    private Task<object?> Read(string path, MediaKind kind)
    {
        var size = Size;
        var target = new Vector2I((int)size.X, (int)size.Y);
        return Task.Run<object?>(() => kind switch
        {
            MediaKind.Image => LoadPicture(path, size),
            MediaKind.Gif => GifView.Open(path, target),
            _ => null,
        });
    }

    // What was asked for and is still on its way, no longer wanted.
    private void DropComing()
    {
        if (_coming is { } coming) LetGo(coming.Ready);
        _coming = null;
    }

    // Let go of once it is done.
    private static void LetGo(Task<object?> ready) =>
        ready.ContinueWith(done =>
        {
            if (done.IsCompletedSuccessfully) Release(done.Result);
        }, TaskScheduler.Default);

    private static void Release(object? ready)
    {
        switch (ready)
        {
            case Image image:
                image.Dispose();
                break;
            case GifView.Start start:
                start.Dispose();
                break;
        }
    }

    public override void _ExitTree()
    {
        if (_next is { } next) LetGo(next.Ready);
        _next = null;
        DropComing();
    }

    // What the stage says before anything has been loaded.
    private static string IdleText => L10n.T("No Gooning files selected yet.");

    // After a change of language: the words up before anything was loaded, in the new one.
    public void Reword()
    {
        if (Showing == "message" && _wordedIdle) _message.Text = IdleText;
    }

    private bool _wordedIdle = true;

    public void ShowMessage(string text)
    {
        DropComing();
        _wordedIdle = false;
        Clear();
        _message.Text = text;
        _message.Visible = true;
        Showing = "message";
    }

    // What the Python player leaves up when a session stops: the picture stays, a GIF holds its
    // frame. Nothing moves until the next session shows something else - nor does a picture still
    // on its way go up after it.
    public void StopAll()
    {
        DropComing();
        if (_shown is GifView gif) gif.Frozen = true;
        if (_shown is VideoView video) video.Freeze();
    }

    // The clip's sound; the click is muted separately.
    public void SetMuted(bool muted)
    {
        _muted = muted;
        if (_shown is VideoView video) video.Muted = muted;
    }

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

    // Read and scaled to the size it is shown at; null for a file that is no picture. Safe on any thread.
    private static Image? LoadPicture(string path, Vector2 size)
    {
        var image = new Image();
        if (image.Load(path) != Error.Ok)
        {
            image.Dispose();
            return null;
        }
        FitInto(image, size);
        return image;
    }

    // Down to the size it is shown at, never up: a 6000-pixel photo held at full size is 140 MB of
    // video memory for what fills a fifth of it. Halved first while it is at least twice too big -
    // averaging four pixels into one is cheap, and it leaves Lanczos a quarter of the work each time
    // (half a second on a 24-megapixel photo without).
    private static void FitInto(Image image, Vector2 size)
    {
        if (size.X < 1 || size.Y < 1) return;
        while (Math.Min(size.X / image.GetWidth(), size.Y / image.GetHeight()) <= 0.5f) image.ShrinkX2();
        var scale = Math.Min(size.X / image.GetWidth(), size.Y / image.GetHeight());
        if (scale >= 1) return;
        image.Resize(Math.Max(1, (int)(image.GetWidth() * scale)), Math.Max(1, (int)(image.GetHeight() * scale)), Image.Interpolation.Lanczos);
    }
}
