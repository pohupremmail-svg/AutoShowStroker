using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gooner.Core.App;
using Gooner.Core.Data;
using Gooner.Core.Formatting;
using Gooner.Core.Media;
using Gooner.Core.Migration;
using Gooner.Core.Panels;
using Gooner.Core.Settings;
using Gooner.Core.Timing;
using Gooner.Engine.Audio;
using Gooner.Engine.Panels;
using Gooner.Engine.Stage;
using Gooner.Engine.Track;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine;

// The window: the picture with its captions, the controls, and the footer with the banner, the
// answers and the note track - laid out as the Python app lays it out. Everything it shows is
// Gooner.Core.App.GoonerApp's state, synced once a frame; everything it does is a call into it.
//
// The frame, in order: the audio clock is read and the picture clock follows it, the app runs
// everything that came due, and the coming notes go to the click and the track alike.
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
//                         --device=<ws://...> switches device output on against a server of your
//                         own (no consent question - a development run only).
//   -- --smoke-panel=<n>  opens one panel by name (see SmokePanels) and quits; with --screenshot.
//                         The explorer's sample is built over --media=<folder>, scrubbed to
//                         --scrub=<s>. --wait=<s> waits that long before the screenshot (0.4).
//   -- --data-dir=<path>  where the history, settings and saved sessions go (see AppPaths).
public partial class Main : Control
{
    private const double SmokeSeconds = 8.0;
    private const int FooterHeight = 110;
    private const int OutcomeRowHeight = 38;
    private const int CalloutMargin = 8;

    private SessionAudio? _audio;
    private FrameClock? _clock;
    private readonly PictureClock _picture = new();
    private GoonerApp? _app;
    private MediaStage? _stage;
    private BeatTrack? _track;
    private bool _wasMaximized;
    private int _hits;
    private double _smokeUntil;
    private string? _smoke;

    // Widgets synced from the app's state every frame.
    private Button _prev = null!, _load = null!, _stop = null!, _next = null!, _edge = null!, _mute = null!, _device = null!;
    private HBoxContainer _controls = null!, _outcomeRow = null!;
    private Control _controlsRow = null!, _footer = null!;
    private PanelContainer _banner = null!, _callout = null!, _chase = null!, _clockCaption = null!;
    private Label _bannerText = null!, _calloutText = null!, _chaseText = null!, _clockText = null!;
    private MenuBar _menu = null!;
    private PanelHost _panels = null!;
    private CrashReporter? _crashes;

