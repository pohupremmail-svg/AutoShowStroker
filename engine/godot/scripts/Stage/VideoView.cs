using System;
using Godot;

namespace Gooner.Engine.Stage;

// A clip, through the FFmpeg extension (EIRTeam.FFmpeg), kept to its own aspect ratio the way the
// Python app's video item kept it. Reports the two things the player needs to hear: the clip ran to
// its end, or it will never play.
//
// The extension is reached by class name, so a build without it compiles and runs: Available is
// false, and every clip is reported unplayable.
//
// A backend that cannot open a file says so in the Qt app; this one mostly just shows nothing. So
// it is watched instead - no picture in its first seconds, or a picture that stops moving while the
// clip claims to play, counts as a file that will never finish.
public partial class VideoView : AspectRatioContainer
{
    private const string StreamClass = "FFmpegVideoStream";

    // Time for the first picture. Opening a 1440p clip took up to 0.2 s in phase 0.
    private const double FirstPictureSec = 3.0;

    // A clip whose position has not moved for this long while playing has stopped decoding.
    private const double StalledSec = 3.0;

    private readonly VideoStreamPlayer? _player;
    private double _waited;
    private double _stillFor;
    private double _lastPosition = -1;
    private bool _pictured;
    private bool _over;

    public static bool Available => ClassDB.ClassExists(StreamClass);

    public VideoView(string path, double volume, bool muted)
    {
        StretchMode = StretchModeEnum.Fit;
        AlignmentHorizontal = AlignmentMode.Center;
        AlignmentVertical = AlignmentMode.Center;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        if (!Available) return;
        var stream = ClassDB.Instantiate(StreamClass).As<VideoStream>();
        stream.File = path;
        Volume = volume;
        _player = new VideoStreamPlayer
        {
            Stream = stream,
            Expand = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        Muted = muted;
        _player.Finished += () =>
        {
            if (_over) return;
            Ended?.Invoke();
        };
        AddChild(_player);
    }

    public event Action? Ended;

    public event Action<string>? Failed;

    // vid_loudness, linear 0 to 1 as QAudioOutput took it.
    public double Volume { get; }

    public bool Muted
    {
        get => _player is { } player && player.VolumeDb <= -79;
        set
        {
            if (_player is null) return;
            // Godot's floor is -80 dB; anything at or below it is silence.
            _player.VolumeDb = value || Volume <= 0 ? -80 : Math.Max(-80, Mathf.LinearToDb((float)Volume));
        }
    }

    public override void _Ready()
    {
        if (_player is null)
        {
            Fail("there is no video decoder in this build");
            return;
        }
        _player.Play();
    }

    // From the top: the clip ended sooner than a clip may.
    public void Replay()
    {
        if (_player is null || _over) return;
        _lastPosition = -1;
        _stillFor = 0;
        _player.Play();
    }

    // Held on its frame, silent - what a stopped session leaves up.
    public void Freeze()
    {
        _over = true;
        if (_player is not null) _player.Paused = true;
    }

    public override void _Process(double delta)
    {
        if (_player is null || _over) return;
        if (!_pictured)
        {
            if (_player.GetVideoTexture() is { } texture && texture.GetWidth() > 0)
            {
                _pictured = true;
                Ratio = texture.GetWidth() / (float)Math.Max(1, texture.GetHeight());
            }
            else if ((_waited += delta) >= FirstPictureSec)
            {
                Fail("the decoder showed nothing");
            }
            return;
        }
        if (!_player.IsPlaying()) return;
        var position = _player.StreamPosition;
        _stillFor = position == _lastPosition ? _stillFor + delta : 0;
        _lastPosition = position;
        if (_stillFor >= StalledSec) Fail("the decoder stopped");
    }

    private void Fail(string reason)
    {
        _over = true;
        _player?.Stop();
        // Deferred: the player hears of it after it has finished loading the clip, as it would from
        // a real backend.
        Callable.From(() => Failed?.Invoke(reason)).CallDeferred();
    }
}
