using Gooner.Core.App;
using Gooner.Core.Callouts;
using Gooner.Core.Characters;
using Gooner.Core.Climax;
using Gooner.Core.Data;
using Gooner.Core.Localization;
using Gooner.Core.Logging;
using Gooner.Core.Media;
using Gooner.Core.Panels;
using Gooner.Core.Settings;
using Gooner.Core.Tests.Support;
using Gooner.Core.Timing;

namespace Gooner.Core.Tests.Panels;

// Port of tests/test_settings_dialog.py: the settings as a draft of every value, read from the
// live objects, reset per tab, checked before anything is written, and applied to the objects and
// the store at once. Where the Python test sets a widget, this sets the draft value the widget
// shows; where it clicks Save, this applies the draft. The real callouts are used, as the Python
// app fixture does, because a language that lacks a tone is part of what is tested.
public sealed class SettingsFormTests : IDisposable
{
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-settings-form-");
    private readonly FrameClock _clock = new(1_800_000_000.0);
    private readonly JsonSettingsStore _settings;
    private readonly GoonerApp _app;
    private SettingsForm? _form;

    public SettingsFormTests()
    {
        var data = new UserDataStore(Path.Combine(_tmp.FullName, "data"));
        _settings = new JsonSettingsStore(data);
        _app = new GoonerApp(new FakeStage(), _clock, new GoonerAppOptions
        {
            Settings = _settings,
            DataStore = data,
            CalloutDir = Repo.PathTo(Path.Combine("res", "callouts")),
            LocaleDir = Repo.PathTo(Path.Combine("res", "locales")),
            Random = new ScriptedRandom(1),
        });
    }

    public void Dispose()
    {
        L10n.Use(null);
        AppLog.Configure(false, null);
        _tmp.Delete(recursive: true);
    }

    private SettingsForm Form => _form ??= new SettingsForm(_app);

    private NumberSetting Number(string name) => Form.Numbers[name];

    private string MissingInGerman() =>
        _app.Callouts!.AvailableTones.First(t => !_app.Callouts.TonesFor("de").Contains(t));

    private void StartSession()
    {
        _app.LoadPlaylist([Path.Combine(_tmp.FullName, "a.png")]);
        _clock.Now += 0.1;
        _app.Frame();
    }

    [Fact]
    public void settings_fields_initialized_from_target_object()
    {
        Assert.Equal(_app.Player.MinDur, Number("min_dur").Value);
        Assert.Equal(PlaylistPlayer.SettingsGroup, Number("min_dur").Group);
        Assert.False(Number("min_dur").IsInt);

        Assert.True(Number("min_pause_dur").IsInt);
        Assert.Equal("BeatHandler", Number("beat_loudness").Group);
    }

    [Fact]
    public void beat_selection_reflects_currently_selected_patterns()
    {
        _app.Beat.SelectedBeatPatterns = ["Standard Beat"];

        Assert.True(Form.Rhythms["Standard Beat"]);
        Assert.False(Form.Rhythms.First(r => r.Key != "Standard Beat").Value);
    }

    [Fact]
    public void a_rhythm_is_listed_with_its_steps() =>
        Assert.Equal("(Standard Beat [1])", Form.RhythmLabel("Standard Beat"));

    [Fact]
    public void manage_patterns_button_opens_editor_and_refreshes_selection()
    {
        var form = Form;
        _app.Beat.AddOrUpdateCustomPattern("From Editor", [1, -1]);

        form.RefreshRhythms();

        Assert.Contains("From Editor", form.Rhythms.Keys);
    }

    [Fact]
    public void refresh_beat_selection_drops_deleted_custom_patterns()
    {
        _app.Beat.AddOrUpdateCustomPattern("Temp Pattern", [1, -1]);
        Form.RefreshRhythms();
        Assert.Contains("Temp Pattern", Form.Rhythms.Keys);

        _app.Beat.DeleteCustomPattern("Temp Pattern");
        Form.RefreshRhythms();
        Assert.DoesNotContain("Temp Pattern", Form.Rhythms.Keys);
    }

