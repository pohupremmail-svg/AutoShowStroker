import pytest

from src.CalloutHandler import CalloutHandler
from src.SettingsDialog import SettingsDialog


@pytest.fixture
def dialog(app, qtbot):
    d = SettingsDialog(parent=app)
    qtbot.addWidget(d)
    return d


def test_settings_fields_initialized_from_target_object(app, dialog):
    assert dialog.settings_fields["min_dur"]["widget"].value() == app.min_dur
    assert dialog.settings_fields["min_dur"]["object"] is app
    assert dialog.settings_fields["min_dur"]["type"] is float

    assert dialog.settings_fields["min_pause_dur"]["type"] is int
    assert dialog.settings_fields["beat_loudness"]["object"] is app.beat_handler


def test_beat_selection_reflects_currently_selected_patterns(app, qtbot):
    app.beat_handler.selected_beat_patterns = ["Standard Beat"]

    dialog = SettingsDialog(parent=app)
    qtbot.addWidget(dialog)

    assert dialog.beat_checkboxes["Standard Beat"].isChecked() is True
    other = next(name for name in dialog.beat_checkboxes if name != "Standard Beat")
    assert dialog.beat_checkboxes[other].isChecked() is False


def test_manage_patterns_button_opens_editor_and_refreshes_selection(app, dialog, monkeypatch):
    opened = {}

    class FakePatternEditorDialog:
        def __init__(self, beat_handler, parent=None):
            opened["beat_handler"] = beat_handler
            beat_handler.add_or_update_custom_pattern("From Editor", [1, -1])

        def exec(self):
            return None

    monkeypatch.setattr("src.SettingsDialog.PatternEditorDialog", FakePatternEditorDialog)

    dialog.manage_patterns_button.click()

    assert opened["beat_handler"] is app.beat_handler
    assert "From Editor" in dialog.beat_checkboxes


def test_refresh_beat_selection_drops_deleted_custom_patterns(app, dialog):
    app.beat_handler.add_or_update_custom_pattern("Temp Pattern", [1, -1])
    dialog.refresh_beat_selection()
    assert "Temp Pattern" in dialog.beat_checkboxes

    app.beat_handler.delete_custom_pattern("Temp Pattern")
    dialog.refresh_beat_selection()
    assert "Temp Pattern" not in dialog.beat_checkboxes


def test_callout_selection_reflects_current_state(app, dialog):
    assert dialog.callout_selected_lang.currentText() == app.callout_handler.lang
    assert dialog.callout_active_checkbox.isChecked() == app.callout_handler.active_callout


def test_manage_phrase_files_button_opens_dialog(app, dialog, monkeypatch):
    opened = {}

    class FakeCustomPhraseFilesDialog:
        def __init__(self, callout_handler, parent=None):
            opened["callout_handler"] = callout_handler

        def exec(self):
            return None

    monkeypatch.setattr("src.SettingsDialog.CustomPhraseFilesDialog", FakeCustomPhraseFilesDialog)

    dialog.manage_phrase_files_button.click()

    assert opened["callout_handler"] is app.callout_handler


def test_accept_settings_applies_spinbox_values_to_target(app, dialog):
    dialog.settings_fields["min_dur"]["widget"].setValue(1.23)
    dialog.accept_settings()
    assert app.min_dur == pytest.approx(1.23)


def test_accept_settings_casts_int_fields(app, dialog):
    dialog.settings_fields["min_pause_dur"]["widget"].setValue(7.0)
    dialog.accept_settings()
    assert app.beat_handler.min_pause_dur == 7
    assert isinstance(app.beat_handler.min_pause_dur, int)


def test_accept_settings_persists_to_qsettings(app, dialog):
    dialog.settings_fields["min_dur"]["widget"].setValue(2.5)
    dialog.accept_settings()
    assert app.settings.value("GoonerApp/min_dur", type=float) == pytest.approx(2.5)


