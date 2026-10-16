using System.Text;
using Gooner.Core.Audio;
using Gooner.Core.Tests.Support;

namespace Gooner.Core.Tests.Audio;

// The click file, read into what the click track mixes: interleaved stereo floats at the rate the
// audio output runs at. QSoundEffect did this on the Python side.
public sealed class WavClipTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("gooner-wav-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Wav(short[] samples, int channels = 2, int rate = 1000, int bits = 16, bool listChunkFirst = false)
    {
        using var data = new MemoryStream();
        using (var w = new BinaryWriter(data, Encoding.ASCII, leaveOpen: true))
            foreach (var s in samples) w.Write(s);
        var path = Path.Combine(_dir, $"{Guid.NewGuid():N}.wav");
        using var file = new BinaryWriter(File.Create(path), Encoding.ASCII);
        var list = listChunkFirst ? 12 : 0;
        file.Write("RIFF"u8);
        file.Write(36 + list + (int)data.Length);
        file.Write("WAVE"u8);
        file.Write("fmt "u8);
        file.Write(16);
        file.Write((short)1);
        file.Write((short)channels);
        file.Write(rate);
        file.Write(rate * channels * bits / 8);
        file.Write((short)(channels * bits / 8));
        file.Write((short)bits);
        if (listChunkFirst)
        {
            file.Write("LIST"u8);
            file.Write(4);
            file.Write("INFO"u8);
        }
        file.Write("data"u8);
        file.Write((int)data.Length);
        file.Write(data.ToArray());
        return path;
    }

    [Fact]
    public void stereo_pcm16_is_read_as_floats()
    {
        var clip = WavClip.Load(Wav([16384, -16384, 32767, 0]), targetRate: 1000);

        Assert.Equal([0.5f, -0.5f, 32767 / 32768f, 0f], clip);
    }

    [Fact]
    public void a_mono_file_plays_on_both_channels()
    {
        var clip = WavClip.Load(Wav([16384, -8192], channels: 1), targetRate: 1000);

        Assert.Equal([0.5f, 0.5f, -0.25f, -0.25f], clip);
    }

    [Fact]
    public void it_is_resampled_to_the_output_rate()
    {
        var clip = WavClip.Load(Wav([0, 16384], channels: 1), targetRate: 2000);

        Assert.Equal([0f, 0f, 0.25f, 0.25f, 0.5f, 0.5f, 0.5f, 0.5f], clip);
    }

    [Fact]
    public void chunks_before_the_data_are_skipped()
    {
        var clip = WavClip.Load(Wav([16384, 16384], listChunkFirst: true), targetRate: 1000);

        Assert.Equal([0.5f, 0.5f], clip);
    }

    [Fact]
    public void anything_but_pcm16_is_refused() =>
        Assert.Throws<InvalidDataException>(() => WavClip.Load(Wav([0, 0], bits: 8), targetRate: 1000));

    [Fact]
    public void a_file_that_is_no_wav_is_refused()
    {
        var path = Path.Combine(_dir, "nope.wav");
        File.WriteAllText(path, "not a wav at all, just text");

        Assert.Throws<InvalidDataException>(() => WavClip.Load(path, targetRate: 1000));
    }

    [Fact]
    public void the_apps_own_click_loads()
    {
        var clip = WavClip.Load(Repo.PathTo("res/mixkit-cool-interface-click-tone-2568.wav"), targetRate: 48000);

        // About a fifth of a second, stereo.
        Assert.InRange(clip.Length / 2 / 48000.0, 0.15, 0.25);
        Assert.Contains(clip, s => s != 0);
    }
}