    // Ticks made before the editor opened survive its closing.
    [Fact]
    public void refreshing_the_rhythms_keeps_what_was_ticked_in_the_draft()
    {
        Form.SetRhythm("Standard Beat", false);
        _app.Beat.AddOrUpdateCustomPattern("From Editor", [1, -1]);

        Form.RefreshRhythms();

        Assert.False(Form.Rhythms["Standard Beat"]);
    }

    [Fact]
    public void callout_selection_reflects_current_state()
    {
        Assert.Equal(_app.Callouts!.Lang, Form.Language);
        Assert.Equal(_app.Callouts.ActiveCallout, Form.Toggles["active_callout"].Value);
    }

    [Fact]
    public void accept_settings_applies_spinbox_values_to_target()
    {
        Number("min_dur").Value = 1.23;

        Assert.True(Form.Apply());

        Assert.Equal(1.23, _app.Player.MinDur, 9);
    }

    [Fact]
    public void accept_settings_casts_int_fields()
    {
        Number("min_pause_dur").Value = 7.0;

        Form.Apply();

        Assert.Equal(7, _app.Beat.MinPauseDur);
    }

    [Fact]
    public void accept_settings_persists_to_qsettings()
    {
        Number("min_dur").Value = 2.5;

        Form.Apply();

        Assert.Equal(2.5, _settings.GetDouble("GoonerApp/min_dur", 0));
    }

    [Fact]
    public void accept_settings_updates_selected_beat_patterns()
    {
        foreach (var name in Form.Rhythms.Keys.ToList())
            Form.SetRhythm(name, name == "Standard Beat");

        Form.Apply();

        Assert.Equal(["Standard Beat"], _app.Beat.SelectedBeatPatterns);
        Assert.Equal(["Standard Beat"], _settings.GetStringList("BeatHandler/selected_beat_patterns")!);
    }

    [Fact]
    public void accept_settings_updates_callout_handler()
    {
        Form.Toggles["active_callout"].Value = true;
        var other = _app.Callouts!.AvailableLanguages.First(l => l != _app.Callouts.Lang);
        Form.Language = other;

        Form.Apply();

        Assert.True(_app.Callouts.ActiveCallout);
        Assert.Equal(other, _app.Callouts.Lang);
        Assert.Equal(other, _settings.GetString("CalloutHandler/selected_lang", ""));
    }

    [Fact]
    public void accept_settings_replans_when_running()
    {
        StartSession();
        Number("min_beat_dur").Value = 17.0;
        Number("max_beat_dur").Value = 17.0;

        Form.Apply();

        Assert.Contains(_app.Beat.PlannedSegments, s => s.Kind == "beat");
        Assert.All(_app.Beat.PlannedSegments.Where(s => s.Kind == "beat"), s => Assert.Equal(17.0, s.DurationSec));
    }

    [Fact]
    public void accept_settings_does_not_replan_when_stopped()
    {
        var planned = _app.Beat.PlannedSegments.ToList();

        Form.Apply();

        Assert.Equal(planned, _app.Beat.PlannedSegments);
    }

    [Fact]
    public void startup_splash_checkbox_initialized_from_app() =>
        Assert.Equal(_app.ShowStartupSplash, Form.Toggles["show_startup_splash"].Value);

    [Fact]
    public void accept_settings_updates_show_startup_splash()
    {
        var expected = !_app.ShowStartupSplash;
        Form.Toggles["show_startup_splash"].Value = expected;

        Form.Apply();

        Assert.Equal(expected, _app.ShowStartupSplash);
        Assert.Equal(expected, _settings.GetBool("GoonerApp/show_startup_splash", !expected));
    }

    [Fact]
    public void record_chase_checkbox_initialized_from_app() =>
        Assert.Equal(_app.Hud.ShowRecordChase, Form.Toggles["show_record_chase"].Value);

