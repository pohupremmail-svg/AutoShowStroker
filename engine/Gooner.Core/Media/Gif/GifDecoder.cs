using System.Text;

namespace Gooner.Core.Media.Gif;

// One composited frame: the full canvas as RGBA (Width * Height * 4), how long it stays up, and
// where it sits in the file.
public sealed record GifFrame(byte[] Rgba, int DelayMs, int Index);

// Streaming GIF decoder - pure C#, no engine dependency. Composites every frame onto a full-canvas
// RGBA buffer, honouring disposal, transparency, local palettes and interlacing. Written in phase 0
// because the FFmpeg the engine plays video with was built without a GIF decoder, and checked
// against FFmpeg itself since, frame by frame and pixel by pixel (GifDecoderTests). GIFs come from
// wherever the user found them, so it is deliberately forgiving - a truncated or slightly broken
// file yields the frames it has - and C# is the safer place to parse one anyway: nothing in here
// can read or write outside its own arrays.
public sealed class GifDecoder : IDisposable
{
    // 4096 x 4096 RGBA is 64 MB per canvas; anything larger is refused rather than allocated.
    public const long MaxCanvasPixels = 4096L * 4096L;
    // What browsers do: delays of 0 or 10 ms are treated as 100 ms.
    public const int MinDelayMs = 20;
    public const int DefaultDelayMs = 100;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public int LoopCount { get; private set; } = -1; // -1 = no NETSCAPE block, 0 = forever
    public bool Ended { get; private set; }
    public List<string> Warnings { get; } = [];

    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private long _dataStart;
    private byte[]? _globalPalette;
    private byte[] _canvas = [];
    private byte[]? _saved;
    private int _frameIndex;

    // Disposal still owed by the previous frame.
    private int _pendingDisposal;
    private int _pendingX, _pendingY, _pendingW, _pendingH;

    // Graphic control extension for the next image only.
    private int _gceDisposal;
    private int _gceDelayCs;
    private int _gceTransparent = -1;

    // LZW tables, reused across frames.
    private readonly ushort[] _prefix = new ushort[4096];
    private readonly byte[] _suffix = new byte[4096];
    private readonly byte[] _stack = new byte[4097];
    private byte[] _indices = Array.Empty<byte>();

    public GifDecoder(string path) : this(new BufferedStream(File.OpenRead(path), 1 << 16), true) { }

    public GifDecoder(Stream stream, bool ownsStream)
    {
        _stream = stream;
        _ownsStream = ownsStream;
        ReadHeader();
    }

    public void Dispose()
    {
        if (_ownsStream) _stream.Dispose();
    }

    private void ReadHeader()
    {
        var sig = ReadBytes(6);
        if (sig[0] != 'G' || sig[1] != 'I' || sig[2] != 'F') throw new InvalidDataException("not a GIF");
        Width = ReadU16();
        Height = ReadU16();
        var packed = ReadByte();
        ReadByte(); // background colour index: browsers ignore it and clear to transparent
        ReadByte(); // pixel aspect ratio
        if ((packed & 0x80) != 0) _globalPalette = ReadBytes(3 * (1 << ((packed & 7) + 1)));
        _dataStart = _stream.Position;
        if (Width > 0 && Height > 0) AllocateCanvas();
    }

    private void AllocateCanvas()
    {
        if ((long)Width * Height > MaxCanvasPixels) throw new InvalidDataException($"canvas {Width}x{Height} is too large");
        _canvas = new byte[Width * Height * 4];
    }

    // Back to the first frame, for looping. A file that has gone in the meantime ends instead.
    public void Rewind()
    {
        try
        {
            _stream.Position = _dataStart;
        }
        catch (IOException)
        {
            Warn("the file could not be read again");
            Ended = true;
            return;
        }
        Array.Clear(_canvas);
        _pendingDisposal = 0;
        _gceDisposal = 0;
        _gceDelayCs = 0;
        _gceTransparent = -1;
        _frameIndex = 0;
        Ended = false;
    }

    // The next composited frame, or null at the end of the file. With a reuse buffer of the
    // canvas size the frame is copied into it instead of into a fresh array - a player that
    // converts each frame right away produces no garbage that way.
    public GifFrame? ReadNextFrame(byte[]? reuse = null)
    {
        _reuse = reuse;
        if (Ended) return null;
        try
        {
            while (true)
            {
                var b = _stream.ReadByte();
                switch (b)
                {
                    case -1:
                    case 0x3B:
                        Ended = true;
                        return null;
                    case 0x21:
                        ReadExtension();
                        break;
                    case 0x2C:
                        return ReadImage();
                    case 0x00:
                        break; // stray padding some encoders leave behind
                    default:
                        Warn($"unexpected block 0x{b:X2}, stopping");
                        Ended = true;
                        return null;
                }
            }
        }
        catch (EndOfStreamException)
        {
            Warn("file ends early");
            Ended = true;
            return null;
        }
        // A file that goes while it plays - an unplugged drive, a deleted file - ends like one cut
        // short. The player decodes on a thread of its own, where anything thrown ends the process.
        catch (IOException)
        {
            Warn("the rest of the file could not be read");
            Ended = true;
            return null;
        }
    }

