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


def test_callout_selection_reflects_current_state(app, dialog):
    assert dialog.callout_selected_lang.currentText() == app.callout_handler.lang
    assert dialog.callout_active_checkbox.isChecked() == app.callout_handler.active_callout


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
