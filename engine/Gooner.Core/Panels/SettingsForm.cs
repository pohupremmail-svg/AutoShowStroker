using System.Text.Json.Nodes;
using Gooner.Core.App;
using Gooner.Core.Beat;
using Gooner.Core.Callouts;
using Gooner.Core.Characters;
using Gooner.Core.Climax;
using Gooner.Core.Devices;
using Gooner.Core.Localization;
using Gooner.Core.Logging;
using Gooner.Core.Media;

namespace Gooner.Core.Panels;

// A number the settings show as a slider whose value can be typed. Its value is a draft until the form
// is applied, and is held the way QDoubleSpinBox held it: inside its range, rounded to its decimals.
public sealed class NumberSetting
{
    private readonly Func<double> _read;
    private readonly Action<double> _write;
    private double _value;

    internal NumberSetting(string name, string label, string group, bool isInt, double min, double max, double step,
        IReadOnlyDictionary<string, object> defaults, Func<double> read, Action<double> write, ValueUnit unit = ValueUnit.Plain)
    {
        Name = name;
        Label = label;
        Group = group;
        IsInt = isInt;
        Min = min;
        Max = max;
        Step = step;
        Decimals = DecimalsFor(step);
        Default = Convert.ToDouble(defaults[name], System.Globalization.CultureInfo.InvariantCulture);
        Slider = new SliderSpec(min, max, step, unit);
        _read = read;
        _write = write;
        Value = read();
    }

    public string Name { get; }
    public string Label { get; }
    public string Group { get; }
    public bool IsInt { get; }
    public double Min { get; }
    public double Max { get; }
    public double Step { get; }
    public int Decimals { get; }
    public double Default { get; }

    // Its slider: where a value sits, what the badge says, what a typed value means.
    public SliderSpec Slider { get; }

    public double Value
    {
        get => _value;
        set => _value = Math.Round(Math.Clamp(value, Min, Max), Decimals);
    }

    // Enough decimal places to represent the step exactly, with Qt's default of 2 as the floor so
    // the ordinary 0.1/0.01 fields keep looking the way they always have. QDoubleSpinBox rounded
    // every value to its decimals, and "Pause chance" steps by 0.001: with two decimals every arrow
    // click was rounded straight back to where it started.
    public static int DecimalsFor(double step)
    {
        var text = step.ToString("F10", System.Globalization.CultureInfo.InvariantCulture).TrimEnd('0');
        var dot = text.IndexOf('.', StringComparison.Ordinal);
        return Math.Max(2, dot < 0 ? 0 : text.Length - dot - 1);
    }

    internal void Write()
    {
        _write(IsInt ? (int)Value : Value);
    }

    internal JsonNode Stored => IsInt ? JsonValue.Create((int)Value) : JsonValue.Create(Value);

    internal void Reload() => Value = _read();
}

// A setting the settings show as a check box.
public sealed class ToggleSetting
{
    private readonly Action<bool> _write;

    internal ToggleSetting(string name, string label, string group, bool @default, bool value, Action<bool> write, string tooltip = "")
    {
        Name = name;
        Label = label;
        Group = group;
        Default = @default;
        Value = value;
        Tooltip = tooltip;
        _write = write;
    }

    public string Name { get; }
    public string Label { get; }
    public string Group { get; }
    public string Tooltip { get; }
    public bool Default { get; }
    public bool Value { get; set; }

    internal void Write() => _write(Value);
}

public enum ChoiceStyle
{
    Segments,
    List,
    Tiles,
    Swatches,
}

// A setting the settings show as one of a few named options. A value that is not one of them is
// not taken - the draft keeps what it had.
public sealed class ChoiceSetting
{
    private readonly Action<string> _write;
    private string _value;

    internal ChoiceSetting(string name, string label, string group, IReadOnlyList<LookOption> options, string @default, string value, Action<string> write)
    {
        Name = name;
        Label = label;
        Group = group;
        Options = options;
        Default = @default;
        _value = value;
        _write = write;
    }

    public string Name { get; }
    public string Label { get; }
    public string Group { get; }
    public IReadOnlyList<LookOption> Options { get; }
    public string Default { get; }

    // How it is shown: a row of segments, a drop-down list (for one that can grow long), tiles that
    // show each option, or colour swatches.
    public ChoiceStyle Style { get; init; }

    public bool Dropdown => Style == ChoiceStyle.List;

    // Where it is stored, under Group, when that is not its Name.
    public string Key
    {
        get => _key ?? Name;
        init => _key = value;
    }

    private readonly string? _key;

    public string Value
    {
        get => _value;
        set
        {
            if (Options.Any(o => o.Value == value)) _value = value;
        }
    }