    private void ReadExtension()
    {
        var label = ReadByte();
        if (label == 0xF9)
        {
            var size = ReadByte();
            var block = ReadBytes(size);
            if (size >= 4)
            {
                _gceDisposal = (block[0] >> 2) & 7;
                _gceDelayCs = block[1] | (block[2] << 8);
                _gceTransparent = (block[0] & 1) != 0 ? block[3] : -1;
            }
            SkipSubBlocks();
        }
        else if (label == 0xFF)
        {
            var size = ReadByte();
            var id = Encoding.ASCII.GetString(ReadBytes(size));
            var isLoop = id.StartsWith("NETSCAPE2.0", StringComparison.Ordinal) || id.StartsWith("ANIMEXTS1.0", StringComparison.Ordinal);
            while (true)
            {
                var len = ReadByte();
                if (len == 0) break;
                var data = ReadBytes(len);
                if (isLoop && len >= 3 && data[0] == 1) LoopCount = data[1] | (data[2] << 8);
            }
        }
        else
        {
            SkipSubBlocks(); // comment, plain text, unknown
        }
    }

    private GifFrame ReadImage()
    {
        var left = ReadU16();
        var top = ReadU16();
        var w = ReadU16();
        var h = ReadU16();
        var packed = ReadByte();
        var palette = (packed & 0x80) != 0 ? ReadBytes(3 * (1 << ((packed & 7) + 1))) : _globalPalette;
        var interlaced = (packed & 0x40) != 0;

        if (_canvas.Length == 0)
        {
            // A logical screen of 0x0: size the canvas from the first image, as browsers do.
            Width = Math.Max(Width, left + w);
            Height = Math.Max(Height, top + h);
            AllocateCanvas();
        }
        if (palette == null)
        {
            Warn("image without any colour table");
            palette = DefaultPalette();
        }

        ApplyPendingDisposal();
        if (_gceDisposal == 3)
        {
            _saved ??= new byte[_canvas.Length];
            Buffer.BlockCopy(_canvas, 0, _saved, 0, _canvas.Length);
        }

        var count = (long)w * h;
        if (count > MaxCanvasPixels) throw new InvalidDataException($"image {w}x{h} is too large");
        if (_indices.Length < count) _indices = new byte[count];

        var truncated = false;
        int decoded;
        try
        {
            decoded = DecodeLzw((int)count);
        }
        catch (EndOfStreamException)
        {
            truncated = true;
            decoded = _lastDecoded;
        }

        Draw(left, top, w, h, decoded, palette, interlaced);

        _pendingDisposal = _gceDisposal;
        _pendingX = left; _pendingY = top; _pendingW = w; _pendingH = h;

        var delay = _gceDelayCs * 10;
        if (delay < MinDelayMs) delay = DefaultDelayMs;
        _gceDisposal = 0;
        _gceDelayCs = 0;
        _gceTransparent = -1;

        if (truncated)
        {
            Warn($"frame {_frameIndex} is cut off after {decoded} of {count} pixels");
            Ended = true;
        }
        byte[] pixels;
        if (_reuse != null && _reuse.Length == _canvas.Length)
        {
            Buffer.BlockCopy(_canvas, 0, _reuse, 0, _canvas.Length);
            pixels = _reuse;
        }
        else
        {
            pixels = (byte[])_canvas.Clone();
        }
        return new GifFrame(pixels, delay, _frameIndex++);
    }

    private byte[]? _reuse;

    private void ApplyPendingDisposal()
    {
        if (_pendingDisposal == 2)
            ClearRect(_pendingX, _pendingY, _pendingW, _pendingH);
        else if (_pendingDisposal == 3 && _saved != null)
            CopyRect(_saved, _canvas, _pendingX, _pendingY, _pendingW, _pendingH);
        _pendingDisposal = 0;
    }

    // Both take the part of the frame that is on the canvas. A frame may lie partly or wholly off it,
    // and its rows then started past the end of a canvas row - or of the canvas.
    private void ClearRect(int x0, int y0, int w, int h)
    {
        var x1 = Math.Min(Width, x0 + w);
        var y1 = Math.Min(Height, y0 + h);
        if (x0 >= x1) return;
        for (var y = y0; y < y1; y++)
            Array.Clear(_canvas, (y * Width + x0) * 4, (x1 - x0) * 4);
    }

    private void CopyRect(byte[] from, byte[] to, int x0, int y0, int w, int h)
    {
        var x1 = Math.Min(Width, x0 + w);
        var y1 = Math.Min(Height, y0 + h);
        if (x0 >= x1) return;
        for (var y = y0; y < y1; y++)
        {
            var o = (y * Width + x0) * 4;
            Buffer.BlockCopy(from, o, to, o, (x1 - x0) * 4);
        }
    }