    public override void _Ready()
    {
        // Before anything that could fail: an exception nobody catches from here on is logged and
        // shown, and the app carries on (see CrashLogger).
        _crashes = new CrashReporter(ShowCrash);
        OS.AddLogger(new CrashLogger(_crashes));
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
            Version = ReadVersion(res),
            DeviceOutput = true,
            // The device connection reports from its own thread; the window hears it on its own.
            Post = action => Callable.From(action).CallDeferred(),
        });
        _stage.Player = _app.Player;
        _app.Beat.BeatHit += () => _hits++;
        _app.Panicked += () => DisplayServer.WindowSetMode(DisplayServer.WindowMode.Minimized);
        _app.StatisticsRequested += ShowRecap;
        _track = new BeatTrack(_app.Beat, () => _clock.Now);
        Build();

        _smoke = AppPaths.UserFlag("smoke-beat") ? "beat" : AppPaths.UserFlag("smoke-session") ? "session" : null;
        if (_smoke is not null)
            // Started on the first frame, as a button press would start it: before that the clock has
            // not run, and the first note would be due before it was ever predicted.
            _smokeUntil = double.MaxValue;
        else if (AppPaths.UserArg("smoke-panel") is { } panel)
            Callable.From(() => SmokePanel(panel)).CallDeferred();
        else if (_app.ShowStartupSplash)
            ShowSplash(ShowWhatsNewOnStartup);
        else
            Callable.From(ShowWhatsNewOnStartup).CallDeferred();
    }

    // Over everything, panels included; then whatever follows it.
    private void ShowSplash(Action then)
    {
        var splash = new Splash(AppPaths.Resources);
        splash.Finished += then;
        AddChild(splash);
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

        var notes = _app.Beat.UpcomingNotes(BeatTrack.LeadTimeSec);
        click.Schedule(notes.Where(n => n.Audible).Select(n => n.At));
        click.Volume = (float)_app.Beat.BeatLoudness;
        click.Muted = _app.Beat.IsMuted;
        _track!.Show(notes);
        Sync();

        if (_smokeUntil > 0 && _clock.Now >= _smokeUntil) EndSmoke(click);
    }

    // The app's state, onto the widgets.
    private void Sync()
    {
        var app = _app!;
        _prev.Disabled = _next.Disabled = _stop.Disabled = !app.IsRunning;
        _load.Text = app.LoadButtonText;
        _edge.Visible = app.EdgeButtonVisible;
        _edge.Disabled = !app.EdgeButtonEnabled;
        _edge.Text = app.EdgeButtonText;
        _mute.Text = app.IsMuted ? "Unmute" : "Mute";
        if (app.Intiface is { } intiface)
        {
            // Only while device output is on - it doubles as the disclosure that a connection is open.
            var device = intiface.Button;
            _device.Visible = device.Visible;
            _device.Text = device.Text;
            _device.Disabled = !device.Enabled;
            _device.TooltipText = device.Tooltip;
            _device.ThemeTypeVariation = device.Primary ? "PrimaryButton" : "";
        }

        _banner.Visible = app.Banner.Text is not null;
        if (app.Banner.Text is { } text)
        {
            _bannerText.Text = text;
            _banner.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Of(app.Banner.Color!), 12, 10));
        }
        // The footer grows for the question rather than squeezing the note track; the media area
        // above changes only then.
        _outcomeRow.Visible = app.OutcomeQuestion is not null;
        _footer.CustomMinimumSize = new Vector2(0, FooterHeight + (_outcomeRow.Visible ? OutcomeRowHeight : 0));

        if (_calloutText.Text != (app.Hud.CalloutText ?? ""))
            FitCallout(app.Hud.CalloutText);
        Caption(_callout, _calloutText, app.Hud.CalloutText);
        Caption(_chase, _chaseText, app.Hud.RecordChaseText);
        Caption(_clockCaption, _clockText, app.Hud.SessionTimerText);
        SyncQuestion();
        // A modal dialog blocked the window's menu shortcuts too.
        _menu.SetDisableShortcuts(_panels.AnyOpen);
    }

    // As wide as its text, up to nine tenths of the picture, then wrapped - what the QLabel in its
    // grid cell did. The panel is centred on the bottom edge and shrinks back to fit.
    private void FitCallout(string? text)
    {
        if (text is null) return;
        var font = _calloutText.GetThemeFont("font");
        var size = _calloutText.GetThemeFontSize("font_size");
        var available = _stage!.Size.X * 0.9f - 2 * CalloutMargin;
        var width = Math.Min(font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X + 2, available);
        _calloutText.CustomMinimumSize = new Vector2(Math.Max(1, width), 0);
        _calloutText.Text = text;
    }

    private static void Caption(PanelContainer panel, Label label, string? text)
    {
        panel.Visible = text is not null;
        label.Text = text ?? "";
    }

    // --- input ---

    // Panic comes first, before any control could take Space for itself: a focused button in a
    // panel would press itself instead. Only a field being typed in keeps it - there a space is
    // a space.
    public override void _Input(InputEvent @event)
    {
        if (_app is null || @event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Space } key) return;
        if (key.CtrlPressed || key.ShiftPressed || key.AltPressed) return;
        if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit) return;
        _app.Panic();
        GetViewport().SetInputAsHandled();
    }

    // The keys of the Python window: F/F11 fullscreen, Esc out of it, M mute, and the buttons' own
    // shortcuts - each only while its button could be pressed. While a panel is open the window
    // behind it is as unreachable as it was behind a modal dialog, and Esc dismisses the panel.
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
        switch (key.Keycode)
        {
            case Key.F or Key.F11:
                ToggleFullscreen();
                break;
            case Key.Escape:
                LeaveFullscreen();
                break;
            case Key.Space when ctrl:
                if (!_stop.Disabled) _app.Stop();
                break;
            case Key.M:
                _app.ToggleMute();
                break;
            case Key.Left when !_prev.Disabled:
                _app.Previous();
                break;
            case Key.Right when !_next.Disabled:
                _app.Next();
                break;
            case Key.O when ctrl:
                OpenFolder();
                break;
            case Key.E when _edge.Visible && !_edge.Disabled:
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

    private void EnterFullscreen()
    {
        if (DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen) return;
        _wasMaximized = DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Maximized;
        _controlsRow.Visible = false;
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
    }

    private void LeaveFullscreen()
    {
        if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Fullscreen) return;
        _controlsRow.Visible = true;
        DisplayServer.WindowSetMode(_wasMaximized ? DisplayServer.WindowMode.Maximized : DisplayServer.WindowMode.Windowed);
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
        GetTree().Quit(exitCode);
    }

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
        var background = new Panel { AnchorRight = 1, AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore };
        background.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = UiTheme.Of(Palette.Background) });
        AddChild(background);

        var root = new VBoxContainer { AnchorRight = 1, AnchorBottom = 1 };
        root.AddThemeConstantOverride("separation", 0);
        AddChild(root);
        _menu = BuildMenu();
        root.AddChild(_menu);

        // The picture, with the captions laid over it.
        var media = new Control { SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddChild(media);
        media.AddChild(_stage);
        (_callout, _calloutText) = CaptionPanel(24, 0.9f, 10, CalloutMargin);
        _calloutText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _calloutText.HorizontalAlignment = HorizontalAlignment.Center;
        // Bottom centre, sized by containers: the wrapped height follows the text by itself.
        var calloutArea = new MarginContainer { AnchorRight = 1, AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore };
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            calloutArea.AddThemeConstantOverride($"margin_{side}", 12);
        var calloutColumn = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.End, MouseFilter = MouseFilterEnum.Ignore };
        var calloutRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        calloutRow.AddChild(_callout);
        calloutColumn.AddChild(calloutRow);
        calloutArea.AddChild(calloutColumn);
        media.AddChild(calloutArea);
        (_chase, _chaseText) = CaptionPanel(13, 0.85f, 8, 6);
        media.AddChild(_chase);
        _chase.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight, LayoutPresetMode.Minsize, 8);
        _chase.GrowHorizontal = GrowDirection.Begin;
        (_clockCaption, _clockText) = CaptionPanel(13, 0.85f, 8, 6);
        media.AddChild(_clockCaption);
        _clockCaption.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft, LayoutPresetMode.Minsize, 8);

        // The controls.
        _controls = new HBoxContainer();
        var controlsMargin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            controlsMargin.AddThemeConstantOverride($"margin_{side}", 8);
        controlsMargin.AddChild(_controls);
        root.AddChild(controlsMargin);
        _controlsRow = controlsMargin;
        _prev = Button("<< Previous", app.Previous, "Left Arrow Key");
        _load = Button("Set Gooning Folder and Start.", () => OpenFolder(), "Ctrl+O");
        _load.ThemeTypeVariation = "PrimaryButton";
        _stop = Button("Stop", app.Stop, "Ctrl+Space");
        _next = Button("Skip >>", app.Next, "Right Arrow Key");
        _edge = Button(GoonerApp.EdgeButtonDefaultText, () => app.EdgeReached(), "E - a pause now, and a gentler beat behind it");
        _mute = Button("Mute", app.ToggleMute, "M");
        // The device's one control, beside Mute as on the Python side; NoFocus like the rest, or a
        // focused device button would swallow the panic key.
        _device = Button("No device", () => app.Intiface?.Press());
        _device.Visible = false;
        foreach (var button in new[] { _prev, _load, _stop, _next, _edge, _mute, _device })
        {
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _controls.AddChild(button);
        }

        // The footer: banner, answers, note track.
        _footer = new VBoxContainer { CustomMinimumSize = new Vector2(0, FooterHeight) };
        _footer.AddThemeConstantOverride("separation", 0);
        root.AddChild(_footer);
        _banner = new PanelContainer { Visible = false };
        _bannerText = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _bannerText.AddThemeFontSizeOverride("font_size", 28);
        _bannerText.AddThemeColorOverride("font_color", Colors.White);
        _banner.AddChild(_bannerText);
        _footer.AddChild(_banner);
        _outcomeRow = new HBoxContainer { CustomMinimumSize = new Vector2(0, OutcomeRowHeight), Visible = false };
        // Styled alike on purpose: a highlighted answer under a question whose only value is an
        // honest one would be a recommendation.
        foreach (var (label, answer) in new[] { ("I Came", "came"), ("I Ruined It", "ruined"), ("I Stopped", "stopped") })
        {
            var button = Button(label, () => app.ReportOutcome(answer));
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _outcomeRow.AddChild(button);
        }
        _footer.AddChild(_outcomeRow);
        _footer.AddChild(_track);

        // Over everything, the menu bar included.
        _panels = new PanelHost();
        AddChild(_panels);
        Sync();
    }

    private static (PanelContainer Panel, Label Text) CaptionPanel(int fontSize, float alpha, int radius, int margin)
    {
        var panel = new PanelContainer { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        var box = UiTheme.Box(new Color(45 / 255f, 29 / 255f, 58 / 255f, alpha), radius, margin);
        // The glow the Qt side drew as a drop shadow.
        box.ShadowColor = UiTheme.Of(Palette.Accent, 0.45f);
        box.ShadowSize = 9;
        panel.AddThemeStyleboxOverride("panel", box);
        var label = new Label { MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", UiTheme.Of(Palette.Accent));
        panel.AddChild(label);
        return (panel, label);
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