    [Fact]
    public void accept_settings_updates_show_record_chase()
    {
        var expected = !_app.Hud.ShowRecordChase;
        Form.Toggles["show_record_chase"].Value = expected;

        Form.Apply();

        Assert.Equal(expected, _app.Hud.ShowRecordChase);
        Assert.Equal(expected, _settings.GetBool("GoonerApp/show_record_chase", !expected));
    }

    [Fact]
    public void session_timer_checkbox_initialized_from_app() =>
        Assert.Equal(_app.Hud.ShowSessionTimer, Form.Toggles["show_session_timer"].Value);

    // The HUD is refreshed at once: a timer switched off while a session runs disappears.
    [Fact]
    public void accept_settings_updates_show_session_timer()
    {
        StartSession();
        _app.Hud.RefreshClock();
        Assert.NotNull(_app.Hud.SessionTimerText);
        Form.Toggles["show_session_timer"].Value = false;

        Form.Apply();

        Assert.False(_app.Hud.ShowSessionTimer);
        Assert.False(_settings.GetBool("GoonerApp/show_session_timer", true));
        Assert.Null(_app.Hud.SessionTimerText);
    }

    [Fact]
    public void ramping_fields_initialized_from_beat_handler()
    {
        Assert.Equal(_app.Beat.RampingActive, Form.Toggles["ramping_active"].Value);
        foreach (var name in new[] { "min_ramp_duration", "max_ramp_duration", "ramp_window_width" })
            Assert.Equal("BeatHandler", Number(name).Group);
    }

    [Fact]
    public void accept_settings_updates_ramping_active()
    {
        var expected = !_app.Beat.RampingActive;
        Form.Toggles["ramping_active"].Value = expected;

        Form.Apply();

        Assert.Equal(expected, _app.Beat.RampingActive);
        Assert.Equal(expected, _settings.GetBool("BeatHandler/ramping_active", !expected));
    }

    [Fact]
    public void climax_only_after_ramp_checkbox_initialized_from_handler() =>
        Assert.Equal(_app.Climax.ClimaxOnlyAfterRamp, Form.Toggles["climax_only_after_ramp"].Value);

    [Fact]
    public void accept_settings_updates_climax_only_after_ramp()
    {
        var expected = !_app.Climax.ClimaxOnlyAfterRamp;
        Form.Toggles["climax_only_after_ramp"].Value = expected;

        Form.Apply();

        Assert.Equal(expected, _app.Climax.ClimaxOnlyAfterRamp);
        Assert.Equal(expected, _settings.GetBool("ClimaxHandler/climax_only_after_ramp", !expected));
    }

    [Fact]
    public void climax_fields_initialized_from_climax_handler()
    {
        Assert.Equal(_app.Climax.ClimaxActive, Form.Toggles["climax_active"].Value);
        Assert.Equal(_app.Climax.RuinedOrgasmActive, Form.Toggles["ruined_orgasm_active"].Value);
        Assert.Equal(_app.Climax.DeniedOrgasmActive, Form.Toggles["denied_orgasm_active"].Value);
        Assert.Equal(_app.Climax.FakeClimaxActive, Form.Toggles["fake_climax_active"].Value);
        foreach (var name in new[] { "min_climax_after", "ruined_orgasm_chance", "denied_orgasm_chance", "fake_climax_chance", "min_fake_climax_delay", "max_fake_climax_delay" })
            Assert.Equal(ClimaxHandler.SettingsGroup, Number(name).Group);
    }

