using Gooner.Core.Media;
using Gooner.Core.Tests.Support;
using Gooner.Core.Timing;

namespace Gooner.Core.Tests.Media;

// Port of tests/test_video_thumbnails.py: one still per video, grabbed in the background, one clip
// at a time. The grabber is a stub the test drives - a real one would start a decoder.
public sealed class VideoThumbnailerTests
{
    private readonly FakeTime _time = new() { Now = 1000.0 };
    private readonly ClockScheduler _scheduler;
    private readonly List<StubGrabber> _grabbers = [];
    private readonly List<(string Path, object? Image)> _got = [];
    private readonly VideoThumbnailer _queue;

    public VideoThumbnailerTests()
    {
        _scheduler = new ClockScheduler(_time);
        _queue = new VideoThumbnailer(() =>
        {
            var grabber = new StubGrabber();
            _grabbers.Add(grabber);
            return grabber;
        }, _scheduler);
    }

    private void Sink(string path, object? image) => _got.Add((path, image));

    private static byte[] Solid(uint rgb, int size = 8)
    {
        var pixels = new byte[size * size * 4];
        for (var i = 0; i < size * size; i++)
        {
            pixels[i * 4] = (byte)(rgb >> 16);
            pixels[i * 4 + 1] = (byte)(rgb >> 8);
            pixels[i * 4 + 2] = (byte)rgb;
            pixels[i * 4 + 3] = 255;
        }
        return pixels;
    }

    private static GrabbedFrame Frame(uint rgb) => new($"image {rgb:X6}", Brightness.Average(Solid(rgb), 8, 8));

    // --- brightness ---

    [Fact]
    public void is_mostly_black_detects_a_black_frame() => Assert.True(Brightness.IsMostlyBlack(Solid(0x000000), 8, 8));

    [Fact]
    public void is_mostly_black_rejects_a_bright_frame() => Assert.False(Brightness.IsMostlyBlack(Solid(0xFF00BF), 8, 8));

    [Fact]
    public void is_mostly_black_respects_a_custom_threshold()
    {
        var darkGray = Solid(0x0F0F0F);
        Assert.False(Brightness.IsMostlyBlack(darkGray, 8, 8, threshold: 10));
        Assert.True(Brightness.IsMostlyBlack(darkGray, 8, 8, threshold: 20));
    }

    [Fact]
    public void average_brightness_orders_dark_to_bright()
    {
        Assert.True(Brightness.Average(Solid(0x000000), 8, 8) < Brightness.Average(Solid(0x0F0F0F), 8, 8));
        Assert.True(Brightness.Average(Solid(0x0F0F0F), 8, 8) < Brightness.Average(Solid(0xFFFFFF), 8, 8));
    }

    // The mean of (r + g + b) / 3 over the picture, as the Python side read it off a 16 x 16 copy.
    [Fact]
    public void the_brightness_of_a_solid_picture_is_its_mean_channel()
    {
        Assert.Equal((0xFF + 0x00 + 0xBF) / 3.0, Brightness.Average(Solid(0xFF00BF), 8, 8), 6);
        Assert.Equal(0x0F, Brightness.Average(Solid(0x0F0F0F, size: 64), 64, 64), 6);
    }

    [Fact]
    public void an_empty_picture_is_black() => Assert.Equal(0, Brightness.Average([], 0, 0));

    // --- the queue ---

    [Fact]
    public void a_request_starts_a_grab()
    {
        _queue.Request("clip.mp4", Sink);
        Assert.Equal("clip.mp4", _grabbers[0].Path);
    }

    // A grabber per pending video would have a long session opening dozens of decoders at once
    // the moment the explorer is scrolled.
    [Fact]
    public void only_one_video_is_decoded_at_a_time()
    {
        _queue.Request("a.mp4", Sink);
        _queue.Request("b.mp4", Sink);

        Assert.Single(_grabbers);
    }