def test_accept_settings_updates_selected_beat_patterns(app, dialog):
    for name, checkbox in dialog.beat_checkboxes.items():
        checkbox.setChecked(name == "Standard Beat")

    dialog.accept_settings()

    assert app.beat_handler.selected_beat_patterns == ["Standard Beat"]
    assert app.settings.value("BeatHandler/selected_beat_patterns") == ["Standard Beat"]


def test_accept_settings_updates_callout_handler(app, dialog):
    dialog.callout_active_checkbox.setChecked(True)
    other_lang = next(lang for lang in app.callout_handler.available_languages if lang != app.callout_handler.lang)
    idx = dialog.callout_selected_lang.findText(other_lang)
    dialog.callout_selected_lang.setCurrentIndex(idx)

    dialog.accept_settings()

    assert app.callout_handler.active_callout is True
    assert app.callout_handler.lang == other_lang


def test_accept_settings_replans_when_running(app, dialog, monkeypatch):
    app.is_running = True
    called = {}
    monkeypatch.setattr(app.beat_handler, "replan_from_next_segment", lambda: called.setdefault("called", True))

    dialog.accept_settings()

    assert called.get("called") is True


def test_accept_settings_does_not_replan_when_stopped(app, dialog, monkeypatch):
    app.is_running = False
    monkeypatch.setattr(
        app.beat_handler, "replan_from_next_segment", lambda: pytest.fail("should not replan")
    )

    dialog.accept_settings()


def test_startup_splash_checkbox_initialized_from_app(app, dialog):
    assert dialog.show_startup_splash_checkbox.isChecked() == app.show_startup_splash


def test_accept_settings_updates_show_startup_splash(app, dialog):
    dialog.show_startup_splash_checkbox.setChecked(not app.show_startup_splash)
    expected = dialog.show_startup_splash_checkbox.isChecked()

    dialog.accept_settings()

    assert app.show_startup_splash == expected
    assert app.settings.value("GoonerApp/show_startup_splash", type=bool) == expected


def test_record_chase_checkbox_initialized_from_app(app, dialog):
    assert dialog.show_record_chase_checkbox.isChecked() == app.show_record_chase


def test_accept_settings_updates_show_record_chase(app, dialog, monkeypatch):
    called = {}
    monkeypatch.setattr(app, "_update_record_chase", lambda: called.setdefault("called", True))
    dialog.show_record_chase_checkbox.setChecked(not app.show_record_chase)
    expected = dialog.show_record_chase_checkbox.isChecked()

    dialog.accept_settings()

    assert app.show_record_chase == expected
    assert app.settings.value("GoonerApp/show_record_chase", type=bool) == expected
    assert called.get("called") is True


def test_session_timer_checkbox_initialized_from_app(app, dialog):
    assert dialog.show_session_timer_checkbox.isChecked() == app.show_session_timer


def test_accept_settings_updates_show_session_timer(app, dialog, monkeypatch):
    called = {}
    monkeypatch.setattr(app, "_update_session_timer", lambda: called.setdefault("called", True))
    dialog.show_session_timer_checkbox.setChecked(not app.show_session_timer)
    expected = dialog.show_session_timer_checkbox.isChecked()

    dialog.accept_settings()

    assert app.show_session_timer == expected
    assert app.settings.value("GoonerApp/show_session_timer", type=bool) == expected
    assert called.get("called") is True


def test_ramping_fields_initialized_from_beat_handler(app, dialog):
    assert dialog.ramping_active_checkbox.isChecked() == app.beat_handler.ramping_active
    assert dialog.settings_fields["min_ramp_duration"]["object"] is app.beat_handler
    assert dialog.settings_fields["max_ramp_duration"]["object"] is app.beat_handler
    assert dialog.settings_fields["ramp_window_width"]["object"] is app.beat_handler


def test_accept_settings_updates_ramping_active(app, dialog):
    dialog.ramping_active_checkbox.setChecked(not app.beat_handler.ramping_active)
    expected = dialog.ramping_active_checkbox.isChecked()

    dialog.accept_settings()

    assert app.beat_handler.ramping_active == expected
    assert app.settings.value("BeatHandler/ramping_active", type=bool) == expected