    [Fact]
    public void accept_settings_updates_climax_toggles()
    {
        var names = new[] { "climax_active", "ruined_orgasm_active", "denied_orgasm_active", "fake_climax_active" };
        foreach (var name in names) Form.Toggles[name].Value = !Form.Toggles[name].Value;
        var expected = names.ToDictionary(n => n, n => Form.Toggles[n].Value);

        Form.Apply();

        Assert.Equal(expected["climax_active"], _app.Climax.ClimaxActive);
        Assert.Equal(expected["ruined_orgasm_active"], _app.Climax.RuinedOrgasmActive);
        Assert.Equal(expected["denied_orgasm_active"], _app.Climax.DeniedOrgasmActive);
        Assert.Equal(expected["fake_climax_active"], _app.Climax.FakeClimaxActive);
        foreach (var name in names)
            Assert.Equal(expected[name], _settings.GetBool($"ClimaxHandler/{name}", !expected[name]));
    }

    [Fact]
    public void accept_settings_applies_climax_spinbox_values()
    {
        Number("min_climax_after").Value = 90.0;

        Form.Apply();

        Assert.Equal(90.0, _app.Climax.MinClimaxAfter);
    }

    // --- reset to defaults ---

    [Fact]
    public void playback_reset_button_resets_fields()
    {
        foreach (var name in new[] { "min_dur", "max_dur", "video_min_dur" }) Number(name).Value = 9.9;
        Number("beat_loudness").Value = 0.0;
        Number("vid_loudness").Value = 0.0;
        foreach (var name in new[] { "show_startup_splash", "show_record_chase", "show_session_timer" })
            Form.Toggles[name].Value = !Form.Toggles[name].Default;

        Form.Reset("Playback");

        Assert.Equal((double)PlaylistPlayer.Defaults["min_dur"], Number("min_dur").Value);
        Assert.Equal((double)PlaylistPlayer.Defaults["max_dur"], Number("max_dur").Value);
        Assert.Equal((double)PlaylistPlayer.Defaults["video_min_dur"], Number("video_min_dur").Value);
        Assert.Equal((double)Gooner.Core.Beat.BeatHandler.Defaults["beat_loudness"], Number("beat_loudness").Value);
        Assert.Equal((double)PlaylistPlayer.Defaults["vid_loudness"], Number("vid_loudness").Value);
        Assert.Equal((bool)GoonerApp.Defaults["show_startup_splash"], Form.Toggles["show_startup_splash"].Value);
        Assert.Equal((bool)SessionHud.Defaults["show_record_chase"], Form.Toggles["show_record_chase"].Value);
        Assert.Equal((bool)SessionHud.Defaults["show_session_timer"], Form.Toggles["show_session_timer"].Value);
    }

    [Fact]
    public void beat_reset_button_resets_fields()
    {
        Number("min_beat_freq").Value = 19.0;
        Number("ramp_window_width").Value = 1.0;
        Form.Toggles["ramping_active"].Value = false;
        Form.SetRhythm(Form.Rhythms.Keys.First(n => n != "Standard Beat"), false);

        Form.Reset("Beat & Rhythm");

        Assert.Equal((double)Gooner.Core.Beat.BeatHandler.Defaults["min_beat_freq"], Number("min_beat_freq").Value);
        Assert.Equal((double)Gooner.Core.Beat.BeatHandler.Defaults["ramp_window_width"], Number("ramp_window_width").Value);
        Assert.Equal((bool)Gooner.Core.Beat.BeatHandler.Defaults["ramping_active"], Form.Toggles["ramping_active"].Value);
        Assert.All(Form.Rhythms.Values, Assert.True);
    }

    [Fact]
    public void climax_reset_button_resets_fields()
    {
        Number("min_climax_after").Value = 990.0;
        Form.Toggles["climax_active"].Value = false;
        Form.Toggles["ruined_orgasm_active"].Value = true;
        Form.Toggles["denied_orgasm_active"].Value = true;
        Form.Toggles["fake_climax_active"].Value = false;

        Form.Reset("Climax");

        Assert.Equal((double)ClimaxHandler.Defaults["min_climax_after"], Number("min_climax_after").Value);
        foreach (var name in new[] { "climax_active", "ruined_orgasm_active", "denied_orgasm_active", "fake_climax_active" })
            Assert.Equal((bool)ClimaxHandler.Defaults[name], Form.Toggles[name].Value);
    }

