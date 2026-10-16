using Gooner.Core.Logging;
using Gooner.Core.Randomness;
using Gooner.Core.Sessions;
using Gooner.Core.Settings;
using Gooner.Core.Timing;

namespace Gooner.Core.Media;

// The part of the player that paints: the engine's stage. Each Show replaces whatever was on
// before it, stopping a clip or an animation that was running.
public interface IMediaStage
{
    void ShowImage(string path);

    void ShowGif(string path);

    void PlayVideo(string path, double volume);

    // The clip that just ended, from the top - it ended sooner than a clip may.
    void ReplayVideo();

    // A line where the picture goes: there is nothing to play.
    void ShowMessage(string text);

    void StopAll();

    void SetMuted(bool muted);
}

// What is on screen, and what replaces it. Port of src/PlaylistPlayer.py, minus the widgets: the
// playlist, the timer that decides how long the current medium stays up, and the way off a video
// that will never finish. The picture itself is the engine's, behind IMediaStage, which reports
// back when a clip ends (VideoEnded) or cannot play (VideoFailed).
//
// It knows two things about the session it serves and no more: whether one is running, and the
// script when it is a replay - a saved session records where each picture was cut, and that pacing
// is as much a part of it as the beats are.
//
// Privacy: a media path goes out on MediaShown for the in-memory session recorder and is never
// logged.
public sealed class PlaylistPlayer : ISettingsOwner
{
    // The timing settings were always stored under GoonerApp/, and they stay there, so no
    // installation has to migrate.
    public static string SettingsGroup => "GoonerApp";
    private const string Log = "PlaylistPlayer";

    // Delay before skipping past media that will never finish playing - long enough that an
    // entirely unplayable playlist cycles visibly instead of spinning.
    public const int MediaErrorAdvanceMs = 1000;

    public static IReadOnlyDictionary<string, object> Defaults { get; } = new Dictionary<string, object>
    {
        ["min_dur"] = 0.5,
        ["max_dur"] = 4.0,
        ["video_min_dur"] = 1.5,
        ["vid_loudness"] = 1.0,
    };

    private readonly IMediaStage _stage;
    private readonly IRandomSource _random;
    private readonly ClockScheduler _scheduler;
    private List<string> _playlist = [];
    private bool _active;
    private double _videoStartTime;

    public PlaylistPlayer(IMediaStage stage, ISettingsStore? settings, IRandomSource? random, ClockScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(scheduler);
        _stage = stage;
        _random = random ?? new SystemRandomSource();
        _scheduler = scheduler;
        MinDur = Read(settings, "min_dur");
        MaxDur = Read(settings, "max_dur");
        VideoMinDur = Read(settings, "video_min_dur");
        VidLoudness = Read(settings, "vid_loudness");
        AutoPlayTimer = scheduler.CreateTimer(ShowNext);
    }

    private static double Read(ISettingsStore? settings, string name)
    {
        var fallback = (double)Defaults[name];
        return settings?.GetDouble($"{SettingsGroup}/{name}", fallback) ?? fallback;
    }

    // A path, on its way to the screen. In memory only - see the class comment.
    public event Action<string>? MediaShown;

    public double MinDur { get; set; }
    public double MaxDur { get; set; }
    public double VideoMinDur { get; set; }
    public double VidLoudness { get; set; }

    public IReadOnlyList<string> Playlist => _playlist;
    public int CurrentIndex { get; set; }

    // Set while replaying a saved session. Null for a live one.
    public SessionScript? Script { get; set; }

    public ClockTimer AutoPlayTimer { get; }

    // --- session lifecycle ---

    public void SessionStarted() => _active = true;

    // Everything stops. Playback was once left running here on the Python side, and the clip kept
    // going (with sound) behind the statistics, its end then restarting the whole slideshow with no
    // session, no beat and the controls greyed out.
    public void SessionEnded()
    {
        _active = false;
        AutoPlayTimer.Stop();
        _stage.StopAll();
    }

    // Takes a fresh, already-shuffled playlist and rewinds to its start.
    public void SetPlaylist(IEnumerable<string> files)
    {
        _playlist = [.. files];
        CurrentIndex = 0;
    }

    // Silences the clip that is playing. The rhythm is muted separately.
    public void SetMuted(bool muted) => _stage.SetMuted(muted);

    public void ShowMessage(string text) => _stage.ShowMessage(text);

    // --- moving through the playlist ---

    public void ShowNext()
    {
        if (_playlist.Count == 0) return;
        CurrentIndex = (CurrentIndex + 1) % _playlist.Count;
        LoadCurrent();
    }

    public void ShowPrev()
    {
        if (_playlist.Count == 0) return;
        CurrentIndex = (CurrentIndex - 1 + _playlist.Count) % _playlist.Count;
        LoadCurrent();
    }

    public void LoadCurrent()
    {
        if (Script?.NextMediaPath() is { } scripted)
        {
            ShowMediaPath(scripted);
            return;
        }
        if (_playlist.Count == 0) return;
        ShowMediaPath(_playlist[CurrentIndex]);
    }

    private void ShowMediaPath(string path)
    {
        MediaShown?.Invoke(path);
        LoadMedia(path);
    }

    // How long the medium now on screen stays up. A replay takes the recorded gap; once the script
    // runs out the settings take over again.
    public void RecalcAutoplayTimer()
    {
        if (Script?.NextMediaGap() is { } scripted)
        {
            AutoPlayTimer.Start((int)(scripted * 1000));
            return;
        }
        AutoPlayTimer.Start((int)(_random.Uniform(MinDur, MaxDur) * 1000));
    }

    public void LoadMedia(string path)
    {
        switch (MediaKinds.Of(path))
        {
            case MediaKind.Video:
                // Live, a clip runs to its end and is not on the autoplay timer at all. In a replay
                // the recorded gap wins instead: the saved session says where this clip was cut.
                if (Script is null)
                    AutoPlayTimer.Stop();
                else
                    RecalcAutoplayTimer();
                _stage.PlayVideo(path, VidLoudness);
                _videoStartTime = _scheduler.Now;
                break;
            case MediaKind.Gif:
                _stage.ShowGif(path);
                RecalcAutoplayTimer();
                break;
            case MediaKind.Image:
                _stage.ShowImage(path);
                RecalcAutoplayTimer();
                break;
            case MediaKind.Unknown:
                break;
        }
    }

    // --- video that will not play ---

    // The stage's clip ran to its end.
    public void VideoEnded()
    {
        // An end can still land just after the session ended - advancing then would restart the
        // slideshow with no session behind it.
        if (!_active) return;
        if (_scheduler.Now - _videoStartTime < VideoMinDur)
        {
            _stage.ReplayVideo();
            return;
        }
        ShowNext();
    }

    // The stage could not play the clip: an exotic codec, a deleted file, an unplugged drive. No end
    // ever arrives for one of those and the autoplay timer is off for video, so the session used to
    // sit on a black frame until somebody pressed an arrow key. Advancing on a timer rather than at
    // once bounds the damage to one file a second if the whole playlist turns out to be unplayable.
    public void VideoFailed(string reason)
    {
        if (!_active) return;
        AppLog.Warning(Log, "Skipping unplayable media: {0}", reason);
        AutoPlayTimer.Start(MediaErrorAdvanceMs);
    }
}
