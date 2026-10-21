using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gooner.Core.App;
using Gooner.Core.Data;
using Gooner.Core.Formatting;
using Gooner.Core.Localization;
using Gooner.Core.Logging;
using Gooner.Core.Media;
using Gooner.Core.Migration;
using Gooner.Core.Panels;
using Gooner.Core.Settings;
using Gooner.Core.Timing;
using Gooner.Core.Track;
using Gooner.Engine.Audio;
using Gooner.Engine.Characters;
using Gooner.Engine.Fx;
using Gooner.Engine.Panels;
using Gooner.Engine.Stage;
using Gooner.Engine.Track;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine;

// The window: the picture filling it, with the effect layer over it - the note track in the style
// the look picks, the captions, the climax - and the controls on a dock of glass floating above.
// Between sessions the start screen takes the menu bar's place. Everything it shows is
// Gooner.Core.App.GoonerApp's state, synced once a frame; everything it does is a call into it.
//
// The frame, in order: the audio clock is read and the picture clock follows it, the app runs
// everything that came due, the climax reacts to what the banner now says, and the coming notes go
// to the click and the track alike.
//
//   -- --smoke            quits at once; exit code 0 when res/ was found.
//   -- --smoke-beat       plays a rhythm for a few seconds; 0 when every click went out on its own
//                         frame and every heard note had one. --long: a minute.
//   -- --smoke-session    plays a whole session over --media=<folder> for a few seconds; 0 when it
//                         ran, showed something and clicked on time. --screenshot=<png> saves the
//                         window at the end (not headless: there is nothing to grab there).
//                         --climax-at=<s> brings the climax that many seconds in; --stop presses
//                         Stop at the end, so the question is in the screenshot, and
//                         --answer=came|ruined|stopped|none answers it, so the recap is.
//                         --shots=N takes N more screenshots a tenth of a second apart.
//                         --say=<trigger> has a callout of that trigger (session_started,
//                         climax_real, ...) said 2.5 s before the end, so it is in the screenshot.
//                         --device=<ws://...> switches device output on against a server of your
//                         own (no consent question - a development run only).
//   -- --smoke-panel=<n>  opens one panel by name (see SmokePanels) and quits; with --screenshot.
//   -- --size=WxH         the window size of a smoke run (1280x800 otherwise).
//   -- --panel=<n>        with --smoke-session: opens that panel over the running session.
//                         The explorer's sample is built over --media=<folder>, scrubbed to
//                         --scrub=<s>. --wait=<s> waits that long before the screenshot (0.4).
//   -- --data-dir=<path>  where the history, settings and saved sessions go (see AppPaths).
public partial class Main : Control
{
    private const double SmokeSeconds = 8.0;

    private SessionAudio? _audio;
    private FrameClock? _clock;
    private readonly PictureClock _picture = new();
    private GoonerApp? _app;
    private MediaStage? _stage;
    private NoteTrackView? _track;
    private Particles _particles = null!;
    private readonly StageFilter _filter = new();
    private readonly Random _shake = new();
    private string _scheme = "";
    private string _language = "";
    private string? _character;
    private bool _wasMaximized;
    private int _hits;
    private double _smokeUntil;
    private string? _say;
    private string? _smoke;

    // Widgets synced from the app's state every frame.
    private Panel _background = null!;
    private Control _world = null!;
    private Vignette _vignette = null!;
    private ClimaxOverlay _climax = null!;
    private GlassChip _chase = null!, _clockChip = null!;
    private CalloutView _callout = null!;
    private TelepathyFrame? _telepathy;
    private double? _noteBefore, _lastNote, _nextNote;
    private string? _frameStyle;
    private RhythmBadge _rhythm = null!;
    private PanelContainer _outcome = null!;
    private Dock _dock = null!;
    private StartScreen _start = null!;
    private PanelHost _panels = null!;
    private CrashReporter? _crashes;