    [Fact]
    public void callout_reset_button_resets_fields()
    {
        Number("talking_chance").Value = 0.99;
        Form.Toggles["active_callout"].Value = true;
        Form.Language = _app.Callouts!.AvailableLanguages.First(l => l != "en");

        Form.Reset("Callouts");

        Assert.Equal((double)CalloutHandler.Defaults["talking_chance"], Number("talking_chance").Value);
        Assert.Equal((bool)CalloutHandler.Defaults["active_callout"], Form.Toggles["active_callout"].Value);
        Assert.Equal((string)CalloutHandler.Defaults["lang"], Form.Language);
    }

    [Fact]
    public void reset_buttons_do_not_persist_until_save()
    {
        Number("min_dur").Value = 9.9;
        var original = _app.Player.MinDur;

        Form.Reset("Playback");

        Assert.Equal(original, _app.Player.MinDur);
    }

    // --- settings that would crash the beat engine must not be saveable ---

    [Fact]
    public void accept_settings_rejects_an_empty_rhythm_selection()
    {
        var before = _app.Beat.SelectedBeatPatterns.ToList();
        foreach (var name in Form.Rhythms.Keys.ToList()) Form.SetRhythm(name, false);

        Assert.False(Form.Apply());

        Assert.Equal("At least one rhythm has to stay active under 'Active Rhythms'.", Form.ValidationError());
        Assert.Equal(before, _app.Beat.SelectedBeatPatterns);
    }

    [Fact]
    public void accept_settings_rejects_inverted_pause_bounds()
    {
        Number("min_pause_dur").Value = 30;
        Number("max_pause_dur").Value = 5;

        Assert.False(Form.Apply());

        Assert.Equal("Pause duration: the minimum must not be higher than the maximum.", Form.ValidationError());
        Assert.NotEqual(30, _app.Beat.MinPauseDur);
    }

    // Driven from the form's own list rather than a copy of it: a copy is how the climax delay
    // pair went unguarded for a commit, and a pair that is never exercised is exactly the one that
    // ships inverted.
    public static TheoryData<string, string> MinMaxPairs()
    {
        var data = new TheoryData<string, string>();
        foreach (var (min, max, _) in SettingsForm.MinMaxPairs) data.Add(min, max);
        return data;
    }

    [Theory]
    [MemberData(nameof(MinMaxPairs))]
    public void accept_settings_rejects_every_inverted_min_max_pair(string minName, string maxName)
    {
        Number(maxName).Value = Number(maxName).Min;
        Number(minName).Value = Number(minName).Max;

        Assert.False(Form.Apply());
    }

    [Fact]
    public void accept_settings_allows_min_equal_to_max()
    {
        Number("min_pause_dur").Value = 7;
        Number("max_pause_dur").Value = 7;

        Assert.True(Form.Apply());

        Assert.Null(Form.ValidationError());
        Assert.Equal(7, _app.Beat.MinPauseDur);
        Assert.Equal(7, _app.Beat.MaxPauseDur);
    }

    // The key used to come from the class name, with every read side hardcoding the same string
    // separately - which is how two settings ended up written but never read.
    [Fact]
    public void settings_are_persisted_under_the_owner_group_constant()
    {
        Number("min_pause_dur").Value = 9;
        Number("max_pause_dur").Value = 11;

        Form.Apply();

        // The literal keys, not $"{SettingsGroup}/..." - asserting against the constant would hold
        // no matter what the constant said, which is the thing being guarded.
        Assert.NotNull(_settings.Value("BeatHandler/min_pause_dur"));
        Assert.NotNull(_settings.Value("GoonerApp/min_dur"));
    }

    // --- spin box precision, and not disturbing a running pause ---