    internal void Write() => _write(Value);
}

// What a tab of the settings holds, top to bottom; the panel draws each kind of row.
public abstract record SettingsRow;
// A card of its own: a title, a line saying what it is for, and the toggle that switches the whole
// of it on and off, if it has one - the rest of the card is greyed out while that is off.
public sealed record SectionRow(string Title, string Description = "", string? Switch = null) : SettingsRow;
public sealed record NoteRow(string Text) : SettingsRow;
// A number; Switch names a toggle shown beside it that turns what it is the chance of on and off.
public sealed record NumberRow(string Name, string? Switch = null) : SettingsRow;
// A minimum and its maximum, as one slider with two handles.
public sealed record RangeRow(string Min, string Max, string Label, string Hint = "") : SettingsRow;
public sealed record ToggleRow(string Name) : SettingsRow;
public sealed record ChoiceRow(string Name) : SettingsRow;
public sealed record RhythmsRow : SettingsRow;
public sealed record ManagePatternsRow : SettingsRow;
public sealed record LanguageRow : SettingsRow;
// The language of the app's own words. Not one of its tab's values: resetting the tab leaves it.
public sealed record UiLanguageRow : SettingsRow;
// The folder of the user's own .vrm models: chosen, shown, forgotten. Not reset with its tab.
public sealed record CharacterFolderRow : SettingsRow;
public sealed record TonesRow : SettingsRow;
public sealed record ManagePhraseFilesRow : SettingsRow;
public sealed record DeviceRow : SettingsRow;
// Where the Look tab shows its choices playing, live.
public sealed record LookPreviewRow : SettingsRow;
public sealed record ResetRow(string Tab) : SettingsRow;

// The title is the tab's name, which the form and the window find it by; it is shown translated.
public sealed record SettingsTab(string Title, IReadOnlyList<SettingsRow> Rows)
{
    public string DisplayTitle => L10n.T(Title);
}

// Port of src/SettingsDialog.py without its widgets: every setting as a draft, read from the live
// objects when the form is made, changed freely, reset per tab, and only applied - to the objects
// and the store at once - once it has passed the checks. A draft that fails them changes nothing:
// applying writes straight to the live handlers, so a half-applied invalid set would take effect
// even after the refusal.
//
// Applying while a session runs leaves the running segment alone: cutting the beat the user is
// following out from under them to prove the save worked would be worse than waiting. Everything
// still queued is planned again from the new values.
public sealed class SettingsForm
{
    private const string Log = "SettingsDialog";

    // Pairs where the first must not exceed the second, with the label of the refusal (translated
    // where the refusal is worded). Only the pause pair would crash (a random draw between them),
    // but every one of these is an inverted, meaningless range if saved that way.
    public static IReadOnlyList<(string Min, string Max, string Label)> MinMaxPairs { get; } =
    [
        ("min_dur", "max_dur", L10n.N("Slideshow duration")),
        ("min_beat_freq", "max_beat_freq", L10n.N("Beat frequency")),
        ("min_beat_dur", "max_beat_dur", L10n.N("Beat duration")),
        ("min_pause_dur", "max_pause_dur", L10n.N("Pause duration")),
        ("min_ramp_duration", "max_ramp_duration", L10n.N("Ramp duration")),
        ("min_climax_after", "max_climax_after", L10n.N("Climax time")),
        ("min_fake_climax_delay", "max_fake_climax_delay", L10n.N("Fake climax reveal delay")),
    ];

    // What each number is, for its slider's badge: minutes for the long stretches, percentages for
    // chances and volumes.
    private static readonly Dictionary<string, ValueUnit> Units = new()
    {
        ["min_dur"] = ValueUnit.Seconds,
        ["max_dur"] = ValueUnit.Seconds,
        ["video_min_dur"] = ValueUnit.Seconds,
        ["beat_loudness"] = ValueUnit.Percent,
        ["vid_loudness"] = ValueUnit.Percent,
        ["min_beat_freq"] = ValueUnit.Hertz,
        ["max_beat_freq"] = ValueUnit.Hertz,
        ["min_beat_dur"] = ValueUnit.Seconds,
        ["max_beat_dur"] = ValueUnit.Seconds,
        ["min_pause_dur"] = ValueUnit.Seconds,
        ["max_pause_dur"] = ValueUnit.Seconds,
        ["pause_chance"] = ValueUnit.Percent,
        ["edge_pause_dur"] = ValueUnit.Seconds,
        ["edge_cooldown_sec"] = ValueUnit.Duration,
        ["min_ramp_duration"] = ValueUnit.Duration,
        ["max_ramp_duration"] = ValueUnit.Duration,
        ["ramp_window_width"] = ValueUnit.Percent,
        ["min_climax_after"] = ValueUnit.Duration,
        ["max_climax_after"] = ValueUnit.Duration,
        ["ruined_orgasm_chance"] = ValueUnit.Percent,
        ["denied_orgasm_chance"] = ValueUnit.Percent,
        ["fake_climax_chance"] = ValueUnit.Percent,
        ["min_fake_climax_delay"] = ValueUnit.Seconds,
        ["max_fake_climax_delay"] = ValueUnit.Seconds,
        ["talking_chance"] = ValueUnit.Percent,
    };

