using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Gooner.Core.Logging;
using Gooner.Core.Media.Gif;
using Godot;

namespace Gooner.Engine.Stage;

// Plays a GIF through Gooner.Core's decoder: the first frame at once (so something is on screen
// straight away, like QMovie.jumpToFrame(0)), the rest on a worker thread a few frames ahead,
// scaled there to the size it is shown at. A GIF whose whole loop fits the cache budget is decoded
// once and replayed from memory; a bigger one is streamed and decoded again each loop. Always
// loops - a slideshow has no use for a GIF that freezes on its last frame. From the phase 0 spike,
// where it kept the file's own timing to within 0.4 % with no late frames.
public partial class GifView : TextureRect
{
    private const string Log = "GifView";
    private const int QueueDepth = 4;
    private const long CacheBudgetBytes = 96L * 1024 * 1024;

    private readonly string _path;
    private readonly Vector2I _target;
    private Start? _start;
    private readonly ConcurrentQueue<(Image? Image, int DelayMs, bool LoopEnd)> _queue = new();
    private readonly List<(Image Image, int DelayMs)> _cache = [];
    private Thread? _worker;
    private volatile bool _running;
    private bool _cacheComplete;
    private ImageTexture? _texture;
    private double _shownForMs;
    private int _currentDelayMs = GifDecoder.DefaultDelayMs;
    private long _cacheBytes;
    private bool _cacheOverflow;
    private bool _playingFromCache;
    private int _cacheIndex;