    private void Draw(int left, int top, int w, int h, int decoded, byte[] palette, bool interlaced)
    {
        var colours = palette.Length / 3;
        var transparent = _gceTransparent;
        for (var i = 0; i < decoded; i++)
        {
            var row = i / w;
            var col = i - row * w;
            var y = top + (interlaced ? InterlacedRow(row, h) : row);
            var x = left + col;
            if (x >= Width || y >= Height) continue;
            int idx = _indices[i];
            if (idx == transparent || idx >= colours) continue;
            var o = (y * Width + x) * 4;
            var p = idx * 3;
            _canvas[o] = palette[p];
            _canvas[o + 1] = palette[p + 1];
            _canvas[o + 2] = palette[p + 2];
            _canvas[o + 3] = 255;
        }
    }

    // Row n of the stored data, in display order: pass 1 every 8th row from 0, pass 2 every
    // 8th from 4, pass 3 every 4th from 2, pass 4 every 2nd from 1.
    private static int InterlacedRow(int n, int h)
    {
        var p1 = (h + 7) / 8;
        if (n < p1) return n * 8;
        n -= p1;
        var p2 = (h + 3) / 8;
        if (n < p2) return 4 + n * 8;
        n -= p2;
        var p3 = (h + 1) / 4;
        if (n < p3) return 2 + n * 4;
        n -= p3;
        return 1 + n * 2;
    }

    private int _lastDecoded;

    private int DecodeLzw(int total)
    {
        _lastDecoded = 0;
        var minCodeSize = ReadByte();
        if (minCodeSize < 1 || minCodeSize > 11)
        {
            Warn($"invalid LZW code size {minCodeSize}");
            SkipSubBlocks();
            return 0;
        }
        var clear = 1 << minCodeSize;
        var eoi = clear + 1;
        var codeSize = minCodeSize + 1;
        var codeMask = (1 << codeSize) - 1;
        var next = clear + 2;
        for (var i = 0; i < clear; i++) { _suffix[i] = (byte)i; _prefix[i] = 0; }

        int datum = 0, bits = 0, blockLeft = 0, old = -1, pixels = 0;
        byte first = 0;
        var terminatorSeen = false;

        while (pixels < total)
        {
            while (bits < codeSize)
            {
                if (blockLeft == 0)
                {
                    blockLeft = ReadByte();
                    if (blockLeft == 0) { terminatorSeen = true; break; }
                }
                datum |= ReadByte() << bits;
                bits += 8;
                blockLeft--;
            }
            if (terminatorSeen) break;

            var code = datum & codeMask;
            datum >>= codeSize;
            bits -= codeSize;

            if (code == clear)
            {
                codeSize = minCodeSize + 1;
                codeMask = (1 << codeSize) - 1;
                next = clear + 2;
                old = -1;
                continue;
            }
            if (code == eoi) break;

            if (old == -1)
            {
                if (code > clear) { Warn("LZW stream starts with a non-literal code"); break; }
                _indices[pixels++] = (byte)code;
                _lastDecoded = pixels;
                old = code;
                first = (byte)code;
                continue;
            }

            var inCode = code;
            var sp = 0;
            if (code >= next)
            {
                if (code > next) { Warn("corrupt LZW code"); break; }
                _stack[sp++] = first;
                code = old;
            }
            while (code >= clear)
            {
                if (code >= 4096 || sp >= 4096) { Warn("corrupt LZW chain"); return pixels; }
                _stack[sp++] = _suffix[code];
                code = _prefix[code];
            }
            first = (byte)code;
            _stack[sp++] = first;

            if (next < 4096)
            {
                _prefix[next] = (ushort)old;
                _suffix[next] = first;
                next++;
                if (next == (1 << codeSize) && codeSize < 12)
                {
                    codeSize++;
                    codeMask = (1 << codeSize) - 1;
                }
            }
            old = inCode;

            while (sp > 0 && pixels < total) _indices[pixels++] = _stack[--sp];
            _lastDecoded = pixels;
        }

        if (pixels < total) Warn($"frame {_frameIndex}: {total - pixels} pixels missing from the data");
        // Skip what is left of this image's data.
        if (!terminatorSeen)
        {
            if (blockLeft > 0) ReadBytes(blockLeft);
            SkipSubBlocks();
        }
        return pixels;
    }

    private void SkipSubBlocks()
    {
        while (true)
        {
            var len = ReadByte();
            if (len == 0) return;
            ReadBytes(len);
        }
    }

    private static byte[] DefaultPalette()
    {
        var p = new byte[256 * 3];
        for (var i = 0; i < 256; i++) p[i * 3] = p[i * 3 + 1] = p[i * 3 + 2] = (byte)i;
        return p;
    }

    private void Warn(string message)
    {
        if (Warnings.Count < 50) Warnings.Add(message);
    }

    private int ReadByte()
    {
        var b = _stream.ReadByte();
        if (b < 0) throw new EndOfStreamException();
        return b;
    }

    private int ReadU16() => ReadByte() | (ReadByte() << 8);

    private byte[] ReadBytes(int n)
    {
        var buffer = new byte[n];
        var read = 0;
        while (read < n)
        {
            var r = _stream.Read(buffer, read, n - read);
            if (r <= 0) throw new EndOfStreamException();
            read += r;
        }
        return buffer;
    }
}