    [Fact]
    public void the_next_video_starts_once_the_first_delivers()
    {
        _queue.Request("a.mp4", Sink);
        _queue.Request("b.mp4", Sink);

        _grabbers[0].Deliver(Frame(0xFFFFFF));

        Assert.Equal(2, _grabbers.Count);
        Assert.Equal("b.mp4", _grabbers[1].Path);
    }

    [Fact]
    public void a_delivered_frame_reaches_the_caller()
    {
        _queue.Request("a.mp4", Sink);

        _grabbers[0].Deliver(Frame(0xFF00BF));

        Assert.Equal([("a.mp4", (object?)"image FF00BF")], _got);
    }

    [Fact]
    public void the_player_is_stopped_once_it_has_delivered()
    {
        _queue.Request("a.mp4", Sink);
        _grabbers[0].Deliver(Frame(0xFFFFFF));
        Assert.True(_grabbers[0].Stopped);
    }

    // Black leader frames and fades are why the grab waits rather than taking the first frame.
    [Fact]
    public void a_black_frame_does_not_settle_the_grab()
    {
        _queue.Request("a.mp4", Sink);

        _grabbers[0].Deliver(Frame(0x000000));

        Assert.Empty(_got);
    }

    [Fact]
    public void a_bright_frame_after_a_black_one_wins()
    {
        _queue.Request("a.mp4", Sink);

        _grabbers[0].Deliver(Frame(0x000000));
        _grabbers[0].Deliver(Frame(0xFFFFFF));

        Assert.Equal([("a.mp4", (object?)"image FFFFFF")], _got);
    }

    // Some clips really are dark throughout - a dim thumbnail beats none.
    [Fact]
    public void giving_up_reports_the_best_frame_seen()
    {
        _queue.Request("a.mp4", Sink);
        _grabbers[0].Deliver(Frame(0x0A0A0A));

        _queue.GiveUp();

        Assert.Equal([("a.mp4", (object?)"image 0A0A0A")], _got);
    }

    [Fact]
    public void giving_up_with_nothing_at_all_reports_no_pixmap()
    {
        _queue.Request("a.mp4", Sink);

        _queue.GiveUp();

        Assert.Equal([("a.mp4", (object?)null)], _got);
    }

    [Fact]
    public void giving_up_moves_on_to_the_next_video()
    {
        _queue.Request("a.mp4", Sink);
        _queue.Request("b.mp4", Sink);

        _queue.GiveUp();

        Assert.Equal("b.mp4", _grabbers[1].Path);
    }

    // A codec nothing can open produces nothing however long it is given.
    [Fact]
    public void a_clip_gives_up_on_its_own_after_the_timeout()
    {
        _queue.Request("a.mp4", Sink);

        _time.Now += VideoThumbnailer.GrabTimeoutMs / 1000.0 - 0.01;
        _scheduler.Advance();
        Assert.Empty(_got);
        _time.Now += 0.02;
        _scheduler.Advance();

        Assert.Equal([("a.mp4", (object?)null)], _got);
    }

    [Fact]
    public void cancel_all_drops_pending_work()
    {
        _queue.Request("a.mp4", Sink);
        _queue.Request("b.mp4", Sink);

        _queue.CancelAll();
        _grabbers[0].Deliver(Frame(0xFFFFFF));

        Assert.Empty(_got);
        Assert.Single(_grabbers);
    }

    [Fact]
    public void cancel_all_stops_the_running_player()
    {
        _queue.Request("a.mp4", Sink);

        _queue.CancelAll();

        Assert.True(_grabbers[0].Stopped);
    }

    private sealed class StubGrabber : IVideoFrameGrabber
    {
        private Action<GrabbedFrame>? _onFrame;

        public string? Path { get; private set; }

        public bool Stopped { get; private set; }

        public void Start(string path, Action<GrabbedFrame> onFrame)
        {
            Path = path;
            _onFrame = onFrame;
        }

        public void Halt() => Stopped = true;

        public void Deliver(GrabbedFrame frame) => _onFrame!(frame);
    }
}