    private readonly GoonerApp _app;
    private readonly Dictionary<string, NumberSetting> _numbers = [];
    private readonly Dictionary<string, ToggleSetting> _toggles = [];
    private readonly Dictionary<string, ChoiceSetting> _choices = [];
    private Dictionary<string, bool> _rhythms = [];
    private Dictionary<string, bool> _tones = [];

    public SettingsForm(GoonerApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        _app = app;
        var player = app.Player;
        var beat = app.Beat;
        var climax = app.Climax;
        var hud = app.Hud;
        var look = app.Look;

        NumberRow Num(string name, string label, string group, IReadOnlyDictionary<string, object> defaults, bool isInt,
            double min, double max, double step, Func<double> read, Action<double> write)
        {
            _numbers[name] = new NumberSetting(name, label, group, isInt, min, max, step, defaults, read, write, Units.GetValueOrDefault(name));
            return new NumberRow(name);
        }
        static SectionRow Section(string title, string description = "", ToggleRow? toggle = null) => new(title, description, toggle?.Name);
        static RangeRow Range(NumberRow min, NumberRow max, string label, string hint = "") => new(min.Name, max.Name, label, hint);
        ToggleRow Toggle(string name, string label, string group, IReadOnlyDictionary<string, object> defaults, bool value,
            Action<bool> write, string tooltip = "")
        {
            _toggles[name] = new ToggleSetting(name, label, group, (bool)defaults[name], value, write, tooltip);
            return new ToggleRow(name);
        }
        // More than a row of buttons holds goes in a list.
        ChoiceRow LookChoice(string name, string label, string value, Action<string> write)
        {
            var options = Look.Choices[name].Select(option => option with { Label = L10n.T(option.Label) }).ToList();
            _choices[name] = new ChoiceSetting(name, label, Look.SettingsGroup, options, (string)Look.Defaults[name], value, write) { Style = LookStyles.GetValueOrDefault(name) };
            return new ChoiceRow(name);
        }

        ChoiceRow CharacterRow(CharacterChoice character)
        {
            _choices["character"] = CharacterSetting(character.Options, character.File);
            return new ChoiceRow("character");
        }

        const string AppGroup = "GoonerApp";
        var tabs = new List<SettingsTab>
        {
            new(L10n.N("Playback"),
            [
                Section(L10n.T("App Language"), L10n.T("The app's own words. What she says is picked under Callouts.")),
                new UiLanguageRow(),
                Section(L10n.T("Slideshow"), L10n.T("How long a picture or a GIF stays up, drawn anew each time.")),
                Range(Num("min_dur", L10n.T("Min. duration (s):"), PlaylistPlayer.SettingsGroup, PlaylistPlayer.Defaults, false, 0.1, 60.0, 0.1, () => player.MinDur, v => player.MinDur = v), Num("max_dur", L10n.T("Max. duration (s):"), PlaylistPlayer.SettingsGroup, PlaylistPlayer.Defaults, false, 0.1, 60.0, 0.1, () => player.MaxDur, v => player.MaxDur = v), L10n.T("Pictures & GIFs")),
                Num("video_min_dur", L10n.T("Videos at least"), PlaylistPlayer.SettingsGroup, PlaylistPlayer.Defaults, false, 0.5, 30.0, 0.1, () => player.VideoMinDur, v => player.VideoMinDur = v),
                Section(L10n.T("Sound")),
                Num("beat_loudness", L10n.T("Beat click"), BeatHandler.SettingsGroup, BeatHandler.Defaults, false, 0.0, 1.0, 0.1, () => beat.BeatLoudness, v => beat.BeatLoudness = v),
                Num("vid_loudness", L10n.T("Video sound"), PlaylistPlayer.SettingsGroup, PlaylistPlayer.Defaults, false, 0.0, 1.0, 0.1, () => player.VidLoudness, v => player.VidLoudness = v),
                Section(L10n.T("On screen")),
                Toggle("show_record_chase", L10n.T("Show live personal-record chase"), SessionHud.SettingsGroup, SessionHud.Defaults, hud.ShowRecordChase, v => hud.ShowRecordChase = v),
                Toggle("show_session_timer", L10n.T("Show session timer"), SessionHud.SettingsGroup, SessionHud.Defaults, hud.ShowSessionTimer, v => hud.ShowSessionTimer = v),
                new ResetRow("Playback"),
            ]),
            new(L10n.N("Beat & Rhythm"),
            [
                Section(L10n.T("Beat"), L10n.T("Each stretch of the session picks a speed and a length between these.")),
                Range(Num("min_beat_freq", L10n.T("Beat Min. frequency (Hz):"), BeatHandler.SettingsGroup, BeatHandler.Defaults, false, 0.1, 20.0, 0.1, () => beat.MinBeatFreq, v => beat.MinBeatFreq = v), Num("max_beat_freq", L10n.T("Beat Max. frequency (Hz):"), BeatHandler.SettingsGroup, BeatHandler.Defaults, false, 0.1, 20.0, 0.1, () => beat.MaxBeatFreq, v => beat.MaxBeatFreq = v), L10n.T("Speed"), L10n.T("beats per second")),
                Range(Num("min_beat_dur", L10n.T("Beat Min. duration (s):"), BeatHandler.SettingsGroup, BeatHandler.Defaults, false, 1.0, 120.0, 1.0, () => beat.MinBeatDur, v => beat.MinBeatDur = v), Num("max_beat_dur", L10n.T("Beat Max. duration (s):"), BeatHandler.SettingsGroup, BeatHandler.Defaults, false, 1.0, 120.0, 1.0, () => beat.MaxBeatDur, v => beat.MaxBeatDur = v), L10n.T("Length of a stretch")),
                Section(L10n.T("Pauses"), L10n.T("Now and then the beat stops and counts down.")),
                Num("pause_chance", L10n.T("Chance"), BeatHandler.SettingsGroup, BeatHandler.Defaults, false, 0.001, 1, 0.001, () => beat.PauseChance, v => beat.PauseChance = v),
                Range(Num("min_pause_dur", L10n.T("Pause Min. duration (s):"), BeatHandler.SettingsGroup, BeatHandler.Defaults, true, 1, 180, 1, () => beat.MinPauseDur, v => beat.MinPauseDur = (int)v), Num("max_pause_dur", L10n.T("Pause Max. duration (s):"), BeatHandler.SettingsGroup, BeatHandler.Defaults, true, 1, 180, 1, () => beat.MaxPauseDur, v => beat.MaxPauseDur = (int)v), L10n.T("Length")),
                Section(L10n.T("Edge Relief"), L10n.T("Press E for a pause now and a gentler beat behind it. The climax waits out the break rather than being paid for with it."),
                    Toggle("edge_relief_active", L10n.T("\"I reached my Edge\" button active"), BeatHandler.SettingsGroup, BeatHandler.Defaults, beat.EdgeReliefActive, v => beat.EdgeReliefActive = v,
                    L10n.T("Press E during a session for a pause now and a gentler rhythm behind it. The climax waits out the break rather than being paid for with it."))),
                Num("edge_pause_dur", L10n.T("Pause"), BeatHandler.SettingsGroup, BeatHandler.Defaults, true, 5, 300, 5, () => beat.EdgePauseDur, v => beat.EdgePauseDur = (int)v),
                Num("edge_cooldown_sec", L10n.T("Cooldown"), BeatHandler.SettingsGroup, BeatHandler.Defaults, true, 0, 900, 10, () => beat.EdgeCooldownSec, v => beat.EdgeCooldownSec = (int)v),
                Section(L10n.T("Difficulty Ramping"), L10n.T("Starts gentle and tightens towards the fast end as the session goes on."),
                    Toggle("ramping_active", L10n.T("Difficulty ramping active"), BeatHandler.SettingsGroup, BeatHandler.Defaults, beat.RampingActive, v => beat.RampingActive = v)),
                Range(Num("min_ramp_duration", L10n.T("Ramp Min. duration (s):"), BeatHandler.SettingsGroup, BeatHandler.Defaults, false, 10.0, 7200.0, 10.0, () => beat.MinRampDuration, v => beat.MinRampDuration = v), Num("max_ramp_duration", L10n.T("Ramp Max. duration (s):"), BeatHandler.SettingsGroup, BeatHandler.Defaults, false, 10.0, 7200.0, 10.0, () => beat.MaxRampDuration, v => beat.MaxRampDuration = v), L10n.T("Ramp takes")),
                Num("ramp_window_width", L10n.T("Window width"), BeatHandler.SettingsGroup, BeatHandler.Defaults, false, 0.05, 1.0, 0.05, () => beat.RampWindowWidth, v => beat.RampWindowWidth = v),
                Section(L10n.T("Active Rhythms"), L10n.T("The ones ticked are drawn at random.")),
                new RhythmsRow(),
                new ManagePatternsRow(),
                new ResetRow("Beat & Rhythm"),
            ]),
            new(L10n.N("Climax"),
            [
                Section(L10n.T("Climax"), L10n.T("When she lets you - decided at the start, somewhere in this window."),
                    Toggle("climax_active", L10n.T("Climax prompts active"), ClimaxHandler.SettingsGroup, ClimaxHandler.Defaults, climax.ClimaxActive, v => climax.ClimaxActive = v)),
                Range(Num("min_climax_after", L10n.T("Climax earliest (s into session):"), ClimaxHandler.SettingsGroup, ClimaxHandler.Defaults, false, 10.0, 7200.0, 10.0, () => climax.MinClimaxAfter, v => climax.MinClimaxAfter = v), Num("max_climax_after", L10n.T("Climax latest (s into session):"), ClimaxHandler.SettingsGroup, ClimaxHandler.Defaults, false, 10.0, 7200.0, 10.0, () => climax.MaxClimaxAfter, v => climax.MaxClimaxAfter = v), L10n.T("Into the session")),
                Toggle("climax_only_after_ramp", L10n.T("Only once ramping is done"), ClimaxHandler.SettingsGroup, ClimaxHandler.Defaults, climax.ClimaxOnlyAfterRamp, v => climax.ClimaxOnlyAfterRamp = v,
                    L10n.T("Holds the climax back until the difficulty ramp has topped out, however early the window above allows it. Has no effect while difficulty ramping is off.")),
                Section(L10n.T("How it ends"), L10n.T("Chances for this session's climax.")),
                Num("ruined_orgasm_chance", L10n.T("Ruined"), ClimaxHandler.SettingsGroup, ClimaxHandler.Defaults, false, 0.0, 1.0, 0.01, () => climax.RuinedOrgasmChance, v => climax.RuinedOrgasmChance = v) with { Switch = Toggle("ruined_orgasm_active", L10n.T("Allow ruined orgasm outcome"), ClimaxHandler.SettingsGroup, ClimaxHandler.Defaults, climax.RuinedOrgasmActive, v => climax.RuinedOrgasmActive = v).Name },
                Num("denied_orgasm_chance", L10n.T("Denied"), ClimaxHandler.SettingsGroup, ClimaxHandler.Defaults, false, 0.0, 1.0, 0.01, () => climax.DeniedOrgasmChance, v => climax.DeniedOrgasmChance = v) with { Switch = Toggle("denied_orgasm_active", L10n.T("Allow full denial outcome"), ClimaxHandler.SettingsGroup, ClimaxHandler.Defaults, climax.DeniedOrgasmActive, v => climax.DeniedOrgasmActive = v).Name },
                Toggle("ask_for_outcome", L10n.T("Ask what actually happened"), AppGroup, GoonerApp.Defaults, app.AskForOutcome, v => app.AskForOutcome = v,
                    L10n.T("Offers I Came / I Ruined It / I Stopped at every climax cue, and asks once when you stop a session yourself - otherwise the app only ever records what it told you to do.")),
                Section(L10n.T("Fake-outs"), L10n.T("A cue that looks exactly like the real one - until it is revealed."),
                    Toggle("fake_climax_active", L10n.T("Fake climax cues active"), ClimaxHandler.SettingsGroup, ClimaxHandler.Defaults, climax.FakeClimaxActive, v => climax.FakeClimaxActive = v)),
                Num("fake_climax_chance", L10n.T("Chance"), ClimaxHandler.SettingsGroup, ClimaxHandler.Defaults, false, 0.0, 1.0, 0.01, () => climax.FakeClimaxChance, v => climax.FakeClimaxChance = v),
                Range(Num("min_fake_climax_delay", L10n.T("Fake climax reveal delay Min. (s)"), ClimaxHandler.SettingsGroup, ClimaxHandler.Defaults, false, 1.0, 30.0, 0.5, () => climax.MinFakeClimaxDelay, v => climax.MinFakeClimaxDelay = v), Num("max_fake_climax_delay", L10n.T("Fake climax reveal delay Max. (s)"), ClimaxHandler.SettingsGroup, ClimaxHandler.Defaults, false, 1.0, 30.0, 0.5, () => climax.MaxFakeClimaxDelay, v => climax.MaxFakeClimaxDelay = v), L10n.T("Revealed after")),
                new ResetRow("Climax"),
            ]),
        };
        if (app.Callouts is { } callouts)
        {
            tabs.Add(new SettingsTab(L10n.N("Callouts"),
            [
                Section(L10n.T("Callouts"), L10n.T("What she says on beat changes, pauses and everything in between."),
                    Toggle("active_callout", L10n.T("Callouts active"), CalloutHandler.SettingsGroup, CalloutHandler.Defaults, callouts.ActiveCallout, v => callouts.ActiveCallout = v)),
                new LanguageRow(),
                Num("talking_chance", L10n.T("How often"), CalloutHandler.SettingsGroup, CalloutHandler.Defaults, false, 0, 1, 0.01, () => callouts.TalkingChance, v => callouts.TalkingChance = v),
                Section(L10n.T("Tone"), L10n.T("Pick one or more - several are mixed evenly.")),
                new TonesRow(),
                new ManagePhraseFilesRow(),
                new ResetRow("Callouts"),
            ]));
            Language = callouts.Lang;
            _tones = callouts.AvailableTones.ToDictionary(t => t, t => callouts.SelectedTones.Contains(t));
        }
        // The engine's own: the note track, the colours, the effects - nothing the Python app had.
        tabs.Add(new SettingsTab(L10n.N("Look"),
        [
            Section(L10n.T("Note Track")),
            LookChoice("note_track", L10n.T("Note track:"), look.NoteTrack, v => look.NoteTrack = v),
            Section(L10n.T("Hypno Ring")),
            Toggle("focus_wanders", L10n.T("The ring wanders across the picture"), Look.SettingsGroup, Look.Defaults, look.FocusWanders, v => look.FocusWanders = v,
                L10n.T("It drifts across the picture the whole time, slowly and in smooth curves, and your eyes go with it.")),
            LookChoice("censor", L10n.T("Around the ring:"), look.Censor, v => look.Censor = v),
            Section(L10n.T("Colours & Effects")),
            LookChoice("color_scheme", L10n.T("Colours:"), look.ColorScheme, v => look.ColorScheme = v),
            LookChoice("effects", L10n.T("Effects:"), look.Effects, v => look.Effects = v),
            Section(L10n.T("Character"), L10n.T("Zazurona, or a .vrm of your own.")),
            CharacterRow(app.Character),
            new CharacterFolderRow(),
            Toggle("in_session", L10n.T("Shown during a session, closer with each callout"), CharacterChoice.SettingsGroup, CharacterChoice.Defaults,
                app.Character.InSession, v => app.Character.InSession = v),
            LookChoice("telepathy_frame", L10n.T("Frame:"), look.TelepathyFrame, v => look.TelepathyFrame = v),
            LookChoice("toy_color", L10n.T("Toy colour:"), look.ToyColor, v => look.ToyColor = v),
            new LookPreviewRow(),
            new ResetRow("Look"),
        ]));
        // The device's tab only when there is device output - the tab is its UI, not the form's.
        if (app.Intiface is { } intiface)
        {
            Device = new DeviceSettings(intiface, app.Settings);
            tabs.Add(new SettingsTab(IntifacePlugin.TabTitle, [new DeviceRow()]));
        }
        Tabs = tabs;
        UiLanguages = app.UiLanguage.Options;
        UiLanguage = app.UiLanguage.Language;
        CharacterFolder = app.Character.Folder;
        RefreshRhythms();
        _saved = Snapshot();
    }