def test_climax_only_after_ramp_checkbox_initialized_from_handler(app, dialog):
    assert dialog.climax_only_after_ramp_checkbox.isChecked() == app.climax_handler.climax_only_after_ramp


def test_accept_settings_updates_climax_only_after_ramp(app, dialog):
    dialog.climax_only_after_ramp_checkbox.setChecked(not app.climax_handler.climax_only_after_ramp)
    expected = dialog.climax_only_after_ramp_checkbox.isChecked()

    dialog.accept_settings()

    assert app.climax_handler.climax_only_after_ramp == expected
    assert app.settings.value("ClimaxHandler/climax_only_after_ramp", type=bool) == expected


def test_climax_fields_initialized_from_climax_handler(app, dialog):
    assert dialog.climax_active_checkbox.isChecked() == app.climax_handler.climax_active
    assert dialog.ruined_orgasm_active_checkbox.isChecked() == app.climax_handler.ruined_orgasm_active
    assert dialog.denied_orgasm_active_checkbox.isChecked() == app.climax_handler.denied_orgasm_active
    assert dialog.fake_climax_active_checkbox.isChecked() == app.climax_handler.fake_climax_active
    assert dialog.settings_fields["min_climax_after"]["object"] is app.climax_handler
    assert dialog.settings_fields["ruined_orgasm_chance"]["object"] is app.climax_handler
    assert dialog.settings_fields["denied_orgasm_chance"]["object"] is app.climax_handler
    assert dialog.settings_fields["fake_climax_chance"]["object"] is app.climax_handler
    assert dialog.settings_fields["min_fake_climax_delay"]["object"] is app.climax_handler
    assert dialog.settings_fields["max_fake_climax_delay"]["object"] is app.climax_handler


def test_accept_settings_updates_climax_toggles(app, dialog):
    dialog.climax_active_checkbox.setChecked(not app.climax_handler.climax_active)
    dialog.ruined_orgasm_active_checkbox.setChecked(not app.climax_handler.ruined_orgasm_active)
    dialog.denied_orgasm_active_checkbox.setChecked(not app.climax_handler.denied_orgasm_active)
    dialog.fake_climax_active_checkbox.setChecked(not app.climax_handler.fake_climax_active)
    expected_climax = dialog.climax_active_checkbox.isChecked()
    expected_ruined = dialog.ruined_orgasm_active_checkbox.isChecked()
    expected_denied = dialog.denied_orgasm_active_checkbox.isChecked()
    expected_fake = dialog.fake_climax_active_checkbox.isChecked()

    dialog.accept_settings()

    assert app.climax_handler.climax_active == expected_climax
    assert app.climax_handler.ruined_orgasm_active == expected_ruined
    assert app.climax_handler.denied_orgasm_active == expected_denied
    assert app.climax_handler.fake_climax_active == expected_fake
    assert app.settings.value("ClimaxHandler/climax_active", type=bool) == expected_climax
    assert app.settings.value("ClimaxHandler/ruined_orgasm_active", type=bool) == expected_ruined
    assert app.settings.value("ClimaxHandler/denied_orgasm_active", type=bool) == expected_denied
    assert app.settings.value("ClimaxHandler/fake_climax_active", type=bool) == expected_fake


def test_accept_settings_applies_climax_spinbox_values(app, dialog):
    dialog.settings_fields["min_climax_after"]["widget"].setValue(90.0)

    dialog.accept_settings()

    assert app.climax_handler.min_climax_after == pytest.approx(90.0)


# --- reset to defaults ---