    public override void _Ready()
    {
        // Before anything that could fail: an exception nobody catches from here on is logged and
        // shown, and the app carries on (see CrashLogger).
        _crashes = new CrashReporter(ShowCrash);
        OS.AddLogger(new CrashLogger(_crashes));
        // Gone again at once, so not even a crash leaves it behind.
        RemoveGodotUserDir();
        // Closing the window goes through Quit, like every other way out.
        GetTree().AutoAcceptQuit = false;
        var res = AppPaths.Resources;
        if (AppPaths.UserFlag("smoke"))
        {
            GetTree().Quit(res is null ? 1 : 0);
            return;
        }
        Theme = UiTheme.Build();
        if (res is null)
        {
            AddChild(Caption("res/ not found."));
            return;
        }
        DisplayServer.WindowSetTitle("Auto Hero Generation");
        DisplayServer.WindowSetMinSize(new Vector2I(800, 500));
        // The window opens maximized (project.godot), as main.py showed it; a smoke run keeps the
        // fixed 1280x800 its screenshots were always taken at.
        if (OS.GetCmdlineUserArgs().Any(arg => arg.StartsWith("--smoke", StringComparison.Ordinal)))
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            // --size=WxH for a check at another size - a small laptop screen, say.
            var size = AppPaths.UserArg("size")?.Split('x') is [var w, var h] ? new Vector2I(int.Parse(w, System.Globalization.CultureInfo.InvariantCulture), int.Parse(h, System.Globalization.CultureInfo.InvariantCulture)) : new Vector2I(1280, 800);
            DisplayServer.WindowSetSize(size);
        }

        _audio = new SessionAudio();
        AddChild(_audio);
        _audio.Begin(Path.Combine(res, "mixkit-cool-interface-click-tone-2568.wav"));
        _clock = new FrameClock(_audio.Heard());

        var data = new UserDataStore(AppPaths.DataDir);
        // Before the stores are built - a JsonSettingsStore reads its file once - whatever the Python
        // app left is carried over (see AppPaths for when that is).
        var migration = ProfileMigration.Run(data, AppPaths.LegacySettings(), AppPaths.LegacyDataDir);
        _stage = new MediaStage();
        _app = new GoonerApp(_stage, _clock, new GoonerAppOptions
        {
            Settings = new JsonSettingsStore(data),
            DataStore = data,
            Migration = migration,
            CalloutDir = Path.Combine(res, "callouts"),
            LocaleDir = Path.Combine(res, "locales"),
            CharacterDir = Path.Combine(res, "characters"),
            Version = ReadVersion(res),
            DeviceOutput = true,
            // The device connection reports from its own thread; the window hears it on its own.
            Post = action => Callable.From(action).CallDeferred(),
        });
        _stage.Player = _app.Player;
        ApplyWindowIcon(res);
        _app.Beat.BeatHit += () => _hits++;
        _app.Panicked += () => DisplayServer.WindowSetMode(DisplayServer.WindowMode.Minimized);
        _app.StatisticsRequested += ShowRecap;
        _particles = new Particles();
        _track = new NoteTrackView(_app.Look, _particles, new Gooner.Core.Randomness.SystemRandomSource());
        _app.SessionStarted += _track.Reset;
        // The character is with the user through a session (TelepathyFrame) and comes closer for each
        // callout - not between sessions, and not when switched off.
        if (_app.Callouts is { } callouts)
        {
            callouts.CalloutSpoken += callout =>
            {
                if (_app.IsRunning && _app.Character.InSession) _telepathy?.Speak(callout);
            };
            callouts.TeaseHidden += () => _telepathy?.Close();
        }
        _app.SessionStarted += () =>
        {
            _noteBefore = _lastNote = _nextNote = null;
            if (_app.Character.InSession) _telepathy?.Open();
        };
        _app.SessionEnded += () => _telepathy?.Cut();
        _app.Panicked += () => _telepathy?.Cut();
        Build();
        // Full screen the first time, and after that as the window was last left; a smoke run keeps
        // the fixed window it is checked at.
        if (_app.Window.Fullscreen && !OS.GetCmdlineUserArgs().Any(arg => arg.StartsWith("--smoke", StringComparison.Ordinal)))
            EnterFullscreen();