    public IReadOnlyList<SettingsTab> Tabs { get; }

    public IReadOnlyDictionary<string, NumberSetting> Numbers => _numbers;

    public IReadOnlyDictionary<string, ToggleSetting> Toggles => _toggles;

    public IReadOnlyDictionary<string, ChoiceSetting> Choices => _choices;

    // The folder of the user's own models as the draft has it; applied with the rest.
    public string? CharacterFolder { get; private set; }

    // Picks the folder (null forgets it) and offers its models at once; the character picked stays
    // if it is still on offer.
    public void SetCharacterFolder(string? folder)
    {
        CharacterFolder = string.IsNullOrEmpty(folder) ? null : folder;
        var options = _app.Character.OptionsFor(CharacterFolder);
        var current = _choices["character"].Value;
        _choices["character"] = CharacterSetting(options, options.Any(o => o.Value == current) ? current : options[0].Value);
    }

    // Someone may keep a great many models: a list. The look's choices are shown as what they are.
    private static readonly Dictionary<string, ChoiceStyle> LookStyles = new()
    {
        ["note_track"] = ChoiceStyle.Tiles,
        ["color_scheme"] = ChoiceStyle.Tiles,
        ["telepathy_frame"] = ChoiceStyle.Tiles,
        ["toy_color"] = ChoiceStyle.Swatches,
    };

