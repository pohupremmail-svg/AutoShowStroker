from unittest.mock import MagicMock

import pytest

from src.SettingsDialog import SettingsDialog


@pytest.fixture
def dialog(app, qtbot):
    dialog = SettingsDialog(app)
    qtbot.addWidget(dialog)
    return dialog


def test_device_tab_has_optional_connection_range_and_controls(dialog):
    tab = dialog.intiface_tab
    assert not tab.enabled.isChecked()
    assert tab.server.text() == "ws://127.0.0.1:12345"
    assert tab.minimum.value() == 10
    assert tab.maximum.value() == 90
    assert not tab.test_up.isEnabled()
    assert not tab.test_down.isEnabled()


def test_invalid_device_range_prevents_any_settings_save(app, dialog):
    dialog.intiface_tab.minimum.setValue(95)
    dialog.intiface_tab.maximum.setValue(20)
    dialog.settings_fields["min_dur"]["widget"].setValue(10)
    old = app.min_dur
    dialog.accept_settings()
    assert app.min_dur == old
    assert not app.intiface_controller.enabled


def test_device_settings_are_applied_on_save(app, dialog, monkeypatch):
    apply = MagicMock()
    monkeypatch.setattr(app.intiface_controller, "configure", apply)
    tab = dialog.intiface_tab
    tab.enabled.setChecked(True)
    tab.minimum.setValue(15)
    tab.maximum.setValue(85)
    dialog.accept_settings()
    apply.assert_called_once_with(True, "ws://127.0.0.1:12345", 0.15, 0.85)


def test_saving_unrelated_settings_does_not_interrupt_device_sync(app, dialog, monkeypatch):
    apply = MagicMock()
    monkeypatch.setattr(app.intiface_controller, "configure", apply)
    dialog.accept_settings()
    apply.assert_not_called()


@pytest.mark.parametrize("action", ["panic", "stop", "close"])
def test_stop_panic_and_exit_cancel_device_output_even_without_session(app, monkeypatch, action):
    stop = MagicMock()
    monkeypatch.setattr(app.intiface_controller, "emergency_stop", stop)
    getattr(app, action)()
    assert stop.called


def test_session_lifecycle_and_pauses_gate_device_sync(app):
    app.session_started_event.emit()
    assert app.intiface_controller.session_active
    app.beat_handler.beat_paused_event.emit()
    assert app.intiface_controller.paused
    app.beat_handler.beat_resumed_event.emit()
    assert not app.intiface_controller.paused
    app.session_ended_event.emit()
    assert not app.intiface_controller.session_active


def test_resume_uses_already_scheduled_next_target(app, monkeypatch):
    target = (42, True, 1234.0)
    monkeypatch.setattr(app.beat_handler, "next_linear_movement", lambda: target)
    resume, move = MagicMock(), MagicMock()
    monkeypatch.setattr(app.intiface_controller, "resume_sync", resume)
    monkeypatch.setattr(app.intiface_controller, "on_movement_planned", move)
    app.resume_intiface_sync()
    resume.assert_called_once()
    move.assert_called_once_with(*target)