def test_playback_reset_button_resets_fields(app, dialog):
    dialog.settings_fields["min_dur"]["widget"].setValue(9.9)
    dialog.settings_fields["max_dur"]["widget"].setValue(9.9)
    dialog.settings_fields["video_min_dur"]["widget"].setValue(9.9)
    dialog.settings_fields["beat_loudness"]["widget"].setValue(0.0)
    dialog.settings_fields["vid_loudness"]["widget"].setValue(0.0)
    dialog.show_startup_splash_checkbox.setChecked(not app.DEFAULTS["show_startup_splash"])
    dialog.show_record_chase_checkbox.setChecked(not app.DEFAULTS["show_record_chase"])
    dialog.show_session_timer_checkbox.setChecked(not app.DEFAULTS["show_session_timer"])

    dialog.playback_reset_button.click()

    assert dialog.settings_fields["min_dur"]["widget"].value() == pytest.approx(app.DEFAULTS["min_dur"])
    assert dialog.settings_fields["max_dur"]["widget"].value() == pytest.approx(app.DEFAULTS["max_dur"])
    assert dialog.settings_fields["video_min_dur"]["widget"].value() == pytest.approx(
        app.DEFAULTS["video_min_dur"]
    )
    assert dialog.settings_fields["beat_loudness"]["widget"].value() == pytest.approx(
        app.beat_handler.DEFAULTS["beat_loudness"]
    )
    assert dialog.settings_fields["vid_loudness"]["widget"].value() == pytest.approx(app.DEFAULTS["vid_loudness"])
    assert dialog.show_startup_splash_checkbox.isChecked() == app.DEFAULTS["show_startup_splash"]
    assert dialog.show_record_chase_checkbox.isChecked() == app.DEFAULTS["show_record_chase"]
    assert dialog.show_session_timer_checkbox.isChecked() == app.DEFAULTS["show_session_timer"]


def test_beat_reset_button_resets_fields(app, dialog):
    dialog.settings_fields["min_beat_freq"]["widget"].setValue(19.0)
    dialog.settings_fields["ramp_window_width"]["widget"].setValue(1.0)
    dialog.ramping_active_checkbox.setChecked(False)
    other = next(name for name in dialog.beat_checkboxes if name != "Standard Beat")
    dialog.beat_checkboxes[other].setChecked(False)

    dialog.beat_reset_button.click()

    beat_defaults = app.beat_handler.DEFAULTS
    assert dialog.settings_fields["min_beat_freq"]["widget"].value() == pytest.approx(
        beat_defaults["min_beat_freq"]
    )
    assert dialog.settings_fields["ramp_window_width"]["widget"].value() == pytest.approx(
        beat_defaults["ramp_window_width"]
    )
    assert dialog.ramping_active_checkbox.isChecked() == beat_defaults["ramping_active"]
    assert all(checkbox.isChecked() for checkbox in dialog.beat_checkboxes.values())


def test_climax_reset_button_resets_fields(app, dialog):
    dialog.settings_fields["min_climax_after"]["widget"].setValue(990.0)
    dialog.climax_active_checkbox.setChecked(False)
    dialog.ruined_orgasm_active_checkbox.setChecked(True)
    dialog.denied_orgasm_active_checkbox.setChecked(True)
    dialog.fake_climax_active_checkbox.setChecked(False)

    dialog.climax_reset_button.click()

    climax_defaults = app.climax_handler.DEFAULTS
    assert dialog.settings_fields["min_climax_after"]["widget"].value() == pytest.approx(
        climax_defaults["min_climax_after"]
    )
    assert dialog.climax_active_checkbox.isChecked() == climax_defaults["climax_active"]
    assert dialog.ruined_orgasm_active_checkbox.isChecked() == climax_defaults["ruined_orgasm_active"]
    assert dialog.denied_orgasm_active_checkbox.isChecked() == climax_defaults["denied_orgasm_active"]
    assert dialog.fake_climax_active_checkbox.isChecked() == climax_defaults["fake_climax_active"]