    private ChoiceSetting CharacterSetting(IReadOnlyList<LookOption> options, string value) =>
        new("character", L10n.T("On the start screen:"), CharacterChoice.SettingsGroup, options,
            _app.Character.First, value, _app.Character.Choose) { Style = ChoiceStyle.List, Key = "file" };

    // The Device tab's draft; null without device output. Its connection settings are applied
    // separately, after Apply, since a changed address may first need the user's consent.
    public DeviceSettings? Device { get; }

    // --- rhythms ---

    // Every rhythm there is, built-in and custom, and whether it is ticked.
    public IReadOnlyDictionary<string, bool> Rhythms => _rhythms;

    public void SetRhythm(string name, bool on)
    {
        if (_rhythms.ContainsKey(name)) _rhythms[name] = on;
    }

    // A rhythm's steps, for the dots its chip shows.
    public IReadOnlyList<int> Pattern(string name) =>
        _app.Beat.AvailableBeatPatterns.TryGetValue(name, out var steps) ? steps : [];

    public string RhythmLabel(string name) =>
        $"({BeatPatterns.DisplayName(name)} [{string.Join(", ", _app.Beat.AvailableBeatPatterns[name])}])";

    // After the pattern editor: a pattern added there appears, one deleted there goes. A rhythm
    // still listed keeps the tick the draft gave it; a new one is ticked if the handler has it.
    public void RefreshRhythms()
    {
        var before = _rhythms;
        _rhythms = _app.Beat.AvailableBeatPatterns.Keys.ToDictionary(
            name => name,
            name => before.TryGetValue(name, out var ticked) ? ticked : _app.Beat.SelectedBeatPatterns.Contains(name));
    }

