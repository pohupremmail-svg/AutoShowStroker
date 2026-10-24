"""Writes expected.json: every frame of every GIF in this folder as FFmpeg decodes it.

FFmpeg is the oracle for Gooner.Core's GIF decoder (GifDecoderTests), the way the Python app is
the oracle for the rest of the core - and only that: nothing that ships uses it. Each frame is
recorded as the SHA-256 of its RGBA canvas, with the colour of fully transparent pixels zeroed
first (what colour an invisible pixel has is nobody's business, and the two decoders do not
agree on it), plus its delay.

    python make_expected.py <ffmpeg> <ffprobe>

Run it again only when a GIF here changes; the output is checked in.
"""
import hashlib
import json
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent


def run(*args):
    return subprocess.run(args, check=True, capture_output=True).stdout


def frames_of(gif, ffmpeg, ffprobe):
    size = run(ffprobe, "-v", "error", "-select_streams", "v:0", "-show_entries",
               "stream=width,height", "-of", "csv=p=0", str(gif)).decode().strip()
    width, height = (int(n) for n in size.split(","))
    raw = run(ffmpeg, "-v", "error", "-i", str(gif), "-f", "rawvideo", "-pix_fmt", "rgba",
              "-fps_mode", "passthrough", "-")
    durations = run(ffprobe, "-v", "error", "-select_streams", "v:0", "-show_entries",
                    "packet=duration_time", "-of", "csv=p=0", str(gif)).decode().split()
    frame_bytes = width * height * 4
    frames = []
    for index in range(len(raw) // frame_bytes):
        canvas = bytearray(raw[index * frame_bytes:(index + 1) * frame_bytes])
        for p in range(0, frame_bytes, 4):
            if canvas[p + 3] == 0:
                canvas[p:p + 3] = b"\0\0\0"
        delay = round(float(durations[index]) * 1000) if index < len(durations) else None
        frames.append({"sha256": hashlib.sha256(canvas).hexdigest(), "delay_ms": delay})
    return {"width": width, "height": height, "frames": frames}


def main(ffmpeg, ffprobe):
    expected = {gif.name: frames_of(gif, ffmpeg, ffprobe) for gif in sorted(HERE.glob("*.gif"))}
    (HERE / "expected.json").write_text(json.dumps(expected, indent=2) + "\n", encoding="utf-8")
    print(f"{len(expected)} files, {sum(len(e['frames']) for e in expected.values())} frames")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
