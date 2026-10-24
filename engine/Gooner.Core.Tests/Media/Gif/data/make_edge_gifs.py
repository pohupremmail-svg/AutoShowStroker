"""Writes GIFs that exercise the corners of the format a decoder gets wrong: interlacing,
local palettes, every disposal mode, transparency, frames outside the canvas, zero delays,
a full LZW table without a clear code, a missing global palette, and a file cut off mid-frame.

Encoder written from the GIF89a spec so every byte is under control. FFmpeg decodes the same
files as the reference (make_expected.py), so a mistake here shows up as a disagreement, not as
a false pass. Test data for GifDecoderTests; deterministic, so running it again rewrites the same
bytes:

    python make_edge_gifs.py <this folder>
"""
import random
import struct
import sys
from pathlib import Path


def lzw_encode(indices, min_code_size, deferred_clear=False):
    clear = 1 << min_code_size
    eoi = clear + 1
    out = bytearray()
    acc = 0
    nbits = 0

    def emit(code, size):
        nonlocal acc, nbits
        acc |= code << nbits
        nbits += size
        while nbits >= 8:
            out.append(acc & 0xFF)
            acc >>= 8
            nbits -= 8

    def reset():
        return {(-1, i): i for i in range(clear)}, min_code_size + 1, eoi + 1

    table, size, next_code = reset()
    emit(clear, size)
    w = indices[0]
    for k in indices[1:]:
        if (w, k) in table:
            w = table[(w, k)]
            continue
        emit(w, size)
        if next_code < 4096:
            table[(w, k)] = next_code
            next_code += 1
            # The decoder widens one code later than it adds the entry, so widen once the
            # next free code no longer fits.
            if next_code > (1 << size) and size < 12:
                size += 1
        elif not deferred_clear:
            emit(clear, size)
            table, size, next_code = reset()
        w = k
    emit(w, size)
    # The decoder adds one more entry on reading that last code, and may widen before EOI.
    if next_code < 4096:
        next_code += 1
        if next_code > (1 << size) and size < 12:
            size += 1
    emit(eoi, size)
    if nbits:
        out.append(acc & 0xFF)
    return bytes(out)


def sub_blocks(data):
    out = bytearray()
    for i in range(0, len(data), 255):
        chunk = data[i:i + 255]
        out.append(len(chunk))
        out += chunk
    out.append(0)
    return bytes(out)


def palette_bytes(colours, bits):
    pal = bytearray()
    for i in range(1 << bits):
        r, g, b = colours[i] if i < len(colours) else (0, 0, 0)
        pal += bytes((r, g, b))
    return bytes(pal)


def interlace_order(rows):
    order = []
    for start, step in ((0, 8), (4, 8), (2, 4), (1, 2)):
        order += list(range(start, len(rows), step))
    return [rows[i] for i in order]


class Gif:
    def __init__(self, w, h, colours=None, bits=4, loop=0):
        self.out = bytearray(b"GIF89a")
        packed = 0
        if colours is not None:
            packed = 0x80 | ((bits - 1) << 4) | (bits - 1)
        self.out += struct.pack("<HHBBB", w, h, packed, 0, 0)
        if colours is not None:
            self.out += palette_bytes(colours, bits)
        if loop is not None:
            self.out += b"\x21\xFF\x0BNETSCAPE2.0\x03\x01" + struct.pack("<H", loop) + b"\x00"

    def comment(self, text):
        self.out += b"\x21\xFE" + sub_blocks(text.encode())

    def frame(self, x, y, w, h, pixels, delay_cs=10, disposal=1, transparent=None,
              local=None, local_bits=4, interlace=False, deferred_clear=False, min_code=None):
        packed = (disposal << 2) | (1 if transparent is not None else 0)
        self.out += b"\x21\xF9\x04" + struct.pack("<BHB", packed, delay_cs, transparent or 0) + b"\x00"
        ipacked = 0
        if local is not None:
            ipacked |= 0x80 | (local_bits - 1)
        if interlace:
            ipacked |= 0x40
        self.out += b"\x2C" + struct.pack("<HHHHB", x, y, w, h, ipacked)
        if local is not None:
            self.out += palette_bytes(local, local_bits)
        rows = [pixels[r * w:(r + 1) * w] for r in range(h)]
        if interlace:
            rows = interlace_order(rows)
        flat = [p for row in rows for p in row]
        mcs = min_code or max(2, local_bits if local is not None else 4)
        self.out.append(mcs)
        self.out += sub_blocks(lzw_encode(flat, mcs, deferred_clear))

    def bytes(self, trailer=True):
        return bytes(self.out) + (b"\x3B" if trailer else b"")


