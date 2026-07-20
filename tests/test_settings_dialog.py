import pytest

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


def test_accept_settings_recalculates_beat_when_running(app, dialog, monkeypatch):
    app.is_running = True
    called = {}
    monkeypatch.setattr(app.beat_handler, "recalc_beat", lambda: called.setdefault("called", True))

    dialog.accept_settings()

    assert called.get("called") is True


def test_accept_settings_does_not_recalculate_beat_when_stopped(app, dialog, monkeypatch):
    app.is_running = False
    monkeypatch.setattr(app.beat_handler, "recalc_beat", lambda: pytest.fail("should not recalc"))

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


def test_climax_fields_initialized_from_climax_handler(app, dialog):
    assert dialog.climax_active_checkbox.isChecked() == app.climax_handler.climax_active
    assert dialog.ruined_orgasm_active_checkbox.isChecked() == app.climax_handler.ruined_orgasm_active
    assert dialog.denied_orgasm_active_checkbox.isChecked() == app.climax_handler.denied_orgasm_active
    assert dialog.fake_climax_active_checkbox.isChecked() == app.climax_handler.fake_climax_active
    assert dialog.settings_fields["climax_chance"]["object"] is app.climax_handler
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
    dialog.settings_fields["climax_chance"]["widget"].setValue(0.42)

    dialog.accept_settings()

    assert app.climax_handler.climax_chance == pytest.approx(0.42)


# --- reset to defaults ---


def test_playback_reset_button_resets_fields(app, dialog):
    dialog.settings_fields["min_dur"]["widget"].setValue(9.9)
    dialog.settings_fields["max_dur"]["widget"].setValue(9.9)
    dialog.settings_fields["video_min_dur"]["widget"].setValue(9.9)
    dialog.settings_fields["beat_loudness"]["widget"].setValue(0.0)
    dialog.settings_fields["vid_loudness"]["widget"].setValue(0.0)
    dialog.show_startup_splash_checkbox.setChecked(not app.DEFAULTS["show_startup_splash"])
    dialog.show_record_chase_checkbox.setChecked(not app.DEFAULTS["show_record_chase"])

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
    dialog.settings_fields["climax_chance"]["widget"].setValue(0.99)
    dialog.climax_active_checkbox.setChecked(False)
    dialog.ruined_orgasm_active_checkbox.setChecked(True)
    dialog.denied_orgasm_active_checkbox.setChecked(True)
    dialog.fake_climax_active_checkbox.setChecked(False)

    dialog.climax_reset_button.click()

    climax_defaults = app.climax_handler.DEFAULTS
    assert dialog.settings_fields["climax_chance"]["widget"].value() == pytest.approx(
        climax_defaults["climax_chance"]
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