    // QDoubleSpinBox defaulted to 2 decimals; "Pause chance" had a 0.001 step, so every arrow click
    // was rounded straight back to where it started.
    [Fact]
    public void every_spinbox_can_actually_reach_its_own_step()
    {
        foreach (var field in Form.Numbers.Values)
            Assert.True(Math.Round(field.Step, field.Decimals) == field.Step, $"{field.Name}: step {field.Step} is finer than {field.Decimals} decimals");
    }

    // What QDoubleSpinBox.setValue did: held to the field's range and rounded to its decimals.
    [Fact]
    public void a_value_is_held_to_its_range_and_its_decimals()
    {
        var chance = Number("pause_chance");

        chance.Value = 5;
        Assert.Equal(chance.Max, chance.Value);
        chance.Value = 0.12345;
        Assert.Equal(0.123, chance.Value);
        chance.Value = -1;
        Assert.Equal(chance.Min, chance.Value);
    }

    // Cutting the beat the user is currently following out from under them, just to prove the save
    // landed, is worse than letting it play out.
    [Fact]
    public void saving_leaves_the_running_segment_alone()
    {
        StartSession();
        var running = _app.Beat.CurrentSegment;

        Form.Apply();

        Assert.Same(running, _app.Beat.CurrentSegment);
    }

    [Fact]
    public void saving_applies_the_new_values_to_everything_still_queued()
    {
        StartSession();
        Number("min_beat_dur").Value = 30.0;
        Number("max_beat_dur").Value = 30.0;

        Form.Apply();

        Assert.All(_app.Beat.PlannedSegments.Where(s => s.Kind == "beat"), s => Assert.Equal(30.0, s.DurationSec));
    }

    [Fact]
    public void saving_during_a_pause_lets_the_pause_finish()
    {
        StartSession();
        _app.Beat.StartPause();
        Assert.True(_app.Beat.IsPaused());

        Form.Apply();

        Assert.True(_app.Beat.IsPaused());
    }

    // --- callout tones (second axis next to language) ---

    [Fact]
    public void tone_checkboxes_offer_every_available_tone() =>
        Assert.Equal(_app.Callouts!.AvailableTones, Form.Tones.Keys);

    [Fact]
    public void tone_checkboxes_are_labelled_not_keyed() =>
        Assert.Equal("Girlfriend Experience", CalloutHandler.ToneLabel("girlfriend"));

    [Fact]
    public void tone_checkboxes_start_from_the_current_selection()
    {
        foreach (var (tone, ticked) in Form.Tones)
            Assert.Equal(_app.Callouts!.SelectedTones.Contains(tone), ticked);
    }

    [Fact]
    public void accept_settings_applies_and_persists_the_tone_mix()
    {
        Form.SetTone("shy", true);
        Form.SetTone("dominant", true);
        Form.SetTone("flirty", false);

        Form.Apply();

        Assert.Equal(["shy", "dominant"], _app.Callouts!.SelectedTones);
        Assert.Equal(["shy", "dominant"], _settings.GetStringList("CalloutHandler/selected_tones")!);
    }

    // The handler falls back at draw time anyway - doing it here too keeps the form from claiming
    // nothing is active while the default tone is what actually speaks.
    [Fact]
    public void saving_with_no_tone_ticked_falls_back_to_the_default_tone()
    {
        foreach (var tone in Form.Tones.Keys.ToList()) Form.SetTone(tone, false);

        Form.Apply();

        Assert.Equal([CalloutHandler.DefaultTone], _app.Callouts!.SelectedTones);
        Assert.True(Form.Tones[CalloutHandler.DefaultTone]);
    }

    [Fact]
    public void callout_reset_button_resets_the_tone_mix()
    {
        Form.SetTone("degrading", true);

        Form.Reset("Callouts");

        foreach (var (tone, ticked) in Form.Tones)
            Assert.Equal(((string[])CalloutHandler.Defaults["selected_tones"]).Contains(tone), ticked);
    }

    // --- a language may ship only some tones ---