def pal16(seed):
    rnd = random.Random(seed)
    return [(rnd.randrange(256), rnd.randrange(256), rnd.randrange(256)) for _ in range(16)]


def stripes(w, h, n, phase=0):
    return [((x // 6 + y // 6 + phase) % n) for y in range(h) for x in range(w)]


def main(out_dir):
    out = Path(out_dir)
    out.mkdir(parents=True, exist_ok=True)
    W, H = 96, 72

    g = Gif(W, H, pal16(1))
    for f in range(4):
        g.frame(0, 0, W, H, stripes(W, H, 16, f), interlace=True)
    (out / "edge_interlaced.gif").write_bytes(g.bytes())

    g = Gif(W, H, pal16(2))
    for f in range(3):
        g.frame(0, 0, W, H, stripes(W, H, 16, f), local=pal16(10 + f))
    (out / "edge_local_palettes.gif").write_bytes(g.bytes())

    # Disposal 3: a box drawn over the background, then restored before the next box.
    g = Gif(W, H, pal16(3))
    g.frame(0, 0, W, H, stripes(W, H, 16), disposal=1)
    g.frame(10, 10, 30, 20, [5] * 600, disposal=3)
    g.frame(50, 30, 30, 20, [9] * 600, disposal=3)
    g.frame(20, 40, 20, 20, [12] * 400, disposal=1)
    (out / "edge_disposal_previous.gif").write_bytes(g.bytes())

    # Disposal 2: the area goes back to transparent, not to the background colour.
    g = Gif(W, H, pal16(4))
    g.frame(0, 0, W, H, stripes(W, H, 16), disposal=2)
    g.frame(10, 10, 40, 30, [3] * 1200, disposal=2)
    g.frame(30, 20, 40, 30, [7] * 1200, disposal=1)
    (out / "edge_disposal_background.gif").write_bytes(g.bytes())

    # Transparency over earlier frames: index 0 lets the previous frame show through.
    g = Gif(W, H, pal16(5))
    g.frame(0, 0, W, H, stripes(W, H, 16))
    holes = [0 if (x + y) % 3 else 8 for y in range(H) for x in range(W)]
    g.frame(0, 0, W, H, holes, transparent=0)
    g.frame(20, 20, 40, 30, [0 if x % 2 else 11 for x in range(1200)], transparent=0, disposal=2)
    (out / "edge_transparency.gif").write_bytes(g.bytes())

    # A frame hanging off the right and bottom edges must be clipped, not wrapped.
    g = Gif(W, H, pal16(6))
    g.frame(0, 0, W, H, stripes(W, H, 16))
    g.frame(W - 20, H - 15, 40, 30, [4] * 1200)
    (out / "edge_offscreen.gif").write_bytes(g.bytes())

    g = Gif(W, H, pal16(7))
    for f, d in enumerate((0, 1, 2, 5, 50)):
        g.frame(0, 0, W, H, stripes(W, H, 16, f), delay_cs=d)
    (out / "edge_delays.gif").write_bytes(g.bytes())

    g = Gif(W, H, colours=None)
    g.comment("no global colour table here")
    for f in range(2):
        g.frame(0, 0, W, H, stripes(W, H, 16, f), local=pal16(20 + f))
    (out / "edge_no_global_palette.gif").write_bytes(g.bytes())

    # 256 colours of noise: the LZW table fills up. One file clears, one keeps going at 12 bits.
    rnd = random.Random(8)
    big = [rnd.randrange(256) for _ in range(160 * 120)]
    pal256 = [(rnd.randrange(256), rnd.randrange(256), rnd.randrange(256)) for _ in range(256)]
    for name, deferred in (("edge_lzw_full_clear.gif", False), ("edge_lzw_deferred_clear.gif", True)):
        g = Gif(160, 120, pal256, bits=8)
        g.frame(0, 0, 160, 120, big, deferred_clear=deferred, min_code=8)
        g.frame(0, 0, 160, 120, big[::-1], deferred_clear=deferred, min_code=8)
        (out / name).write_bytes(g.bytes())

    # Cut off in the middle of the second frame's data.
    g = Gif(W, H, pal16(9))
    g.frame(0, 0, W, H, stripes(W, H, 16, 0))
    g.frame(0, 0, W, H, stripes(W, H, 16, 3))
    data = g.bytes()
    (out / "edge_truncated.gif").write_bytes(data[: len(data) - 120])

    print(f"written to {out}")


if __name__ == "__main__":
    main(sys.argv[1])
