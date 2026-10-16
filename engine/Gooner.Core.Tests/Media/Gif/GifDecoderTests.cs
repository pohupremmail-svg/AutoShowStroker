using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Gooner.Core.Media.Gif;
using Gooner.Core.Tests.Support;

namespace Gooner.Core.Tests.Media.Gif;

// The GIF decoder against FFmpeg, frame by frame and pixel by pixel - the comparison phase 0 ran,
// kept. The files in data/ are written by make_edge_gifs.py to hit the corners of the format a
// decoder gets wrong; what FFmpeg makes of each frame is in expected.json (make_expected.py).
// Where the two disagree on purpose, a test below says why.
public class GifDecoderTests
{
    private static readonly string Data = Repo.PathTo("engine/Gooner.Core.Tests/Media/Gif/data");
    private static readonly JsonObject Expected = JsonNode.Parse(File.ReadAllText(Path.Combine(Data, "expected.json")))!.AsObject();

    // What browsers do rather than what FFmpeg does - see the tests of each.
    private static readonly HashSet<string> Deviating = ["edge_disposal_background.gif", "edge_truncated.gif"];

    public static TheoryData<string> Files()
    {
        var files = new TheoryData<string>();
        foreach (var (name, _) in Expected)
            if (!Deviating.Contains(name))
                files.Add(name);
        return files;
    }

    private static List<GifFrame> DecodeAll(string name, out GifDecoder decoder)
    {
        decoder = new GifDecoder(Path.Combine(Data, name));
        var frames = new List<GifFrame>();
        while (decoder.ReadNextFrame() is { } frame)
            frames.Add(frame);
        return frames;
    }

    // The canvas as make_expected.py hashes it: an invisible pixel has no colour worth comparing.
    private static string Hash(byte[] rgba)
    {
        var canvas = (byte[])rgba.Clone();
        for (var p = 0; p < canvas.Length; p += 4)
            if (canvas[p + 3] == 0)
                canvas[p] = canvas[p + 1] = canvas[p + 2] = 0;
        return Convert.ToHexStringLower(SHA256.HashData(canvas));
    }

    private static JsonArray ExpectedFrames(string name) => Expected[name]!["frames"]!.AsArray();

    [Theory]
    [MemberData(nameof(Files))]
    public void every_frame_is_what_ffmpeg_makes_of_it(string name)
    {
        var frames = DecodeAll(name, out var decoder);
        using (decoder)
        {
            var expected = ExpectedFrames(name);
            Assert.Equal((int)Expected[name]!["width"]!, decoder.Width);
            Assert.Equal((int)Expected[name]!["height"]!, decoder.Height);
            Assert.Equal(expected.Count, frames.Count);
            for (var i = 0; i < frames.Count; i++)
                Assert.True((string)expected[i]!["sha256"]! == Hash(frames[i].Rgba), $"{name} frame {i}");
            Assert.Empty(decoder.Warnings);
        }
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void every_delay_is_what_ffmpeg_reads(string name)
    {
        var frames = DecodeAll(name, out var decoder);
        decoder.Dispose();
        var expected = ExpectedFrames(name);
        for (var i = 0; i < frames.Count; i++)
        {
            var theirs = (int)expected[i]!["delay_ms"]!;
            // The one place it disagrees on purpose: see the next test.
            if (theirs < GifDecoder.MinDelayMs) continue;
            Assert.Equal(theirs, frames[i].DelayMs);
        }
    }

    // Browsers play a frame of 0 or 10 ms for 100 ms - a GIF someone made to look right in one
    // would otherwise race. FFmpeg takes 10 ms at its word.
    [Fact]
    public void delays_under_twenty_milliseconds_play_for_a_tenth_of_a_second()
    {
        var frames = DecodeAll("edge_delays.gif", out var decoder);
        decoder.Dispose();

        // Written as 0, 1, 2, 5 and 50 hundredths of a second.
        Assert.Equal([100, 100, 20, 50, 500], frames.Select(f => f.DelayMs));
    }

    // Disposal "to background" clears the frame's area to transparent, as browsers do; FFmpeg
    // fills it with the background colour, which nobody who made the file ever saw.
    [Fact]
    public void disposal_to_background_leaves_the_area_transparent()
    {
        var frames = DecodeAll("edge_disposal_background.gif", out var decoder);
        using (decoder)
        {
            // The second frame drew a box at (10, 10) and asked for it to be disposed of; the third
            // draws elsewhere, so a pixel of that box it does not cover is now see-through.
            var third = frames[2].Rgba;
            Assert.Equal(0, third[((12 * decoder.Width) + 12) * 4 + 3]);
            // The first frame was disposed of the same way before the box, so nothing of it is left.
            Assert.Equal(0, frames[1].Rgba[((60 * decoder.Width) + 90) * 4 + 3]);
        }
    }

    // A file cut off mid-frame yields what it has and says so, rather than throwing - GIFs come
    // from wherever the user found them.
    [Fact]
    public void a_truncated_file_yields_the_frames_it_has_and_says_so()
    {
        var frames = DecodeAll("edge_truncated.gif", out var decoder);
        using (decoder)
        {
            Assert.Equal(2, frames.Count);
            Assert.Equal((string)ExpectedFrames("edge_truncated.gif")[0]!["sha256"]!, Hash(frames[0].Rgba));
            Assert.Contains(decoder.Warnings, w => w.Contains("cut off", StringComparison.Ordinal));
            Assert.True(decoder.Ended);
        }
    }

    [Fact]
    public void rewinding_plays_it_again_from_the_first_frame()
    {
        using var decoder = new GifDecoder(Path.Combine(Data, "edge_disposal_previous.gif"));
        var first = new List<string>();
        while (decoder.ReadNextFrame() is { } frame) first.Add(Hash(frame.Rgba));

        decoder.Rewind();
        var second = new List<string>();
        while (decoder.ReadNextFrame() is { } frame) second.Add(Hash(frame.Rgba));

        Assert.Equal(first, second);
    }

    // A player that converts each frame at once hands the same buffer back every time.
    [Fact]
    public void a_reused_buffer_receives_the_frame()
    {
        using var decoder = new GifDecoder(Path.Combine(Data, "edge_interlaced.gif"));
        var buffer = new byte[decoder.Width * decoder.Height * 4];

        var frame = decoder.ReadNextFrame(buffer)!;

        Assert.Same(buffer, frame.Rgba);
        Assert.Equal((string)ExpectedFrames("edge_interlaced.gif")[0]!["sha256"]!, Hash(buffer));
    }

    [Fact]
    public void the_loop_count_is_read()
    {
        using var decoder = new GifDecoder(Path.Combine(Data, "ffmpeg_transparent_offsets.gif"));
        decoder.ReadNextFrame();

        Assert.Equal(0, decoder.LoopCount);
    }

    [Fact]
    public void a_file_that_is_no_gif_is_refused()
    {
        using var stream = new MemoryStream("PNG and then some"u8.ToArray());

        Assert.Throws<InvalidDataException>(() => new GifDecoder(stream, ownsStream: false));
    }

    // 4096 x 4096 RGBA is 64 MB a canvas; a header claiming more is refused rather than allocated.
    [Fact]
    public void a_canvas_too_large_to_hold_is_refused()
    {
        byte[] header = [(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0];
        using var stream = new MemoryStream(header);

        Assert.Throws<InvalidDataException>(() => new GifDecoder(stream, ownsStream: false));
    }
}
