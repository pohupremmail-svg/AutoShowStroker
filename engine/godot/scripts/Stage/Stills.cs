using System;
using System.IO;
using Gooner.Core.Logging;
using Gooner.Core.Media;
using Gooner.Core.Media.Gif;
using Godot;

namespace Gooner.Engine.Stage;

// Single pictures of media for the panels that look back at them: an image as it is, a GIF's
// first frame, a video's frame grabbed through the FFmpeg extension (FrameGrabber). Never larger
// than they are shown.
public static class Stills
{
    private const string Log = "Stills";

    // load_scaled_pixmap: the picture, scaled down to fit `fit`, or null when it will not load.
    public static Image? Load(string path, Vector2 fit)
    {
        Image? image = null;
        try
        {
            image = MediaKinds.Of(path) == MediaKind.Gif ? FirstGifFrame(path) : Image.LoadFromFile(path);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            AppLog.Warning(Log, "Could not load {0}: {1}", Path.GetFileName(path), e.Message);
        }
        if (image is null || image.IsEmpty()) return null;
        FitInto(image, fit);
        return image;
    }

    private static Image? FirstGifFrame(string path)
    {
        using var decoder = new GifDecoder(path);
        return decoder.ReadNextFrame() is { } frame
            ? Image.CreateFromData(decoder.Width, decoder.Height, false, Image.Format.Rgba8, frame.Rgba)
            : null;
    }

    // Down, never up, keeping the aspect ratio.
    public static void FitInto(Image image, Vector2 size)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (size.X < 1 || size.Y < 1) return;
        var scale = Math.Min(size.X / image.GetWidth(), size.Y / image.GetHeight());
        if (scale >= 1) return;
        image.Resize(Math.Max(1, (int)(image.GetWidth() * scale)), Math.Max(1, (int)(image.GetHeight() * scale)), Image.Interpolation.Lanczos);
    }

    // How bright a picture is, read off a small copy - what the thumbnailer uses to skip black
    // leaders and fades.
    public static double BrightnessOf(Image image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var sample = (Image)image.Duplicate();
        sample.Resize(32, 32, Image.Interpolation.Nearest);
        sample.Convert(Image.Format.Rgba8);
        return Brightness.Average(sample.GetData(), 32, 32);
    }
}

// One video decoder for the thumbnailer: plays the clip silently out of sight, jumps a third of the
// way in once its length is known - leaders and fade-ins are black at the start - and hands over a
// frame every few frames until it is told to stop.
//
// The extension's own texture cannot be read back (it is not made copyable), so the player draws
// into a small viewport of its own, shaped like the clip once its size is known, and that is read.
public partial class FrameGrabber : Node, IVideoFrameGrabber
{
    private const string StreamClass = "FFmpegVideoStream";
    private const double SeekRatio = 0.35;
    private const int SampleEvery = 4;
    private const int Width = 480;

    private SubViewport? _viewport;
    private VideoStreamPlayer? _player;
    private Action<GrabbedFrame>? _onFrame;
    private bool _seeked;
    private bool _shaped;
    private int _frames;

    public void Start(string path, Action<GrabbedFrame> onFrame)
    {
        _onFrame = onFrame;
        if (!ClassDB.ClassExists(StreamClass)) return;
        var stream = ClassDB.Instantiate(StreamClass).As<VideoStream>();
        stream.File = path;
        _viewport = new SubViewport
        {
            Size = new Vector2I(Width, Width * 9 / 16),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Disable3D = true,
            // Clear to nothing rather than to grey: a frame not drawn yet then reads as black, and
            // the thumbnailer waits on, instead of taking the background for a picture.
            TransparentBg = true,
        };
        _player = new VideoStreamPlayer { Stream = stream, VolumeDb = -80, Expand = true, Size = _viewport.Size };
        _viewport.AddChild(_player);
        AddChild(_viewport);
        _player.Play();
    }

    public void Halt()
    {
        _onFrame = null;
        _player?.Stop();
        QueueFree();
    }

    public override void _Process(double delta)
    {
        if (_player is null || _viewport is null || _onFrame is null) return;
        if (!_seeked && _player.GetStreamLength() > 0)
        {
            _seeked = true;
            _player.StreamPosition = _player.GetStreamLength() * SeekRatio;
            return;
        }
        if (_player.GetVideoTexture() is not { } texture || texture.GetWidth() <= 0) return;
        if (!_shaped)
        {
            _shaped = true;
            _viewport.Size = new Vector2I(Width, Math.Max(1, Width * texture.GetHeight() / texture.GetWidth()));
            _player.Size = _viewport.Size;
            return;
        }
        if (++_frames % SampleEvery != 0) return;
        var image = _viewport.GetTexture().GetImage();
        if (image is null || image.IsEmpty()) return;
        _onFrame(new GrabbedFrame(image, Stills.BrightnessOf(image)));
    }
}
