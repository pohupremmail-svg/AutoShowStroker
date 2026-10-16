using System;
using System.Threading;
using Gooner.Core.Audio;
using Godot;

namespace Gooner.Engine.Audio;

// The sound of the rhythm and the clock it runs on - the one thing the phase 0 spike proved has to
// be done this way and no other:
//  1. The stream is filled from its own thread, never from a frame: a frame that hitches (a big
//     picture loading) must not be able to make the click late.
//  2. The clock is the frames the mixer has actually consumed, not where it says it is - an underrun
//     then delays the clock instead of breaking the rhythm against it.
// The click itself is Gooner.Core.Audio.ClickTrack; this only feeds it to Godot. Its clock is in
// seconds since the Unix epoch like every other clock in the app, so it can stand in for the wall
// clock everywhere: its origin is the wall clock at the moment the stream started.
public partial class SessionAudio : Node
{
    // The stream's buffer. Godot rounds it up to a power of two - 0.17 s at 48 kHz - and it is kept
    // only partly full: see AheadSec.
    private const float BufferSec = 0.1f;

    // How far ahead of the mixer the click is written, to begin with. Everything written is
    // committed, so this is how close to its moment a note can still be learned of and get its click
    // on time - the first note of a session is the one that comes that close. Kept full, the buffer
    // would put that at 0.17 s. Each time the stream runs dry the lead grows by AheadStepSec, so it
    // settles wherever the device needs it: a few steps for a sound card, the whole buffer for the
    // dummy driver of a headless run, which pulls in large blocks.
    private const double AheadSec = 0.08;
    private const double AheadStepSec = 0.01;

    private readonly Lock _lock = new();
    private AudioStreamPlayer? _player;
    private AudioStreamGeneratorPlayback? _playback;
    private int _capacity;
    private Thread? _thread;
    private int _aheadFrames;
    private int _skipsSeen;
    private volatile bool _running;

    // Null until the stream runs, and when there is no click file to play.
    public ClickTrack? Click { get; private set; }

    public int SampleRate { get; private set; }

    public void Begin(string clickFile)
    {
        SampleRate = (int)AudioServer.GetMixRate();
        var generator = new AudioStreamGenerator { MixRate = SampleRate, BufferLength = BufferSec };
        _player = new AudioStreamPlayer { Stream = generator };
        AddChild(_player);
        _player.Play();
        _playback = (AudioStreamGeneratorPlayback)_player.GetStreamPlayback();
        _capacity = _playback.GetFramesAvailable();
        var origin = (DateTimeOffset.UtcNow - DateTimeOffset.UnixEpoch).TotalSeconds;
        Click = new ClickTrack(WavClip.Load(clickFile, SampleRate), SampleRate, origin);
        _aheadFrames = (int)(AheadSec * SampleRate);
        _running = true;
        _thread = new Thread(Fill) { IsBackground = true, Name = "gooner-audio", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public override void _ExitTree() => Shutdown();

    // Stops the stream and lets go of it. Called on the way out, a mix cycle before the engine goes
    // down: the audio server frees a stopped playback on its next mix, and one still held at exit is
    // reported as a leak.
    public void Shutdown()
    {
        _running = false;
        _thread?.Join(200);
        _thread = null;
        lock (_lock)
        {
            _player?.Stop();
            _playback?.Dispose();
            _playback = null;
        }
    }

    // What the listener hears right now, on the session clock: frames the mixer has consumed,
    // carried on since its last mix, less the time the sound takes to reach the speakers.
    public double Heard()
    {
        if (Click is null || _playback is null) return (DateTimeOffset.UtcNow - DateTimeOffset.UnixEpoch).TotalSeconds;
        long consumed;
        lock (_lock)
            consumed = Click.FramesWritten - (_capacity - _playback.GetFramesAvailable());
        return Click.TimeAt(consumed) + AudioServer.GetTimeSinceLastMix() - AudioServer.GetOutputLatency();
    }

    // How far ahead of the mixer the click is being written now, in seconds.
    public double Lead
    {
        get
        {
            lock (_lock) return _aheadFrames / (double)SampleRate;
        }
    }

    // Times the stream ran dry. Zero in a healthy run.
    public int Underruns => _playback?.GetSkips() ?? 0;

    private void Fill()
    {
        var floats = Array.Empty<float>();
        var frames = Array.Empty<Vector2>();
        while (_running)
        {
            lock (_lock)
            {
                if (_playback is null) break;
                var skips = _playback.GetSkips();
                if (skips > _skipsSeen)
                {
                    _skipsSeen = skips;
                    _aheadFrames = Math.Min(_capacity, _aheadFrames + (int)(AheadStepSec * SampleRate));
                }
                var available = _playback.GetFramesAvailable();
                var n = Math.Min(available, _aheadFrames - (_capacity - available));
                if (n > 0)
                {
                    if (floats.Length < n * 2)
                    {
                        floats = new float[n * 2];
                        frames = new Vector2[n];
                    }
                    Click!.Mix(floats.AsSpan(0, n * 2));
                    for (var i = 0; i < n; i++)
                        frames[i] = new Vector2(floats[i * 2], floats[i * 2 + 1]);
                    _playback.PushBuffer(n == frames.Length ? frames : frames[..n]);
                }
            }
            Thread.Sleep(2);
        }
    }
}