def test_callout_reset_button_resets_fields(app, dialog):
    dialog.settings_fields["talking_chance"]["widget"].setValue(0.99)
    dialog.callout_active_checkbox.setChecked(True)
    other_lang = next(lang for lang in app.callout_handler.available_languages if lang != "en")
    idx = dialog.callout_selected_lang.findText(other_lang)
    dialog.callout_selected_lang.setCurrentIndex(idx)

    dialog.callout_reset_button.click()

    callout_defaults = app.callout_handler.DEFAULTS
    assert dialog.settings_fields["talking_chance"]["widget"].value() == pytest.approx(
        callout_defaults["talking_chance"]
    )
    assert dialog.callout_active_checkbox.isChecked() == callout_defaults["active_callout"]
    assert dialog.callout_selected_lang.currentText() == callout_defaults["lang"]


def test_reset_buttons_do_not_persist_until_save(app, dialog):
    dialog.settings_fields["min_dur"]["widget"].setValue(9.9)
    original = app.min_dur

    dialog.playback_reset_button.click()

    assert app.min_dur == original


# --- P0: settings that would crash the beat engine must not be saveable ---


@pytest.fixture
def rejected(dialog, monkeypatch):
    """Captures the validation message and whether the dialog closed."""
    seen = {}
    monkeypatch.setattr(dialog, "_show_validation_error", lambda msg: seen.setdefault("msg", msg))
    monkeypatch.setattr(dialog, "accept", lambda: seen.setdefault("accepted", True))
    return seen


def test_accept_settings_rejects_an_empty_rhythm_selection(app, dialog, rejected):
    before = list(app.beat_handler.selected_beat_patterns)
    for checkbox in dialog.beat_checkboxes.values():
        checkbox.setChecked(False)

    dialog.accept_settings()

    assert "msg" in rejected
    assert "accepted" not in rejected
    assert app.beat_handler.selected_beat_patterns == before


def test_accept_settings_rejects_inverted_pause_bounds(app, dialog, rejected):
    dialog.settings_fields["min_pause_dur"]["widget"].setValue(30)
    dialog.settings_fields["max_pause_dur"]["widget"].setValue(5)

    dialog.accept_settings()

    assert "msg" in rejected
    assert "accepted" not in rejected
    assert app.beat_handler.min_pause_dur != 30


# Driven from the dialog's own list rather than a copy of it: a copy is how the climax
# delay pair went unguarded for a commit, and a pair that is never exercised is exactly
# the one that ships inverted.
@pytest.mark.parametrize(
    ("min_name", "max_name"),
    [(pair[0], pair[1]) for pair in SettingsDialog.MIN_MAX_PAIRS],
    ids=[pair[2] for pair in SettingsDialog.MIN_MAX_PAIRS],
)
def test_accept_settings_rejects_every_inverted_min_max_pair(dialog, rejected, min_name, max_name):
    max_widget = dialog.settings_fields[max_name]["widget"]
    max_widget.setValue(max_widget.minimum())
    min_widget = dialog.settings_fields[min_name]["widget"]
    min_widget.setValue(min_widget.maximum())

    dialog.accept_settings()

    assert "accepted" not in rejected


def test_accept_settings_allows_min_equal_to_max(app, dialog, rejected):
    dialog.settings_fields["min_pause_dur"]["widget"].setValue(7)
    dialog.settings_fields["max_pause_dur"]["widget"].setValue(7)

    dialog.accept_settings()

    assert "msg" not in rejected
    assert app.beat_handler.min_pause_dur == 7
    assert app.beat_handler.max_pause_dur == 7


def test_settings_are_persisted_under_the_owner_group_constant(app, dialog):
    """The key used to come from __class__.__name__, with every read side hardcoding the
    same string separately - which is how two settings ended up written but never read."""
    dialog.settings_fields["min_pause_dur"]["widget"].setValue(9)
    dialog.settings_fields["max_pause_dur"]["widget"].setValue(11)

    dialog.accept_settings()

    # The literal keys, not f"{SETTINGS_GROUP}/..." - asserting against the constant would
    # hold no matter what the constant said, which is the thing being guarded.
    assert app.settings.value("BeatHandler/min_pause_dur") is not None
    assert app.settings.value("GoonerApp/min_dur") is not None


# --- P4: spinbox precision, and not disturbing a running pause ---


