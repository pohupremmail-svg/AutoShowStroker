namespace Gooner.Core.Audio;

// The click, mixed into the audio stream at the exact frame its note is due on. The main thread
// hands over what the rhythm is about to play (Schedule, every frame); the audio thread mixes it
// block by block, ahead of what is being heard (Mix). On the Python side the click played whenever
// a QTimer got round to it, on the thread that also painted; here a hitch in the picture cannot
// move it.
//
// Times are on the session clock, in seconds; `origin` is the session time of frame 0, so the frame
// a note sounds on is (t - origin) * rate. Thread-safe: Schedule and Mix may run on different
// threads.
public sealed class ClickTrack
{
    // A note learned of after its frame was written still plays, at once - but not this late: after
    // a stall it would be a beat nobody asked for.
    public const double LateLimitSec = 0.25;

    // How long a started note is remembered, so a prediction that names it again does not start it
    // twice. Predictions run from what is being heard, a fraction of a second behind what is written.
    private const double RememberSec = 5.0;

    private readonly Lock _lock = new();
    private readonly float[] _click;
    private readonly int _clickFrames;
    private readonly List<long> _pending = [];
    private readonly List<long> _sounding = [];
    private readonly List<long> _started = [];
    private float _volume = 1.0f;
    private bool _muted;

    // `click` is interleaved stereo at `sampleRate`; see Stereo() for a mono one.
    public ClickTrack(float[] click, int sampleRate, double origin)
    {
        ArgumentNullException.ThrowIfNull(click);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        _click = click;
        _clickFrames = click.Length / 2;
        SampleRate = sampleRate;
        Origin = origin;
    }

    public int SampleRate { get; }

    public double Origin { get; }

    public long FramesWritten
    {
        get
        {
            lock (_lock) return _framesWritten;
        }
    }

    private long _framesWritten;
    private int _clicksStarted;
    private int _clicksLate;

    // Clicks that went out, and how many of those could not go out on their own frame - what a
    // check of the running engine reads. A late one means a note was predicted too close to its
    // moment.
    public int ClicksStarted
    {
        get
        {
            lock (_lock) return _clicksStarted;
        }
    }

    public int ClicksLate
    {
        get
        {
            lock (_lock) return _clicksLate;
        }
    }

    // beat_loudness: linear, 0 to 1, as QSoundEffect took it.
    public float Volume
    {
        get
        {
            lock (_lock) return _volume;
        }
        set
        {
            lock (_lock) _volume = value;
        }
    }

    // A note due while muted is silenced, not postponed: unmuting never lets a held-back click out.
    public bool Muted
    {
        get
        {
            lock (_lock) return _muted;
        }
        set
        {
            lock (_lock) _muted = value;
        }
    }

    public long FrameAt(double time) => (long)Math.Round((time - Origin) * SampleRate);

    public double TimeAt(long frame) => Origin + frame / (double)SampleRate;

    // What the rhythm is about to play, replacing everything not yet started: a note the rhythm no
    // longer plays (an edge, a stop) is dropped, one already sounding plays out.
    public void Schedule(IEnumerable<double> times)
    {
        ArgumentNullException.ThrowIfNull(times);
        var frames = times.Select(FrameAt).Distinct().Order().ToList();
        lock (_lock)
        {
            _pending.Clear();
            var tooLate = _framesWritten - (long)(LateLimitSec * SampleRate);
            foreach (var frame in frames)
                if (frame >= tooLate && _started.BinarySearch(frame) < 0)
                    _pending.Add(frame);
        }
    }

    // Writes the next block: interleaved stereo, overwritten rather than added to.
    public void Mix(Span<float> block)
    {
        var frames = block.Length / 2;
        lock (_lock)
        {
            for (var i = 0; i < frames; i++)
            {
                var frame = _framesWritten + i;
                while (_pending.Count > 0 && _pending[0] <= frame)
                {
                    _started.Add(_pending[0]);
                    _clicksStarted++;
                    if (_pending[0] < frame) _clicksLate++;
                    _pending.RemoveAt(0);
                    // From here: on its own frame, or now if it was learned of too late for that.
                    if (!_muted) _sounding.Add(frame);
                }
                float left = 0, right = 0;
                for (var c = _sounding.Count - 1; c >= 0; c--)
                {
                    var offset = frame - _sounding[c];
                    if (offset >= _clickFrames)
                    {
                        _sounding.RemoveAt(c);
                        continue;
                    }
                    left += _click[offset * 2];
                    right += _click[offset * 2 + 1];
                }
                block[i * 2] = left * _volume;
                block[i * 2 + 1] = right * _volume;
            }
            _framesWritten += frames;
            var forget = _framesWritten - (long)(RememberSec * SampleRate);
            var stale = 0;
            while (stale < _started.Count && _started[stale] < forget) stale++;
            _started.RemoveRange(0, stale);
        }
    }

    public static float[] Stereo(float[] mono)
    {
        ArgumentNullException.ThrowIfNull(mono);
        var stereo = new float[mono.Length * 2];
        for (var i = 0; i < mono.Length; i++)
            stereo[i * 2] = stereo[i * 2 + 1] = mono[i];
        return stereo;
    }
}
