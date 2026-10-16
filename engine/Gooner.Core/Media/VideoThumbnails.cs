using Gooner.Core.Logging;
using Gooner.Core.Timing;

namespace Gooner.Core.Media;

// Port of average_brightness / is_mostly_black in src/video_thumbnails.py: a cheap check that
// catches black leader frames and fades, so a thumbnail does not land on one. The mean of
// (r + g + b) / 3 over a 16 x 16 sample of the picture - Qt scaled a copy down to that size.
public static class Brightness
{
    // Below this a frame counts as a black leader or fade rather than a usable thumbnail.
    public const double BlackFrameThreshold = 20;

    private const int Sample = 16;

    // `rgba` is 8 bits per channel, row by row.
    public static double Average(ReadOnlySpan<byte> rgba, int width, int height)
    {
        if (width <= 0 || height <= 0 || rgba.Length < width * height * 4) return 0;
        double total = 0;
        for (var sy = 0; sy < Sample; sy++)
        {
            var y = Math.Min(height - 1, (int)((sy + 0.5) * height / Sample));
            for (var sx = 0; sx < Sample; sx++)
            {
                var x = Math.Min(width - 1, (int)((sx + 0.5) * width / Sample));
                var at = (y * width + x) * 4;
                total += (rgba[at] + rgba[at + 1] + rgba[at + 2]) / 3.0;
            }
        }
        return total / (Sample * Sample);
    }

    public static bool IsMostlyBlack(ReadOnlySpan<byte> rgba, int width, int height, double threshold = BlackFrameThreshold) =>
        Average(rgba, width, height) < threshold;
}

// A decoded frame, as the engine hands it over: its image (whatever the engine draws with) and
// how bright it is.
public sealed record GrabbedFrame(object Image, double Brightness);

// One video decoder, reduced to start and stop and a frame callback - so the queue can be driven
// by a stub in a test, and by the engine's FFmpeg player for real.
public interface IVideoFrameGrabber
{
    void Start(string path, Action<GrabbedFrame> onFrame);

    void Halt();
}

// Port of VideoThumbnailQueue (.NET will not have a class end in Queue unless it is one): one
// still per video, grabbed in the background, one clip at a time. Request(path, callback) queues a
// clip; the callback gets (path, image) when a usable frame arrives, or (path, the best seen,
// maybe null) when the clip yields nothing better before the timeout. One at a time on purpose: a
// long session opens the explorer on dozens of videos at once, and a decoder each would bury the
// machine.
public sealed class VideoThumbnailer
{
    // How long a single clip may take before it is given up on: a codec nothing can open produces
    // nothing however long it is given.
    public const int GrabTimeoutMs = 2500;

    private const string Log = "video_thumbnails";

    private readonly Func<IVideoFrameGrabber> _grabberFactory;
    private readonly ClockTimer _timeout;
    private readonly List<(string Path, Action<string, object?> Callback)> _pending = [];
    private (string Path, Action<string, object?> Callback)? _current;
    private IVideoFrameGrabber? _grabber;
    private GrabbedFrame? _best;

    public VideoThumbnailer(Func<IVideoFrameGrabber> grabberFactory, ClockScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(grabberFactory);
        ArgumentNullException.ThrowIfNull(scheduler);
        _grabberFactory = grabberFactory;
        _timeout = scheduler.CreateTimer(GiveUp, singleShot: true);
    }

    public void Request(string path, Action<string, object?> callback)
    {
        _pending.Add((path, callback));
        if (_current is null) StartNext();
    }

    // Drops everything queued and stops the running grab - for a panel going away.
    public void CancelAll()
    {
        _pending.Clear();
        FinishCurrent();
    }

    private void StartNext()
    {
        if (_pending.Count == 0) return;
        _current = _pending[0];
        _pending.RemoveAt(0);
        _best = null;
        var grabber = _grabberFactory();
        _grabber = grabber;
        _timeout.Start(GrabTimeoutMs);
        grabber.Start(_current.Value.Path, frame => OnFrame(grabber, frame));
    }

    private void OnFrame(IVideoFrameGrabber from, GrabbedFrame frame)
    {
        // Cancelled, or moved on, while the decoder was still running.
        if (_current is null || from != _grabber) return;
        // The least-black frame seen, rather than the first: a later one can be dark-but-less-dark
        // without ever clearing the threshold.
        if (_best is null || frame.Brightness > _best.Brightness) _best = frame;
        if (frame.Brightness >= Brightness.BlackFrameThreshold) Deliver(_best.Image);
    }

    // The timeout: hand over whatever was seen. Some clips really are dark throughout, and a dim
    // thumbnail beats an empty cell.
    internal void GiveUp()
    {
        if (_current is not { } current) return;
        if (_best is null)
            AppLog.Warning(Log, "No video frame could be decoded for {0}", Path.GetFileName(current.Path));
        Deliver(_best?.Image);
    }

    private void Deliver(object? image)
    {
        var (path, callback) = _current!.Value;
        FinishCurrent();
        callback(path, image);
        StartNext();
    }

    private void FinishCurrent()
    {
        _timeout.Stop();
        _grabber?.Halt();
        _grabber = null;
        _current = null;
    }
}
