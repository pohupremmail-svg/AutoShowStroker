using System.IO;
using System.Linq;
using Gooner.Core.Beat;
using Gooner.Core.Data;
using Gooner.Core.Timing;
using Gooner.Engine.Audio;
using Godot;

namespace Gooner.Engine;

// The window. For now it proves the halves are joined: the scene loads, Gooner.Core is reachable,
// res/ is found on disk, and a rhythm plays its click on the audio clock.
//
//   -- --smoke        quits straight away; exit code 0 when res/ was found, 1 when it was not.
//   -- --smoke-beat   plays a rhythm for a few seconds and quits; exit code 0 when every click went
//                     out on its own frame and every note heard had its click. --long: a minute.
// Both are what a headless check runs (see engine/CLAUDE.md).
public partial class Main : Control
{
    private static readonly double SmokeBeatSeconds = OS.GetCmdlineUserArgs().Contains("--long") ? 60.0 : 8.0;

    private SessionAudio? _audio;
    private FrameClock? _clock;
    private readonly PictureClock _picture = new();
    private BeatHandler? _beat;
    private double _smokeUntil;
    private int _hits;

    public override void _Ready()
    {
        // Closing the window goes through Quit, like every other way out.
        GetTree().AutoAcceptQuit = false;
        var res = AppPaths.Resources;
        var args = OS.GetCmdlineUserArgs();
        if (args.Contains("--smoke"))
        {
            GetTree().Quit(res is null ? 1 : 0);
            return;
        }
        if (res is null)
        {
            AddChild(Caption("res/ not found."));
            return;
        }

        _audio = new SessionAudio();
        AddChild(_audio);
        _audio.Begin(Path.Combine(res, "mixkit-cool-interface-click-tone-2568.wav"));
        _clock = new FrameClock(_audio.Heard());
        _beat = new BeatHandler(scheduler: new ClockScheduler(_clock));
        _beat.BeatHit += () => _hits++;
        AddChild(Caption("GoonerApp"));
        if (args.Contains("--smoke-beat"))
        {
            _beat.MinBeatFreq = 3.0;
            _beat.MaxBeatFreq = 5.0;
            // A plain rhythm: its first note comes a fifth of a second in at the earliest, so even
            // that one has to make its click on time.
            _beat.SelectedBeatPatterns = ["Standard Beat"];
            // Started on the first frame, as a button press would start it: before that the clock
            // has not run, and the first note would be due before it was ever predicted.
            _smokeUntil = double.MaxValue;
        }
    }

    public override void _Process(double delta)
    {
        if (_audio?.Click is not { } click || _clock is null || _beat is null) return;
        _clock.Now = _picture.Next(_audio.Heard(), delta);
        if (_smokeUntil == double.MaxValue)
        {
            _beat.StartBeat();
            _smokeUntil = _clock.Now + SmokeBeatSeconds;
        }
        _beat.Advance();
        click.Schedule(_beat.UpcomingNotes(2.5).Where(n => n.Audible).Select(n => n.At));

        if (_smokeUntil > 0 && _clock.Now >= _smokeUntil)
        {
            _beat.Stop();
            // A click goes out a little ahead of its note, so one or two may be out ahead of the
            // last heard note when this stops.
            var ok = click.ClicksLate == 0 && _hits > 10 && click.ClicksStarted - _hits is >= 0 and <= 2;
            GD.Print($"smoke-beat: {_hits} notes, {click.ClicksStarted} clicks, {click.ClicksLate} late, " +
                $"{_audio.Underruns} underruns, {_picture.Snaps} clock snaps, {click.SampleRate} Hz, lead {_audio.Lead * 1000:0} ms");
            _smokeUntil = 0;
            Quit(ok ? 0 : 1);
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) Quit(0);
    }

    // The one way out: the stream is stopped first, and the engine goes down once the audio server
    // has had a mix cycle to let go of it.
    private async void Quit(int exitCode)
    {
        _audio?.Shutdown();
        await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
        GetTree().Quit(exitCode);
    }

    private static Label Caption(string text) => new()
    {
        Text = text,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        AnchorRight = 1,
        AnchorBottom = 1,
    };
}

// Where the engine finds what it reads from disk.
public static class AppPaths
{
    // res/ beside the executable once exported; from the editor or a dev run, the repo's own,
    // found by walking up from the Godot project.
    public static string? Resources { get; } = ResourceRoot.Find([
        System.IO.Path.GetDirectoryName(OS.GetExecutablePath()) ?? "",
        ProjectSettings.GlobalizePath("res://"),
    ]);
}