def test_every_spinbox_can_actually_reach_its_own_step(dialog):
    """QDoubleSpinBox defaults to 2 decimals. 'Pause chance' had a 0.001 step, so setValue
    rounded every arrow click straight back to where it started."""
    for var_name, data in dialog.settings_fields.items():
        widget = data["widget"]
        step = widget.singleStep()
        rounded = round(step, widget.decimals())
        assert rounded == step, f"{var_name}: step {step} is finer than {widget.decimals()} decimals"


def test_saving_leaves_the_running_segment_alone(app, dialog, tmp_path):
    """Cutting the beat the user is currently following out from under them, just to prove
    the save landed, is worse than letting it play out."""
    app.playlist = [tmp_path / "a.png"]
    app.start()
    running = app.beat_handler.current_segment

    dialog.accept_settings()

    assert app.beat_handler.current_segment is running


def test_saving_applies_the_new_values_to_everything_still_queued(app, dialog, tmp_path):
    app.playlist = [tmp_path / "a.png"]
    app.start()
    dialog.settings_fields["min_beat_dur"]["widget"].setValue(30.0)
    dialog.settings_fields["max_beat_dur"]["widget"].setValue(30.0)

    dialog.accept_settings()

    assert all(s.duration_sec == 30.0 for s in app.beat_handler.planned_segments if s.kind == "beat")


def test_saving_during_a_pause_lets_the_pause_finish(app, dialog, tmp_path):
    app.playlist = [tmp_path / "a.png"]
    app.start()
    app.beat_handler.start_pause()
    assert app.beat_handler.is_paused()

    dialog.accept_settings()

    assert app.beat_handler.is_paused()


# --- callout tones (second axis next to language) ---


def test_tone_checkboxes_offer_every_available_tone(app, dialog):
    assert list(dialog.tone_checkboxes) == app.callout_handler.available_tones


def test_tone_checkboxes_are_labelled_not_keyed(app, dialog):
    assert dialog.tone_checkboxes["girlfriend"].text() == "Girlfriend Experience"


def test_tone_checkboxes_start_from_the_current_selection(app, dialog):
    for tone, checkbox in dialog.tone_checkboxes.items():
        assert checkbox.isChecked() == (tone in app.callout_handler.selected_tones)


def test_accept_settings_applies_and_persists_the_tone_mix(app, dialog):
    dialog.tone_checkboxes["shy"].setChecked(True)
    dialog.tone_checkboxes["dominant"].setChecked(True)
    dialog.tone_checkboxes["flirty"].setChecked(False)

    dialog.accept_settings()

    assert app.callout_handler.selected_tones == ["shy", "dominant"]
    assert app.settings.value("CalloutHandler/selected_tones") == ["shy", "dominant"]


def test_saving_with_no_tone_ticked_falls_back_to_the_default_tone(app, dialog):
    """The handler falls back at draw time anyway - doing it here too keeps the dialog
    from claiming nothing is active while the default tone is what actually speaks."""
    for checkbox in dialog.tone_checkboxes.values():
        checkbox.setChecked(False)

    dialog.accept_settings()

    assert app.callout_handler.selected_tones == [CalloutHandler.DEFAULT_TONE]
    assert dialog.tone_checkboxes[CalloutHandler.DEFAULT_TONE].isChecked() is True


def test_callout_reset_button_resets_the_tone_mix(app, dialog):
    dialog.tone_checkboxes["degrading"].setChecked(True)

    dialog.callout_reset_button.click()

    for tone, checkbox in dialog.tone_checkboxes.items():
        assert checkbox.isChecked() == (tone in CalloutHandler.DEFAULTS["selected_tones"])


# --- a language may ship only some tones ---


def test_a_tone_the_current_language_lacks_is_greyed_out(app, dialog):
    de_tones = app.callout_handler.tones_for("de")
    missing = next(t for t in app.callout_handler.available_tones if t not in de_tones)

    dialog.callout_selected_lang.setCurrentText("de")

    assert dialog.tone_checkboxes[missing].isEnabled() is False
    assert "de" in dialog.tone_checkboxes[missing].toolTip()
    assert dialog.tone_checkboxes[CalloutHandler.DEFAULT_TONE].isEnabled() is True