    [Fact]
    public void a_tone_the_current_language_lacks_is_greyed_out()
    {
        var missing = MissingInGerman();

        Form.Language = "de";

        Assert.False(Form.ToneUsable(missing));
        Assert.Contains("de", Form.ToneTooltip(missing));
        Assert.True(Form.ToneUsable(CalloutHandler.DefaultTone));
    }

    [Fact]
    public void switching_language_re_enables_the_tone()
    {
        var missing = MissingInGerman();
        Form.Language = "de";
        Assert.False(Form.ToneUsable(missing));

        Form.Language = "en";

        Assert.True(Form.ToneUsable(missing));
        Assert.Equal("", Form.ToneTooltip(missing));
    }

    // Greying must not quietly drop the tone from the saved mix - the user may well switch back to
    // the language that has it.
    [Fact]
    public void a_ticked_tone_survives_being_greyed_out()
    {
        var missing = MissingInGerman();
        Form.SetTone(missing, true);

        Form.Language = "de";
        Form.Apply();

        Assert.Contains(missing, _app.Callouts!.SelectedTones);
    }

    // Greyed out on its own only says "no" - the user still has to be told why, and once under the
    // grid beats a parenthetical on every second row.
    [Fact]
    public void a_hint_explains_the_greyed_out_tones()
    {
        Form.Language = "de";

        Assert.Contains("de", Form.ToneHint);
        Assert.StartsWith("Greyed out: no de phrases yet for ", Form.ToneHint);
    }

    [Fact]
    public void the_hint_goes_away_when_the_language_has_every_tone()
    {
        Form.Language = "en";

        Assert.Equal("", Form.ToneHint);
    }

    [Fact]
    public void the_outcome_question_can_be_turned_off_and_saved()
    {
        Assert.True(Form.Toggles["ask_for_outcome"].Value);

        Form.Toggles["ask_for_outcome"].Value = false;
        Form.Apply();

        Assert.False(_app.AskForOutcome);
        Assert.False(_settings.GetBool("GoonerApp/ask_for_outcome", true));
    }

    [Fact]
    public void the_edge_relief_settings_reach_the_beat_handler()
    {
        Assert.True(Form.Toggles["edge_relief_active"].Value);

        Number("edge_pause_dur").Value = 35;
        Number("edge_cooldown_sec").Value = 90;
        Form.Toggles["edge_relief_active"].Value = false;
        Form.Apply();

        Assert.Equal(35, _app.Beat.EdgePauseDur);
        Assert.Equal(90, _app.Beat.EdgeCooldownSec);
        Assert.False(_app.Beat.EdgeReliefActive);
        Assert.False(_settings.GetBool("BeatHandler/edge_relief_active", true));
    }

    // Applied immediately, like every other setting - waiting for the next session would leave a
    // button that does nothing.
    [Fact]
    public void switching_edge_relief_off_takes_the_button_away_at_once()
    {
        Form.Toggles["edge_relief_active"].Value = false;
        Form.Apply();

        Assert.False(_app.EdgeButtonVisible);
    }

    // --- the layout ---

    // The dialog's tabs in its order, and the engine's own Look behind them.
    [Fact]
    public void the_tabs_are_the_dialogs() =>
        Assert.Equal(["Playback", "Beat & Rhythm", "Climax", "Callouts", "Look"], Form.Tabs.Select(t => t.Title));

    // Every value the form holds is on some tab, once, and every tab ends with its reset button.
    [Fact]
    public void every_field_is_laid_out_exactly_once()
    {
        var numbers = Form.Tabs.SelectMany(t => t.Rows).OfType<NumberRow>().Select(r => r.Name).ToList();
        var toggles = Form.Tabs.SelectMany(t => t.Rows).OfType<ToggleRow>().Select(r => r.Name).ToList();
        var choices = Form.Tabs.SelectMany(t => t.Rows).OfType<ChoiceRow>().Select(r => r.Name).ToList();

        Assert.Equal(Form.Numbers.Keys.Order(), numbers.Order());
        Assert.Equal(Form.Toggles.Keys.Order(), toggles.Order());
        Assert.Equal(Form.Choices.Keys.Order(), choices.Order());
        Assert.All(Form.Tabs, tab => Assert.IsType<ResetRow>(tab.Rows[^1]));
    }