    // --- the app's own language ---

    // Every language the app's own words come in, each under its own name.
    public IReadOnlyList<LookOption> UiLanguages { get; }

    public string UiLanguage
    {
        get => _uiLanguage;
        set
        {
            if (UiLanguages.Any(o => o.Value == value)) _uiLanguage = value;
        }
    }

    private string _uiLanguage = "";

    // --- callouts ---

    public IReadOnlyList<string> Languages => _app.Callouts?.AvailableLanguages ?? [];

    public string Language { get; set; } = "";

    public IReadOnlyDictionary<string, bool> Tones => _tones;

    public void SetTone(string tone, bool on)
    {
        if (_tones.ContainsKey(tone)) _tones[tone] = on;
    }

    // A language may ship only some tones. The handler skips what the current language lacks,
    // but silently - the box would stay ticked while a different tone speaks. So it is greyed out,
    // and keeps its tick: the user may well switch back to the language that has it.
    public bool ToneUsable(string tone) => _app.Callouts?.TonesFor(Language).Contains(tone) ?? false;

    public string ToneTooltip(string tone) => ToneUsable(tone) ? "" : L10n.F("No phrases for this tone in {0} yet.", Language);

    // Greyed out alone only says "no". Saying why once under the grid beats a parenthetical on
    // every second row.
    public string ToneHint
    {
        get
        {
            var unavailable = _tones.Keys.Where(t => !ToneUsable(t)).Select(CalloutHandler.ToneLabel).ToList();
            return unavailable.Count == 0 ? "" : L10n.F("Greyed out: no {0} phrases yet for {1}.", Language, string.Join(", ", unavailable));
        }
    }