def test_the_label_stays_clean_when_a_tone_is_unavailable(app, dialog):
    """The grid is read at a glance - a parenthetical on half the rows is noise where
    the greyed-out state already says it."""
    de_tones = app.callout_handler.tones_for("de")
    missing = next(t for t in app.callout_handler.available_tones if t not in de_tones)

    dialog.callout_selected_lang.setCurrentText("de")

    assert dialog.tone_checkboxes[missing].text() == app.callout_handler.tone_label(missing)


def test_switching_language_re_enables_the_tone(app, dialog):
    de_tones = app.callout_handler.tones_for("de")
    missing = next(t for t in app.callout_handler.available_tones if t not in de_tones)

    dialog.callout_selected_lang.setCurrentText("de")
    assert dialog.tone_checkboxes[missing].isEnabled() is False

    dialog.callout_selected_lang.setCurrentText("en")
    assert dialog.tone_checkboxes[missing].isEnabled() is True
    assert dialog.tone_checkboxes[missing].toolTip() == ""


def test_a_ticked_tone_survives_being_greyed_out(app, dialog):
    """Greying must not quietly drop the tone from the saved mix - the user may well
    switch back to the language that has it."""
    de_tones = app.callout_handler.tones_for("de")
    missing = next(t for t in app.callout_handler.available_tones if t not in de_tones)
    dialog.tone_checkboxes[missing].setChecked(True)

    dialog.callout_selected_lang.setCurrentText("de")
    dialog.accept_settings()

    assert missing in app.callout_handler.selected_tones


def test_a_hint_explains_the_greyed_out_tones(app, dialog):
    """Greyed out on its own only says 'no' - the user still has to be told why, and
    once under the grid beats a parenthetical on every second row."""
    dialog.callout_selected_lang.setCurrentText("de")

    assert dialog.tone_hint.isVisible() or dialog.tone_hint.text()
    assert "de" in dialog.tone_hint.text()


def test_the_hint_goes_away_when_the_language_has_every_tone(app, dialog):
    dialog.callout_selected_lang.setCurrentText("en")

    assert dialog.tone_hint.text() == ""


def test_the_save_button_shows_its_ampersand(dialog):
    """Qt reads a single & as a mnemonic prefix and swallows it - the button read
    'Save  Close Settings' with a hole in the middle. && is the literal one."""
    assert dialog.button_ok.text() == "Save && Close Settings"


def test_the_outcome_question_can_be_turned_off_and_saved(app, dialog):
    assert dialog.ask_for_outcome_checkbox.isChecked() is True

    dialog.ask_for_outcome_checkbox.setChecked(False)
    dialog.accept_settings()

    assert app.ask_for_outcome is False
    assert app.settings.value("GoonerApp/ask_for_outcome", type=bool) is False


def test_the_edge_relief_settings_reach_the_beat_handler(app, dialog):
    assert dialog.edge_relief_active_checkbox.isChecked() is True

    dialog.settings_fields["edge_pause_dur"]["widget"].setValue(35)
    dialog.settings_fields["edge_cooldown_sec"]["widget"].setValue(90)
    dialog.edge_relief_active_checkbox.setChecked(False)
    dialog.accept_settings()

    assert app.beat_handler.edge_pause_dur == 35
    assert app.beat_handler.edge_cooldown_sec == 90
    assert app.beat_handler.edge_relief_active is False
    assert app.settings.value("BeatHandler/edge_relief_active", type=bool) is False


def test_switching_edge_relief_off_takes_the_button_away_at_once(app, dialog):
    """Applied immediately, like every other setting - waiting for the next session would
    leave a button that does nothing."""
    dialog.edge_relief_active_checkbox.setChecked(False)
    dialog.accept_settings()

    assert app.btn_edge.isVisible() is False