    // `target` is the size it is shown at; frames larger than that are scaled down on the worker.
    // `start`, when it is given, is the file already opened (Open) - the view takes it over.
    public GifView(string path, Vector2I target, Start? start = null)
    {
        _path = path;
        _target = target;
        _start = start;
        ExpandMode = ExpandModeEnum.IgnoreSize;
        StretchMode = StretchModeEnum.KeepAspectCentered;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    // Whether the file gave a first frame. False for a file that is no GIF, or has nothing in it.
    public bool Started { get; private set; }

    // Held on the frame it is showing - QMovie.stop(), what a stopped session leaves up.
    public bool Frozen { get; set; }

    // A GIF opened and its first frame decoded, scaled to the size it is shown at. Made when the GIF is
    // shown, or ahead of that on a worker (MediaStage.Prepare), so that opening the file and decoding a
    // large first frame no longer cost the frame it appears in. Disposed by whoever holds it last.
    public sealed class Start : IDisposable
    {
        internal Start(GifDecoder decoder, Image first, int delayMs)
        {
            Decoder = decoder;
            First = first;
            DelayMs = delayMs;
        }

        internal GifDecoder Decoder { get; }
        internal Image First { get; }
        internal int DelayMs { get; }

        public void Dispose()
        {
            Decoder.Dispose();
            First.Dispose();
        }
    }

    // Null for a file that is no GIF, or has nothing in it - said in the log by the file's name only,
    // the folder it sits in being nobody's business. Safe on any thread.
    public static Start? Open(string path, Vector2I target)
    {
        GifDecoder? decoder = null;
        try
        {
            decoder = new GifDecoder(path);
            if (decoder.ReadNextFrame() is { } first)
                return new Start(decoder, ToImage(first.Rgba, decoder.Width, decoder.Height, target), first.DelayMs);
            AppLog.Warning(Log, "Could not play {0}: no frames", System.IO.Path.GetFileName(path));
        }
        catch (Exception e) when (e is System.IO.IOException or InvalidDataException or UnauthorizedAccessException)
        {
            AppLog.Warning(Log, "Could not play {0}: {1}", System.IO.Path.GetFileName(path), e.Message);
        }
        decoder?.Dispose();
        return null;
    }

    public override void _Ready()
    {
        if ((_start ?? Open(_path, _target)) is not { } start) return;
        _start = null;
        _texture = ImageTexture.CreateFromImage(start.First);
        Texture = _texture;
        _currentDelayMs = start.DelayMs;
        Remember(start.First, start.DelayMs);
        Started = true;

        _running = true;
        var decoder = start.Decoder;
        _worker = new Thread(() => Work(decoder)) { IsBackground = true, Name = "gif-decode" };
        _worker.Start();
    }

    public override void _ExitTree()
    {
        // Handed over but never shown.
        _start?.Dispose();
        _start = null;
        _running = false;
        _worker?.Join(500);
        // A Godot Image keeps its pixels in native memory until the C# wrapper goes - released here
        // rather than whenever the collector gets round to it.
        foreach (var (image, _) in _cache) image.Dispose();
        _cache.Clear();
        while (_queue.TryDequeue(out var item)) item.Image?.Dispose();
    }

    private static Image ToImage(byte[] rgba, int width, int height, Vector2I target)
    {
        var image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba);
        if (target.X > 0 && target.Y > 0 && (target.X < width || target.Y < height))
        {
            var scale = Math.Min((float)target.X / width, (float)target.Y / height);
            image.Resize(Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)), Image.Interpolation.Bilinear);
        }
        return image;
    }

    private void Work(GifDecoder decoder)
    {
        using (decoder)
        {
            var buffer = new byte[decoder.Width * decoder.Height * 4];
            while (_running && !Volatile.Read(ref _cacheComplete))
            {
                if (_queue.Count >= QueueDepth)
                {
                    Thread.Sleep(2);
                    continue;
                }
                GifFrame? frame;
                try
                {
                    frame = decoder.ReadNextFrame(buffer);
                }
                catch (InvalidDataException e)
                {
                    AppLog.Warning(Log, "Stopped playing {0}: {1}", System.IO.Path.GetFileName(_path), e.Message);
                    return;
                }
                if (frame is null)
                {
                    _queue.Enqueue((null, 0, true));
                    // The main thread decides whether the cache holds the whole loop; if it does, it
                    // stops asking and this thread idles out.
                    if (Volatile.Read(ref _cacheComplete)) return;
                    decoder.Rewind();
                    continue;
                }
                _queue.Enqueue((ToImage(frame.Rgba, decoder.Width, decoder.Height, _target), frame.DelayMs, false));
            }
        }
    }

    private void Remember(Image image, int delayMs)
    {
        if (_cacheOverflow || _playingFromCache) return;
        _cacheBytes += (long)image.GetWidth() * image.GetHeight() * 4;
        if (_cacheBytes > CacheBudgetBytes)
        {
            _cacheOverflow = true;
            foreach (var (cached, _) in _cache) cached.Dispose();
            _cache.Clear();
            return;
        }
        _cache.Add((image, delayMs));
    }

    public override void _Process(double delta)
    {
        if (_texture is null || Frozen) return;
        _shownForMs += delta * 1000.0;
        // Catch up a few frames at most; after a long stall the current frame starts afresh.
        for (var guard = 0; guard < 4 && _shownForMs >= _currentDelayMs; guard++)
            if (!Advance()) return;
        if (_shownForMs > 1000) _shownForMs = 0;
    }

    private bool Advance()
    {
        if (_playingFromCache)
        {
            _cacheIndex = (_cacheIndex + 1) % _cache.Count;
            Show(_cache[_cacheIndex].Image, _cache[_cacheIndex].DelayMs);
            return true;
        }
        while (_queue.TryDequeue(out var item))
        {
            if (item.LoopEnd)
            {
                if (!_cacheOverflow && _cache.Count > 0)
                {
                    Volatile.Write(ref _cacheComplete, true);
                    _playingFromCache = true;
                    _cacheIndex = 0;
                    Show(_cache[0].Image, _cache[0].DelayMs);
                    return true;
                }
                // Streaming from here on: stop collecting.
                _cacheOverflow = true;
                continue;
            }
            var cachedBefore = _cache.Count;
            Remember(item.Image!, item.DelayMs);
            Show(item.Image!, item.DelayMs);
            // Streaming: the texture holds its own copy now, so this frame's pixels can go.
            if (_cache.Count == cachedBefore) item.Image!.Dispose();
            return true;
        }
        // The worker is behind: the current frame stays up a little longer.
        return false;
    }

    private void Show(Image image, int delayMs)
    {
        _shownForMs = Math.Max(0, _shownForMs - _currentDelayMs);
        _currentDelayMs = delayMs;
        if (image.GetSize() == _texture!.GetSize()) _texture.Update(image);
        else _texture.SetImage(image);
    }
}