    // --- changes not yet saved ---

    private Dictionary<string, string> _saved = [];

    // How many values the draft holds that are not what was saved - for the panel's foot. A value
    // moved and moved back is no change.
    public int Changes
    {
        get
        {
            var now = Snapshot();
            return now.Keys.Union(_saved.Keys).Count(key => now.GetValueOrDefault(key) != _saved.GetValueOrDefault(key));
        }
    }

    private Dictionary<string, string> Snapshot()
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var snapshot = new Dictionary<string, string>
        {
            ["ui_language"] = UiLanguage,
            ["language"] = Language,
            ["character_folder"] = CharacterFolder ?? "",
        };
        foreach (var (name, number) in _numbers) snapshot[$"number/{name}"] = number.Value.ToString(inv);
        foreach (var (name, toggle) in _toggles) snapshot[$"toggle/{name}"] = toggle.Value.ToString(inv);
        foreach (var (name, choice) in _choices) snapshot[$"choice/{name}"] = choice.Value;
        foreach (var (name, on) in _rhythms) snapshot[$"rhythm/{name}"] = on.ToString(inv);
        foreach (var (name, on) in _tones) snapshot[$"tone/{name}"] = on.ToString(inv);
        return snapshot;
    }

    // --- reset ---

    // Reset to defaults for one tab: only the draft - pressing Save applies it, as any other change.
    public void Reset(string tab)
    {
        foreach (var row in Tabs.Single(t => t.Title == tab).Rows)
        {
            switch (row)
            {
                case NumberRow number:
                    _numbers[number.Name].Value = _numbers[number.Name].Default;
                    if (number.Switch is { } numberSwitch) _toggles[numberSwitch].Value = _toggles[numberSwitch].Default;
                    break;
                case RangeRow range:
                    _numbers[range.Min].Value = _numbers[range.Min].Default;
                    _numbers[range.Max].Value = _numbers[range.Max].Default;
                    break;
                case SectionRow { Switch: { } sectionSwitch }:
                    _toggles[sectionSwitch].Value = _toggles[sectionSwitch].Default;
                    break;
                case ToggleRow toggle:
                    _toggles[toggle.Name].Value = _toggles[toggle.Name].Default;
                    break;
                case ChoiceRow choice:
                    _choices[choice.Name].Value = _choices[choice.Name].Default;
                    break;
                case RhythmsRow:
                    foreach (var name in _rhythms.Keys.ToList()) _rhythms[name] = true;
                    break;
                case LanguageRow:
                    var defaultLang = (string)CalloutHandler.Defaults["lang"];
                    if (Languages.Contains(defaultLang)) Language = defaultLang;
                    break;
                case TonesRow:
                    var defaultTones = (string[])CalloutHandler.Defaults["selected_tones"];
                    foreach (var tone in _tones.Keys.ToList()) _tones[tone] = defaultTones.Contains(tone);
                    break;
            }
        }
    }

    // --- saving ---

    // The first reason the draft cannot be saved, or null.
    public string? ValidationError()
    {
        if (Device?.ValidationError() is { } deviceError) return deviceError;
        if (!_rhythms.Values.Any(ticked => ticked))
            return L10n.T("At least one rhythm has to stay active under 'Active Rhythms'.");
        foreach (var (min, max, label) in MinMaxPairs)
            if (_numbers[min].Value > _numbers[max].Value)
                return L10n.F("{0}: the minimum must not be higher than the maximum.", L10n.T(label));
        return null;
    }

    // Save & Close Settings: checked first, then every value to its object and to the store.
    // False, and nothing written, when the draft does not pass.
    public bool Apply()
    {
        if (ValidationError() is { } error)
        {
            AppLog.Info(Log, "Settings rejected: {0}", error);
            return false;
        }
        var settings = _app.Settings;
        foreach (var number in _numbers.Values)
        {
            number.Write();
            settings?.SetValue($"{number.Group}/{number.Name}", number.Stored);
        }
        foreach (var toggle in _toggles.Values)
        {
            toggle.Write();
            settings?.SetValue($"{toggle.Group}/{toggle.Name}", toggle.Value);
        }
        // Before the choices: the character picked may be one from the new folder.
        _app.Character.ChooseFolder(CharacterFolder);
        foreach (var choice in _choices.Values)
        {
            choice.Write();
            settings?.SetValue($"{choice.Group}/{choice.Key}", choice.Value);
        }

        var selected = _rhythms.Where(r => r.Value).Select(r => r.Key).ToList();
        _app.Beat.SelectedBeatPatterns = selected;
        settings?.SetValue("BeatHandler/selected_beat_patterns", new JsonArray([.. selected.Select(s => (JsonNode)s)]));

        if (_app.Callouts is { } callouts)
        {
            settings?.SetValue($"{CalloutHandler.SettingsGroup}/selected_lang", Language);
            callouts.SetLang(Language);
            callouts.SetTones(TickedTones());
            settings?.SetValue($"{CalloutHandler.SettingsGroup}/selected_tones", new JsonArray([.. callouts.SelectedTones.Select(t => (JsonNode)t)]));
        }

        _app.UiLanguage.Choose(UiLanguage);

        _app.SettingsSaved();
        _saved = Snapshot();
        AppLog.Info(Log, "Settings saved ({0} active rhythms)", selected.Count);
        return true;
    }

    // Falls back to the default tone rather than saving an empty mix: the handler would fall back
    // at draw time anyway, and this way the form does not claim no tone is active while one plainly
    // is.
    private List<string> TickedTones()
    {
        var ticked = _tones.Where(t => t.Value).Select(t => t.Key).ToList();
        if (ticked.Count > 0) return ticked;
        foreach (var tone in _tones.Keys.ToList()) _tones[tone] = tone == CalloutHandler.DefaultTone;
        return [CalloutHandler.DefaultTone];
    }
}