        _smoke = AppPaths.UserFlag("smoke-beat") ? "beat" : AppPaths.UserFlag("smoke-session") ? "session" : null;
        if (_smoke is not null)
            // Started on the first frame, as a button press would start it: before that the clock has
            // not run, and the first note would be due before it was ever predicted.
            _smokeUntil = double.MaxValue;
        else if (AppPaths.UserArg("smoke-panel") is { } panel)
            Callable.From(() => SmokePanel(panel)).CallDeferred();
        else
            Callable.From(ShowWhatsNewOnStartup).CallDeferred();
    }

    // GoonerApp._apply_window_icon: the window and the taskbar show the app's icon, not Godot's.
    private static void ApplyWindowIcon(string res)
    {
        var icon = Path.Combine(res, "icons", "favicon.ico");
        if (!OperatingSystem.IsWindows()) return;
        if (File.Exists(icon)) DisplayServer.SetNativeIcon(icon);
        else AppLog.Warning("GoonerApp", "Window icon not found at {0}", icon);
    }

    private static string ReadVersion(string res)
    {
        var file = Path.Combine(Path.GetDirectoryName(res) ?? "", "VERSION");
        try
        {
            return File.ReadAllText(file).Trim();
        }
        catch (IOException)
        {
            return "unknown";
        }
    }

    // --- the frame ---

    public override void _Process(double delta)
    {
        if (_app is null || _audio?.Click is not { } click || _clock is null) return;
        _clock.Now = _picture.Next(_audio.Heard(), delta);
        if (_smokeUntil == double.MaxValue) BeginSmoke();
        _app.Frame();
        if (_scheme != _app.Look.ColorScheme) ApplyScheme();
        if (_frameStyle != _app.Look.TelepathyFrame && _telepathy is not null) BuildTelepathy();
        if (_language != L10n.Language || _character != _app.Character.Path) ApplyLanguage();

        var notes = _app.Beat.UpcomingNotes(NoteTrackView.LeadTimeSec);
        // The notes either side of now and one beyond each, for the character to move to.
        var now = _clock.Now;
        if (_nextNote is { } passed && passed <= now) (_noteBefore, _lastNote) = (_lastNote, passed);
        var ahead = notes.Where(n => n.Audible && n.At > now).Select(n => (double?)n.At).Take(2).ToList();
        _nextNote = ahead.ElementAtOrDefault(0);
        _telepathy?.Rhythm(now, _noteBefore, _lastNote, _nextNote, ahead.ElementAtOrDefault(1));
        click.Schedule(notes.Where(n => n.Audible).Select(n => n.At));
        click.Volume = (float)_app.Beat.BeatLoudness;
        click.Muted = _app.Beat.IsMuted;
        // Before the track gets its new forecast: a denial shatters what was still coming.
        _climax.Strength = (float)_app.Look.EffectScale;
        _climax.Sync(_app.Banner, _app.IsRunning);
        // The same moments the click was given, with the stretches of the plan they fall in.
        _track!.Show(_app.Beat.Forecast(NoteTrackView.LeadTimeSec), _clock.Now, delta);
        // Censoring round the Hypno Ring, wherever it has wandered; the clear circle breathes with the beat.
        var censor = _track.ShowsRing && _app.IsRunning ? _app.Look.Censor : null;
        _filter.Apply(censor, _track.Focus, _track.FocusRadius * 2.5f * (1 + 0.07f * _track.Flare), _climax.Aberration);
        _world.Position = _climax.Shake > 0
            ? new Vector2((float)(_shake.NextDouble() - 0.5) * _climax.Shake, (float)(_shake.NextDouble() - 0.5) * _climax.Shake)
            : Vector2.Zero;
        Place();
        Sync();

        if (_say is not null && _smokeUntil > 0 && _clock.Now >= _smokeUntil - 2.5)
        {
            _app.Callouts?.ForceOutputSentence(_say);
            _say = null;
        }
        if (_smokeUntil > 0 && _clock.Now >= _smokeUntil) EndSmoke(click);
    }

    // The app's state, onto the widgets.
    private void Sync()
    {
        var app = _app!;
        var running = app.IsRunning;
        _start.ShowScreen(!running);
        _dock.Visible = running;
        _dock.MayFade = running && !_panels.AnyOpen;
        _dock.Previous.Disabled = _dock.Next.Disabled = _dock.Stop.Disabled = !running;
        _dock.Edge.Visible = app.EdgeButtonVisible;
        _dock.Edge.Disabled = !app.EdgeButtonEnabled;
        _dock.Edge.Text = app.EdgeButtonText;
        _dock.ShowMuted(app.IsMuted);
        if (app.Intiface is { } intiface)
        {
            // Only while device output is on - it doubles as the disclosure that a connection is open.
            var device = intiface.Button;
            _dock.Device.Visible = device.Visible;
            _dock.Device.Text = device.Text;
            _dock.Device.Disabled = !device.Enabled;
            _dock.Device.TooltipText = device.Tooltip;
            _dock.Device.ThemeTypeVariation = device.Primary ? "EdgeButton" : "DockButton";
        }
        _outcome.Visible = app.OutcomeQuestion is not null;
        _callout.Show(app.Hud.CalloutText);
        _chase.Show(app.Hud.RecordChaseText);
        _clockChip.Show(app.Hud.SessionTimerText);
        SyncQuestion();
    }

    // Where the captions go: above the note track, wherever the look has put it - and below the
    // climax's word while it is up.
    private void Place()
    {
        var size = Size;
        var style = _app!.Look.NoteTrack;
        var stream = new StreamLane(size.X, size.Y);
        _callout.Place(_climax.Showing ? size.Y * 0.62f : style switch
        {
            "ring" => size.Y - 110,
            "stream" => stream.Top - 36,
            "tunnel" => size.Y * 0.52f,
            _ => size.Y * 0.43f,
        });
        _rhythm.At = style switch
        {
            "ring" => _track!.Focus,
            "stream" => new Vector2(stream.HitX + 150, stream.Top - 26),
            "tunnel" => new Vector2(size.X / 2, size.Y * 0.6f),
            _ => new Vector2(size.X / 2, size.Y * 0.47f - 24),
        };
        _outcome.Size = _outcome.GetCombinedMinimumSize();
        _outcome.Position = new Vector2((size.X - _outcome.Size.X) / 2, size.Y * 0.68f).Round();
    }

    // A change of colour scheme: the theme, the glass and the start screen in the new colours.
    private void ApplyScheme()
    {
        _scheme = _app!.Look.ColorScheme;
        Palette.Use(_scheme);
        Theme = UiTheme.Build();
        Glass.Refresh();
        _background.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = UiTheme.Of(Palette.SurfaceDarkest) });
        _start.Restyle();
        _dock.Restyle();
    }

    // A change of language, saved in the settings: what stays up between panels - the start screen,
    // the dock, the answers under the climax - is built again in it. A panel is worded when it opens,
    // and the settings close themselves on Save. Each goes back where it was in the drawing order.
    private void ApplyLanguage()
    {
        _language = L10n.Language;
        _character = _app?.Character.Path;
        _outcome = Replace(_outcome, BuildOutcome());
        _dock = Replace(_dock, BuildDock());
        _start = Replace(_start, BuildStart());
        BuildTelepathy();
        _stage?.Reword();
        Sync();
    }

    // The frame the character comes through in during a session, for the character picked; none
    // without one. Built again when the pick changes.
    private void BuildTelepathy()
    {
        var index = _telepathy?.GetIndex() ?? _callout.GetIndex() + 1;
        if (_telepathy is not null)
        {
            _world.RemoveChild(_telepathy);
            _telepathy.QueueFree();
            _telepathy = null;
        }
        if (_app?.Character.Path is not { } path) return;
        _frameStyle = _app.Look.TelepathyFrame;
        var style = _frameStyle == "crt" ? TelepathyFrame.Style.TubeScreen : TelepathyFrame.Style.Oval;
        _telepathy = new TelepathyFrame(path, style);
        _world.AddChild(_telepathy);
        _world.MoveChild(_telepathy, index);
        // Picked anew in the middle of a session: she is there at once.
        if (_app.IsRunning && _app.Character.InSession) _telepathy.Open();
    }

    private T Replace<T>(T old, T fresh) where T : Node
    {
        var index = old.GetIndex();
        RemoveChild(old);
        old.QueueFree();
        AddChild(fresh);
        MoveChild(fresh, index);
        return fresh;
    }

    // --- input ---

    // Panic comes first, before any control could take Space for itself: a focused button in a
    // panel would press itself instead. Only a field being typed in keeps it - there a space is
    // a space. Any movement of the pointer brings the dock back in full.
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion) _dock?.Wake();
        if (_app is null || @event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Space } key) return;
        if (key.CtrlPressed || key.ShiftPressed || key.AltPressed) return;
        if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit) return;
        _app.Panic();
        GetViewport().SetInputAsHandled();
    }

    // The keys of the Python window: F/F11 fullscreen, Esc out of it, M mute, the buttons' own
    // shortcuts - each only while its button could be pressed - and the old menus' (Ctrl+S, F1,
    // Ctrl+Q). While a panel is open the window behind it is as unreachable as it was behind a modal
    // dialog, and Esc dismisses the panel.
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (_app is null || @event is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (_panels.AnyOpen)
        {
            if (key.Keycode == Key.Escape) _panels.RejectTop();
            GetViewport().SetInputAsHandled();
            return;
        }
        var ctrl = key.CtrlPressed;
        var running = _app.IsRunning;
        switch (key.Keycode)
        {
            case Key.F or Key.F11:
                ToggleFullscreen();
                break;
            case Key.Escape:
                LeaveFullscreen();
                break;
            case Key.Space when ctrl:
                if (running) _app.Stop();
                break;
            case Key.M:
                _app.ToggleMute();
                break;
            case Key.Left when running:
                _app.Previous();
                break;
            case Key.Right when running:
                _app.Next();
                break;
            case Key.O when ctrl:
                OpenFolder();
                break;
            case Key.S when ctrl:
                ShowSettings();
                break;
            case Key.Q when ctrl:
                Quit(0);
                break;
            case Key.F1:
                _panels.Show(new GuidePanel());
                break;
            case Key.E when _app.EdgeButtonVisible && _app.EdgeButtonEnabled:
                _app.EdgeReached();
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    private void ToggleFullscreen()
    {
        if (DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen) LeaveFullscreen();
        else EnterFullscreen();
    }

    // Both remember the switch for the next start (WindowState).
    private void EnterFullscreen()
    {
        if (DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen) return;
        _wasMaximized = DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Maximized;
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
        _app?.Window.Remember(fullscreen: true);
    }

    private void LeaveFullscreen()
    {
        if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Fullscreen) return;
        DisplayServer.WindowSetMode(_wasMaximized ? DisplayServer.WindowMode.Maximized : DisplayServer.WindowMode.Windowed);
        _app?.Window.Remember(fullscreen: false);
    }

    // The folder picker; what it starts with goes to the playlist, counted - a folder is never logged.
    private void OpenFolder(IReadOnlyList<string>? folders = null)
    {
        var random = new Gooner.Core.Randomness.SystemRandomSource();
        var picker = new MediaFolderPicker(_app!.DataStore, random, folders);
        _panels.Show(new FolderPickerPanel(picker, files => _app.LoadPlaylist(files), random, _app.Scheduler));
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) Quit(0);
    }

    // The one way out: a session still running is recorded, the stream is stopped, and the engine
    // goes down once the audio server has had a mix cycle to let go of it.
    //
    // With a device connected the window goes first and the device's stop goes out behind it: in an
    // app with a panic key, off the screen is the part that has to be instant. The wait for the
    // connection to close is bounded - a server that never answers must not keep the app alive.
    private async void Quit(int exitCode)
    {
        _app?.Close();
        _audio?.Shutdown();
        if (_app?.Intiface?.Controller is { HasWorker: true } controller)
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Minimized);
            var waited = 0.0;
            while (controller.HasWorker && waited < 2.0)
            {
                await ToSignal(GetTree().CreateTimer(0.05), SceneTreeTimer.SignalName.Timeout);
                waited += 0.05;
            }
        }
        await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
        RemoveGodotUserDir();
        // A character still loading: Godot's import runs on in its own thread into the engine going
        // down, and that crashes. Everything of the app's own is written by now (the log line by line
        // as it goes), so the process ends here instead - at once, which a large model would not.
        if (Figures.Loading)
        {
            AppLog.Info("GoonerApp", "Quitting while a character is still loading: the process ends at once");
            OS.Kill(OS.GetProcessId());
        }
        Figures.Release();
        GetTree().Quit(exitCode);
    }

    // Godot makes %APPDATA%\Godot\app_userdata\GoonerApp at every start, before any of this code
    // runs; nothing is written there (no file log, no shader or pipeline cache - project.godot), so
    // it goes again, with Godot\app_userdata and Godot themselves when they hold nothing else.
    private static void RemoveGodotUserDir() => EmptyFolders.RemoveWithParents(OS.GetUserDataDir(), parents: 2);

    // --- smoke runs ---

    private void BeginSmoke()
    {
        var app = _app!;
        _smokeUntil = _clock!.Now + (AppPaths.UserFlag("long") ? 60.0 : SmokeSeconds);
        app.Beat.MinBeatFreq = 3.0;
        app.Beat.MaxBeatFreq = 5.0;
        // A plain rhythm: its first note comes a fifth of a second in at the earliest, so even that
        // one has to make its click on time.
        app.Beat.SelectedBeatPatterns = ["Standard Beat"];
        _say = AppPaths.UserArg("say");
        if (AppPaths.UserArg("climax-at") is { } climaxAt)
        {
            app.Climax.ClimaxActive = true;
            app.Climax.MinClimaxAfter = app.Climax.MaxClimaxAfter = double.Parse(climaxAt, System.Globalization.CultureInfo.InvariantCulture);
            app.Climax.ClimaxOnlyAfterRamp = false;
        }
        // A development run against a server of one's own - straight in, without the consent question
        // a user is always asked. Never reached without --device on the command line.
        if (AppPaths.UserArg("device") is { } device)
            app.Intiface?.Controller.Configure(true, device, 0.25, 0.75);
        if (_smoke == "session" && AppPaths.UserArg("media") is { } media)
        {
            app.Player.MinDur = app.Player.MaxDur = 1.5;
            app.LoadPlaylist(MediaKinds.FindSupportedFiles(media));
        }
        else
        {
            app.Start();
        }
        // --panel=<name> opens one of SmokePanels over the running session: the glass over a picture.
        if (AppPaths.UserArg("panel") is { } panel && SmokePanels().TryGetValue(panel, out var open)) open();
    }

    // Opens one panel, waits for it to fade in, saves the window and quits: 0 when the name was known.
    private async void SmokePanel(string name)
    {
        var known = SmokePanels().TryGetValue(name, out var open);
        open?.Invoke();
        await Screenshot();
        Quit(known ? 0 : 1);
    }

    private async System.Threading.Tasks.Task Screenshot()
    {
        if (AppPaths.UserArg("screenshot") is not { } shot) return;
        // Once whatever the last lines opened has faded in.
        var wait = AppPaths.UserArg("wait") is { } seconds ? double.Parse(seconds, System.Globalization.CultureInfo.InvariantCulture) : 0.4;
        await ToSignal(GetTree().CreateTimer(wait), SceneTreeTimer.SignalName.Timeout);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(shot);
        // --shots=N: N more, a tenth of a second apart (<shot>_1.png, ...), to see a movement through.
        var shots = AppPaths.UserArg("shots") is { } n ? int.Parse(n, System.Globalization.CultureInfo.InvariantCulture) : 0;
        for (var i = 1; i <= shots; i++)
        {
            await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.ChangeExtension(shot, null) + $"_{i}.png");
        }
    }

    private async void EndSmoke(Gooner.Core.Audio.ClickTrack click)
    {
        _smokeUntil = 0;
        var app = _app!;
        var shown = _stage!.Showing;
        var ok = click.ClicksLate == 0 && _hits > 10 && click.ClicksStarted - _hits is >= 0 and <= 2
            && (_smoke != "session" || (app.IsRunning && shown is "image" or "gif" or "video"));
        GD.Print($"smoke-{_smoke}: {_hits} notes, {click.ClicksStarted} clicks, {click.ClicksLate} late, " +
            $"{_audio!.Underruns} underruns, {_picture.Snaps} clock snaps, {click.SampleRate} Hz, lead {_audio.Lead * 1000:0} ms, " +
            $"showing {shown ?? "nothing"}");
        if (app.Intiface is { Controller.Enabled: true } intiface)
            GD.Print($"smoke-device: {intiface.Controller.Status}; device {(intiface.Controller.DeviceName.Length > 0 ? "found" : "none")}, " +
                $"sync {(intiface.Armed ? "armed" : "off")}");
        if (AppPaths.UserFlag("stop"))
        {
            app.Stop();
            // --answer=came|ruined|stopped|none answers its question, so the recap is on screen.
            if (AppPaths.UserArg("answer") is { } answer)
                app.AnswerHowItEnded(answer == "none" ? null : answer);
            Sync();
        }
        await Screenshot();
        Quit(ok ? 0 : 1);
    }

    // --- the layout ---

    private void Build()
    {
        var app = _app!;
        _scheme = app.Look.ColorScheme;
        _language = L10n.Language;
        _character = _app?.Character.Path;
        Palette.Use(_scheme);
        Theme = UiTheme.Build();
        _background = new Panel { AnchorRight = 1, AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore };
        _background.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = UiTheme.Of(Palette.SurfaceDarkest) });
        AddChild(_background);

        // The world: the picture and everything laid over it, shaken as one at the climax.
        _world = new Control { AnchorRight = 1, AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_world);
        _world.AddChild(_stage);
        _world.AddChild(_filter);
        _vignette = new Vignette();
        _world.AddChild(_vignette);
        _world.AddChild(_track);
        _clockChip = new GlassChip();
        _world.AddChild(_clockChip);
        _clockChip.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft, LayoutPresetMode.Minsize, 16);
        _chase = new GlassChip();
        _world.AddChild(_chase);
        _chase.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop, LayoutPresetMode.Minsize, 16);
        _chase.GrowHorizontal = GrowDirection.Both;
        _rhythm = new RhythmBadge();
        _world.AddChild(_rhythm);
        _callout = new CalloutView();
        _world.AddChild(_callout);
        BuildTelepathy();
        _climax = new ClimaxOverlay(_particles);
        _world.AddChild(_climax);
        _world.AddChild(_particles);

        _track!.RhythmArrived += _rhythm.Announce;
        _track.NoteLanded += strength => _vignette.Pulse(strength * 0.28f * (float)app.Look.EffectScale);
        _climax.Denied += _track.Shatter;

        _outcome = BuildOutcome();
        AddChild(_outcome);
        _dock = BuildDock();
        AddChild(_dock);
        _start = BuildStart();
        AddChild(_start);
        // Over everything, the start screen included.
        _panels = new PanelHost();
        AddChild(_panels);
        Sync();
    }

    // The answers to "what actually happened?", on glass under the climax. Styled alike on purpose: a
    // highlighted answer under a question whose only value is an honest one would be a recommendation.
    private PanelContainer BuildOutcome()
    {
        var card = new PanelContainer { Visible = false };
        Glass.Apply(card, 22, 14, opacity: Glass.CardOpacity);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        foreach (var (label, answer) in new[] { (L10n.T("I Came"), "came"), (L10n.T("I Ruined It"), "ruined"), (L10n.T("I Stopped"), "stopped") })
        {
            var button = Button(label, () => _app!.ReportOutcome(answer));
            button.CustomMinimumSize = new Vector2(150, 44);
            row.AddChild(button);
        }
        card.AddChild(row);
        return card;
    }

    private Dock BuildDock()
    {
        var app = _app!;
        var dock = new Dock { Visible = false };
        dock.Previous.Pressed += app.Previous;
        dock.Stop.Pressed += app.Stop;
        dock.Next.Pressed += app.Next;
        dock.Edge.Pressed += () => app.EdgeReached();
        dock.Mute.Pressed += app.ToggleMute;
        // The device's one control, beside Mute as on the Python side.
        dock.Device.Pressed += () => app.Intiface?.Press();
        dock.Folder.Pressed += () => OpenFolder();
        dock.Settings.Pressed += ShowSettings;
        dock.Menu.Pressed += ShowMenu;
        return dock;
    }

    // NoFocus, as the Qt side has it: a focused button takes Space for itself, and Space is Panic.
    private static Button Button(string text, Action pressed, string? tooltip = null)
    {
        var button = new Button { Text = text, FocusMode = FocusModeEnum.None, TooltipText = tooltip ?? "" };
        button.Pressed += pressed;
        return button;
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