    // --- the look (the engine's own) ---

    [Fact]
    public void the_look_offers_every_choice_of_its_owner()
    {
        var rows = Form.Tabs.Single(t => t.Title == "Look").Rows;

        // Look's own choices, plus which character stands on the start screen.
        Assert.Equal(Look.Choices.Keys.Append("character").Order(), rows.OfType<ChoiceRow>().Select(r => r.Name).Order());
        Assert.Contains(new ToggleRow("focus_wanders"), rows);
        Assert.Equal(Look.Choices["note_track"], Form.Choices["note_track"].Options);
    }

    [Fact]
    public void no_character_is_applied_and_stored()
    {
        Form.Choices["character"].Value = CharacterChoice.None;

        Assert.True(Form.Apply());

        Assert.Null(_app.Character.Path);
        Assert.Equal(CharacterChoice.None, _settings.GetString("Character/file", ""));
    }

    [Fact]
    public void a_chosen_look_is_applied_and_stored()
    {
        Form.Choices["note_track"].Value = "ring";
        Form.Choices["censor"].Value = "blur";
        Form.Toggles["focus_wanders"].Value = false;

        Assert.True(Form.Apply());

        Assert.Equal("ring", _app.Look.NoteTrack);
        Assert.Equal("blur", _app.Look.Censor);
        Assert.False(_app.Look.FocusWanders);
        Assert.Equal("ring", _settings.GetString("Look/note_track", ""));
        Assert.Equal("blur", _settings.GetString("Look/censor", ""));
        Assert.False(_settings.GetBool("Look/focus_wanders", true));
    }

    [Fact]
    public void resetting_the_look_brings_back_its_defaults()
    {
        Form.Choices["color_scheme"].Value = "boudoir";
        Form.Toggles["focus_wanders"].Value = false;

        Form.Reset("Look");

        Assert.Equal("neon", Form.Choices["color_scheme"].Value);
        Assert.True(Form.Toggles["focus_wanders"].Value);
    }

    [Fact]
    public void a_choice_outside_its_options_is_not_taken()
    {
        Form.Choices["effects"].Value = "extreme";

        Assert.Equal("normal", Form.Choices["effects"].Value);
    }

    // --- the app's own language ---

    // Its own row at the top, not one of the tab's tunable values: the Playback tab's reset brings the
    // slideshow back to its defaults and leaves the language the user reads it all in alone.
    [Fact]
    public void the_app_language_is_offered_first()
    {
        var rows = Form.Tabs[0].Rows;

        Assert.IsType<HeaderRow>(rows[0]);
        Assert.IsType<UiLanguageRow>(rows[1]);
        Assert.Equal(["en", "de", "fr"], Form.UiLanguages.Select(o => o.Value));
        Assert.Equal("en", Form.UiLanguage);
    }

    [Fact]
    public void saving_another_app_language_switches_to_it()
    {
        Form.UiLanguage = "de";

        Assert.True(Form.Apply());

        Assert.Equal("de", _app.UiLanguage.Language);
        Assert.Equal("de", L10n.Language);
        Assert.Equal("de", _settings.GetString("Ui/language", ""));
    }

    [Fact]
    public void resetting_the_tab_keeps_the_app_language()
    {
        Form.UiLanguage = "fr";

        Form.Reset("Playback");

        Assert.Equal("fr", Form.UiLanguage);
    }

    [Fact]
    public void an_app_language_not_on_offer_is_not_taken()
    {
        Form.UiLanguage = "es";

        Assert.Equal("en", Form.UiLanguage);
    }
}
