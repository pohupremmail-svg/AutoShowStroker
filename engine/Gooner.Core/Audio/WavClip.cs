using System.Text;

namespace Gooner.Core.Audio;

// A short sound file read into what the click track mixes: interleaved stereo floats at the rate
// the audio output runs at. QSoundEffect did this on the Python side. Only what the click needs -
// uncompressed 16-bit PCM, mono or stereo - and anything else is refused rather than half-read.
public static class WavClip
{
    public static float[] Load(string path, int targetRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetRate);
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 12 || Tag(bytes, 0) != "RIFF" || Tag(bytes, 8) != "WAVE")
            throw new InvalidDataException("Not a WAV file.");

        int? channels = null, rate = null, bits = null, format = null;
        var pos = 12;
        while (pos + 8 <= bytes.Length)
        {
            var tag = Tag(bytes, pos);
            var size = BitConverter.ToInt32(bytes, pos + 4);
            var body = pos + 8;
            if (size < 0 || body + size > bytes.Length)
                throw new InvalidDataException("A WAV chunk runs past the end of the file.");
            if (tag == "fmt " && size >= 16)
            {
                format = BitConverter.ToInt16(bytes, body);
                channels = BitConverter.ToInt16(bytes, body + 2);
                rate = BitConverter.ToInt32(bytes, body + 4);
                bits = BitConverter.ToInt16(bytes, body + 14);
            }
            else if (tag == "data")
            {
                if (format != 1 || bits != 16 || channels is not (1 or 2) || rate is not > 0)
                    throw new InvalidDataException("Only 16-bit PCM WAV, mono or stereo, is supported.");
                return Resample(Decode(bytes, body, size, channels.Value), rate.Value, targetRate);
            }
            // Chunks are padded to an even length.
            pos = body + size + (size & 1);
        }
        throw new InvalidDataException("The WAV file has no sound data.");
    }

    private static string Tag(byte[] bytes, int at) => Encoding.ASCII.GetString(bytes, at, 4);

    private static float[] Decode(byte[] bytes, int start, int size, int channels)
    {
        var frames = size / (2 * channels);
        var stereo = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var at = start + i * 2 * channels;
            var left = BitConverter.ToInt16(bytes, at) / 32768f;
            var right = channels > 1 ? BitConverter.ToInt16(bytes, at + 2) / 32768f : left;
            stereo[i * 2] = left;
            stereo[i * 2 + 1] = right;
        }
        return stereo;
    }

    // Linear interpolation: a click is short and bright, and nobody hears the difference.
    private static float[] Resample(float[] stereo, int rate, int targetRate)
    {
        if (rate == targetRate) return stereo;
        var frames = stereo.Length / 2;
        var outFrames = (int)((long)frames * targetRate / rate);
        var output = new float[outFrames * 2];
        for (var i = 0; i < outFrames; i++)
        {
            var x = i * (double)rate / targetRate;
            var i0 = (int)x;
            var i1 = Math.Min(i0 + 1, frames - 1);
            var t = (float)(x - i0);
            for (var c = 0; c < 2; c++)
                output[i * 2 + c] = stereo[i0 * 2 + c] + (stereo[i1 * 2 + c] - stereo[i0 * 2 + c]) * t;
        }
        return output;
    }
}
