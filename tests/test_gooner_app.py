import os
import sys
import time
from unittest.mock import MagicMock

import pytest
from PyQt6.QtMultimedia import QMediaPlayer
from PyQt6.QtWidgets import QDialog

from src import applog, session_files
from src.BeatTrackWidget import BeatTrackWidget
from src.GoonerApp import GoonerApp
from src.SessionScript import SessionScript


class _FakeDialogBase:
    """Base for stand-in dialogs.

    GoonerApp releases every dialog it opens with deleteLater() so instances don't pile up
    as children of the window - a fake without it just raises AttributeError.
    """

    def deleteLater(self):
        pass


# --- fullscreen ---


def test_leaving_fullscreen_restores_maximized_state(app):
    app.showMaximized()
    assert app.isMaximized()

    app._enter_fullscreen()
    assert app.isFullScreen()

    app._leave_fullscreen()
    assert not app.isFullScreen()
    assert app.isMaximized()


def test_leaving_fullscreen_from_normal_restores_normal_state(app):
    app.showNormal()
    assert not app.isMaximized()

    app._enter_fullscreen()
    assert app.isFullScreen()

    app._leave_fullscreen()
    assert not app.isFullScreen()
    assert not app.isMaximized()


def test_entering_fullscreen_hides_controls(app):
    app._enter_fullscreen()
    assert app.controls_container.isHidden()


def test_leaving_fullscreen_shows_controls_again(app):
    app._enter_fullscreen()
    app._leave_fullscreen()
    assert not app.controls_container.isHidden()


# --- mute / panic ---


def test_set_muted_mutes_video_and_beat_audio(app):
    app.set_muted(True)
    assert app.is_muted is True
    assert app.audio_output.isMuted() is True
    assert app.beat_handler.is_muted is True
    assert app.btn_mute.isChecked() is True
    assert app.btn_mute.text() == "Unmute"

    app.set_muted(False)
    assert app.is_muted is False
    assert app.audio_output.isMuted() is False
    assert app.beat_handler.is_muted is False
    assert app.btn_mute.isChecked() is False
    assert app.btn_mute.text() == "Mute"


def test_mute_button_click_toggles_mute(app):
    app.btn_mute.click()
    assert app.is_muted is True

    app.btn_mute.click()
    assert app.is_muted is False


def test_m_key_toggles_mute(app, monkeypatch, qtbot):
    from PyQt6.QtCore import Qt

    called = {"count": 0}
    monkeypatch.setattr(app, "toggle_mute", lambda: called.__setitem__("count", called["count"] + 1))

    qtbot.keyClick(app, Qt.Key.Key_M)

    assert called["count"] == 1


def test_toggle_mute_flips_state(app):
    assert app.is_muted is False

    app.toggle_mute()
    assert app.is_muted is True

    app.toggle_mute()
    assert app.is_muted is False


def test_panic_mutes_and_minimizes(app, monkeypatch):
    minimized = {}
    monkeypatch.setattr(app, "showMinimized", lambda: minimized.setdefault("called", True))

    app.panic()

    assert app.is_muted is True
    assert minimized.get("called") is True


def test_space_key_triggers_panic(app, monkeypatch, qtbot):
    from PyQt6.QtCore import Qt

    called = {}
    monkeypatch.setattr(app, "panic", lambda: called.setdefault("panicked", True))

    qtbot.keyClick(app, Qt.Key.Key_Space)

    assert called.get("panicked") is True


def test_control_buttons_are_not_keyboard_focusable(app):
    from PyQt6.QtCore import Qt

    for button in (app.btn_prev, app.btn_load, app.btn_next, app.btn_stop, app.btn_mute):
        assert button.focusPolicy() == Qt.FocusPolicy.NoFocus


# --- folder scanning ---


# --- open_folder ---


def _fake_picker_dialog(exec_result, selected_files=None):
    class FakeDialog(_FakeDialogBase):
        def __init__(self, parent=None):
            self.selected_files = selected_files or []

        def exec(self):
            return exec_result

    return FakeDialog


def test_open_folder_cancelled_leaves_playlist_untouched(app, monkeypatch):
    monkeypatch.setattr(
        "src.GoonerApp.MediaFolderPickerDialog", _fake_picker_dialog(QDialog.DialogCode.Rejected)
    )
    app.open_folder()
    assert app.playlist == []
    assert app.is_running is False


def test_open_folder_no_supported_files_shows_message_and_stays_stopped(app, monkeypatch):
    monkeypatch.setattr(
        "src.GoonerApp.MediaFolderPickerDialog", _fake_picker_dialog(QDialog.DialogCode.Accepted, [])
    )
    app.open_folder()
    assert app.image_label.text() == "No supported files found."
    assert app.is_running is False


def test_open_folder_with_files_starts_session(app, monkeypatch, tmp_path):
    files = [tmp_path / "a.png", tmp_path / "b.png"]
    monkeypatch.setattr(
        "src.GoonerApp.MediaFolderPickerDialog", _fake_picker_dialog(QDialog.DialogCode.Accepted, files)
    )

    app.open_folder()

    assert {p.name for p in app.playlist} == {"a.png", "b.png"}
    assert app.current_index == 0
    assert app.is_running is True


def test_open_folder_with_files_hides_climax_banner(app, monkeypatch, tmp_path):
    old = tmp_path / "old.png"
    old.write_bytes(b"")
    app.playlist = [old]
    app.start()
    app._update_climax_status_label("ruined")
    assert app.climax_blink_timer.isActive()

    monkeypatch.setattr(
        "src.GoonerApp.MediaFolderPickerDialog",
        _fake_picker_dialog(QDialog.DialogCode.Accepted, [tmp_path / "a.png"]),
    )
    app.open_folder()

    assert app.climax_status_label.isHidden()
    assert app.climax_status_label.text() == ""
    assert not app.climax_blink_timer.isActive()


def test_open_folder_no_supported_files_hides_climax_banner(app, monkeypatch, tmp_path):
    old = tmp_path / "old.png"
    old.write_bytes(b"")
    app.playlist = [old]
    app.start()
    app._update_climax_status_label("denied")

    monkeypatch.setattr(
        "src.GoonerApp.MediaFolderPickerDialog", _fake_picker_dialog(QDialog.DialogCode.Accepted, [])
    )
    app.open_folder()

    assert app.climax_status_label.isHidden()
    assert not app.climax_blink_timer.isActive()


# --- playlist navigation ---


def test_show_next_wraps_around_playlist(app, tmp_path):
    files = [tmp_path / f"{i}.png" for i in range(3)]
    for f in files:
        f.write_bytes(b"")
    app.playlist = files
    app.current_index = 2

    app.show_next()

    assert app.current_index == 0


def test_show_prev_wraps_around_playlist(app, tmp_path):
    files = [tmp_path / f"{i}.png" for i in range(3)]
    for f in files:
        f.write_bytes(b"")
    app.playlist = files
    app.current_index = 0

    app.show_prev()

    assert app.current_index == 2


def test_show_next_noop_on_empty_playlist(app):
    app.playlist = []
    app.current_index = 0
    app.show_next()
    assert app.current_index == 0


def test_load_current_index_noop_on_empty_playlist(app):
    app.playlist = []
    app.load_current_index()


# --- load_media dispatch ---


def test_load_media_image_extension_shows_image_label(app, tmp_path):
    img = tmp_path / "pic.png"
    img.write_bytes(b"")

    app.load_media(str(img))

    assert app.media_stack.currentWidget() is app.image_label


def test_load_media_gif_extension_shows_image_label_and_sets_movie(app, tmp_path):
    gif = tmp_path / "clip.gif"
    gif.write_bytes(b"")

    app.load_media(str(gif))

    assert app.media_stack.currentWidget() is app.image_label
    assert app.current_movie is not None


def test_load_media_video_extension_switches_to_video_widget(app, monkeypatch, tmp_path):
    fake_player = MagicMock()
    monkeypatch.setattr(app, "media_player", fake_player)
    monkeypatch.setattr(app, "audio_output", MagicMock())

    video = tmp_path / "clip.mp4"
    video.write_bytes(b"")

    app.load_media(str(video))

    assert app.media_stack.currentWidget() is app.video_widget
    fake_player.setSource.assert_called_once()
    fake_player.play.assert_called_once()


# --- video_status_changed ---


def test_video_status_changed_replays_if_below_min_duration(app, monkeypatch):
    fake_player = MagicMock()
    monkeypatch.setattr(app, "media_player", fake_player)
    app.is_running = True
    app.video_min_dur = 5.0
    app.video_start_time = 100.0
    monkeypatch.setattr(time, "time", lambda: 102.0)

    app.video_status_changed(QMediaPlayer.MediaStatus.EndOfMedia)

    fake_player.play.assert_called_once()


def test_video_status_changed_advances_if_above_min_duration(app, monkeypatch):
    app.is_running = True
    app.video_min_dur = 1.0
    app.video_start_time = 100.0
    monkeypatch.setattr(time, "time", lambda: 105.0)
    advanced = {}
    monkeypatch.setattr(app, "show_next", lambda: advanced.setdefault("called", True))

    app.video_status_changed(QMediaPlayer.MediaStatus.EndOfMedia)

    assert advanced.get("called") is True


def test_video_status_changed_ignores_other_statuses(app, monkeypatch):
    monkeypatch.setattr(app, "show_next", lambda: pytest.fail("should not advance"))
    app.video_status_changed(QMediaPlayer.MediaStatus.LoadingMedia)


# --- start / stop lifecycle ---


def test_start_enables_controls_and_emits_session_started(app, qtbot, tmp_path):
    img = tmp_path / "a.png"
    img.write_bytes(b"")
    app.playlist = [img]

    with qtbot.waitSignal(app.session_started_event, timeout=1000):
        app.start()

    assert app.is_running is True
    assert app.btn_next.isEnabled()
    assert app.btn_prev.isEnabled()
    assert app.btn_stop.isEnabled()
    assert app.btn_load.text() == "Change Gooning Folder."


def test_start_when_already_running_does_not_reemit_session_started(app, qtbot, tmp_path):
    img = tmp_path / "a.png"
    img.write_bytes(b"")
    app.playlist = [img]
    app.start()

    with qtbot.assertNotEmitted(app.session_started_event, wait=200):
        app.start()


def test_stop_disables_controls_and_emits_session_ended(app, qtbot, tmp_path):
    img = tmp_path / "a.png"
    img.write_bytes(b"")
    app.playlist = [img]
    app.start()

    with qtbot.waitSignal(app.session_ended_event, timeout=1000):
        app.stop()

    assert app.is_running is False
    assert not app.btn_next.isEnabled()
    assert not app.btn_prev.isEnabled()
    assert not app.btn_stop.isEnabled()
    assert app.btn_load.text() == "Set Gooning Folder and Start."


def test_stop_when_not_running_is_noop(app, qtbot):
    with qtbot.assertNotEmitted(app.session_ended_event, wait=200):
        app.stop()


def test_stop_freezes_climax_banner_without_hiding_it(app, tmp_path):
    img = tmp_path / "a.png"
    img.write_bytes(b"")
    app.playlist = [img]
    app.start()
    app._update_climax_status_label("cum")
    assert app.climax_blink_timer.isActive()

    app.stop()

    assert not app.climax_status_label.isHidden()
    assert app.climax_status_label.text() == "CUM"
    assert not app.climax_blink_timer.isActive()
    on_color, _off_color = app.CLIMAX_STATUS_COLORS["cum"]
    assert on_color in app.climax_status_label.styleSheet()


def test_stop_when_no_climax_outcome_active_is_still_safe(app, tmp_path):
    # climax_active/fake_climax_active must be off, not just left at their random-chance
    # defaults - on_beat_change() rolls for a fake climax on every beat_change_event,
    # including the one start() fires immediately, so leaving these on made this test flaky
    # (~5% of runs): a climax_status_label the test never expected made it through to stop().
    app.climax_handler.climax_active = False
    app.climax_handler.fake_climax_active = False

    img = tmp_path / "a.png"
    img.write_bytes(b"")
    app.playlist = [img]
    app.start()

    app.stop()

    assert app.climax_status_label.isHidden()
    assert not app.climax_blink_timer.isActive()


# --- next/prev buttons ---


def test_btn_next_action_advances_and_emits_skip_event(app, qtbot, tmp_path):
    files = [tmp_path / f"{i}.png" for i in range(2)]
    for f in files:
        f.write_bytes(b"")
    app.playlist = files
    app.current_index = 0

    with qtbot.waitSignal(app.media_skipped_event, timeout=1000):
        app.btn_next_action()

    assert app.current_index == 1


def test_btn_prev_action_goes_back_and_emits_repeat_event(app, qtbot, tmp_path):
    files = [tmp_path / f"{i}.png" for i in range(2)]
    for f in files:
        f.write_bytes(b"")
    app.playlist = files
    app.current_index = 1

    with qtbot.waitSignal(app.media_repeated_event, timeout=1000):
        app.btn_prev_action()

    assert app.current_index == 0


# --- callout label ---


def test_display_new_tease_shows_label_with_text(app):
    app.display_new_tease("hello")
    assert app.callout_label.text() == "hello"
    assert not app.callout_label.isHidden()


def test_hide_last_tease_hides_and_clears_label(app):
    app.display_new_tease("hello")
    app.hide_last_tease()
    assert app.callout_label.text() == ""
    assert app.callout_label.isHidden()


# --- climax outcome ---


def test_on_climax_outcome_denied_schedules_stop(app):
    from src.GoonerApp import DENIED_STOP_DELAY_MS

    app._on_climax_outcome("denied")

    assert app._denied_stop_timer.isActive()
    assert app._denied_stop_timer.interval() == DENIED_STOP_DELAY_MS


@pytest.mark.parametrize("outcome", ["real", "ruined"])
def test_on_climax_outcome_non_denied_does_not_schedule_stop(app, outcome):
    app._on_climax_outcome(outcome)

    assert not app._denied_stop_timer.isActive()


# --- climax status banner ---


def test_footer_container_has_fixed_height(app):
    # Total footer height must never change, or the media area above it wobbles whenever
    # the climax label appears/disappears - only the internal split (label vs. beat_meter)
    # changes, via stretch factors.
    assert app.footer_container.minimumHeight() == app.footer_container.maximumHeight()
    assert app.footer_container.minimumHeight() > 0


def test_beat_meter_gets_stretch_priority_over_climax_label(app):
    # climax_status_label: index 0, stretch 0 (fixed to its own size hint when visible, 0
    # space when hidden). beat_meter: index 1, stretch 1 (absorbs whatever space the label
    # isn't using) - this is what lets the Strokebar reclaim full height when idle.
    assert app.footer_layout.stretch(app.footer_layout.indexOf(app.climax_status_label)) == 0
    assert app.footer_layout.stretch(app.footer_layout.indexOf(app.outcome_row)) == 0
    assert app.footer_layout.stretch(app.footer_layout.indexOf(app.beat_track)) == 1


def test_climax_status_label_hidden_by_default(app):
    assert app.climax_status_label.isHidden()
    assert app.climax_status_label.text() == ""
    assert not app.climax_blink_timer.isActive()


def test_update_climax_status_label_shows_cum(app):
    app._update_climax_status_label("cum")
    assert app.climax_status_label.text() == "CUM"
    assert not app.climax_status_label.isHidden()
    assert app.climax_blink_timer.isActive()


def test_update_climax_status_label_shows_ruined(app):
    app._update_climax_status_label("ruined")
    assert app.climax_status_label.text() == "RUINED"
    assert not app.climax_status_label.isHidden()
    assert app.climax_blink_timer.isActive()


def test_update_climax_status_label_shows_denied(app):
    app._update_climax_status_label("denied")
    assert app.climax_status_label.text() == "DENIED"
    assert not app.climax_status_label.isHidden()
    assert app.climax_blink_timer.isActive()


def test_update_climax_status_label_neutral_hides_and_stops_blink(app):
    app._update_climax_status_label("cum")
    app._update_climax_status_label("neutral")
    assert app.climax_status_label.isHidden()
    assert app.climax_status_label.text() == ""
    assert not app.climax_blink_timer.isActive()


def test_toggle_climax_blink_alternates_colors_keeps_text(app):
    app._update_climax_status_label("cum")
    on_color, off_color = app.CLIMAX_STATUS_COLORS["cum"]
    assert on_color in app.climax_status_label.styleSheet()
    assert app.climax_status_label.text() == "CUM"

    app._toggle_climax_blink()
    assert off_color in app.climax_status_label.styleSheet()
    assert app.climax_status_label.text() == "CUM"  # text never clears - only the color blinks

    app._toggle_climax_blink()
    assert on_color in app.climax_status_label.styleSheet()
    assert app.climax_status_label.text() == "CUM"


def test_climax_blink_interval_is_fast(app):
    assert app.climax_blink_timer.interval() <= 150


# --- beat track (GoonerApp owns the widget, BeatHandler only emits state) ---


def test_beat_track_starts_idle(app):
    assert app.beat_track._kind == "idle"
    assert app.beat_track._caption == "Strokemeter appears here."


def test_update_beat_track_sets_caption_and_kind(app):
    for kind, text in [("new_beat", "New Beat! [1]"), ("pause", "Pause: 5s")]:
        app._update_beat_track(text, kind)
        assert app.beat_track._caption == text
        assert app.beat_track._kind == kind


def test_beat_handler_meter_updates_reach_the_gooner_app_owned_widget(app):
    app._update_beat_track("New Beat! [1]", "new_beat")

    app.beat_handler.toggle_blink()

    # The blink kinds drive the track's state but must not clobber the real caption.
    assert app.beat_track._kind in ("up", "down")
    assert app.beat_track._caption == "New Beat! [1]"


def test_beat_event_flashes_the_beat_track(app):
    app.beat_handler.beat_event.emit()
    assert app.beat_track.is_flashing() is True


def test_beat_change_sweeps_the_beat_track(app):
    # A new segment re-seeds the whole prediction, so every note on screen jumps at
    # once - the sweep is what covers that reset.
    assert app.beat_track._change_progress() is None

    app.beat_handler.selected_beat_patterns = ["Standard Beat"]
    app.beat_handler.start_beat()

    assert app.beat_track._change_progress() is not None


def test_beat_track_animates_only_while_a_session_runs(app, tmp_path):
    img = tmp_path / "a.png"
    img.write_bytes(b"")
    app.playlist = [img]
    assert app.beat_track.frame_timer.isActive() is False

    app.start()
    assert app.beat_track.frame_timer.isActive() is True

    app.stop()
    assert app.beat_track.frame_timer.isActive() is False


def test_beat_track_reads_upcoming_beats_from_the_handler(app):
    app.beat_handler.selected_beat_patterns = ["Standard Beat"]
    app.beat_handler.start_beat()

    upcoming = app.beat_track.beat_handler.upcoming_beats(BeatTrackWidget.LEAD_TIME_SEC)

    assert app.beat_track.beat_handler is app.beat_handler
    assert upcoming


# --- live record-chase ---


def test_record_chase_label_hidden_by_default(app):
    assert app.record_chase_label.isHidden()


def test_show_record_chase_defaults_to_true(app):
    assert app.show_record_chase is True


def test_starting_session_does_not_show_record_chase_below_threshold(app, tmp_path):
    app.score_tracker.history = [{"total_num_beat": 100}]
    img = tmp_path / "a.png"
    img.write_bytes(b"")
    app.playlist = [img]

    app.start()

    assert app.record_chase_label.isHidden()


def test_record_chase_label_shows_once_threshold_crossed(app, tmp_path):
    app.score_tracker.history = [{"total_num_beat": 100}]
    img = tmp_path / "a.png"
    img.write_bytes(b"")
    app.playlist = [img]
    app.start()

    app.score_tracker.beat_count = 90
    app._update_record_chase()

    assert not app.record_chase_label.isHidden()
    assert "Total Beats" in app.record_chase_label.text()
    assert "90" in app.record_chase_label.text()


def test_beat_event_wired_to_record_chase_update(app, tmp_path):
    app.score_tracker.history = [{"total_num_beat": 1}]
    img = tmp_path / "a.png"
    img.write_bytes(b"")
    app.playlist = [img]
    app.start()

    app.beat_handler.beat_event.emit()

    assert app.score_tracker.beat_count == 1
    assert not app.record_chase_label.isHidden()
    assert "New Total Beats Record!" in app.record_chase_label.text()


def test_record_chase_label_hidden_when_setting_disabled(app, tmp_path):
    app.score_tracker.history = [{"total_num_beat": 100}]
    app.show_record_chase = False
    img = tmp_path / "a.png"
    img.write_bytes(b"")
    app.playlist = [img]
    app.start()

    app.score_tracker.beat_count = 90
    app._update_record_chase()

    assert app.record_chase_label.isHidden()


def test_stopping_session_hides_record_chase_label(app, tmp_path):
    app.score_tracker.history = [{"total_num_beat": 100}]
    img = tmp_path / "a.png"
    img.write_bytes(b"")
    app.playlist = [img]
    app.start()
    app.score_tracker.beat_count = 90
    app._update_record_chase()
    assert not app.record_chase_label.isHidden()

    app.stop()

    assert app.record_chase_label.isHidden()


# --- session timer ---


def test_session_timer_label_hidden_by_default(app):
    assert app.session_timer_label.isHidden()


def test_show_session_timer_defaults_to_true(app):
    assert app.show_session_timer is True


def test_starting_session_shows_session_timer_at_zero(app, tmp_path):
    img = tmp_path / "a.png"
    img.write_bytes(b"")
    app.playlist = [img]

    app.start()

    assert not app.session_timer_label.isHidden()
    assert "00:00" in app.session_timer_label.text()


def test_session_timer_reflects_elapsed_time(app, tmp_path):
    img = tmp_path / "a.png"
    img.write_bytes(b"")
    app.playlist = [img]
    app.start()

    app.score_tracker.session_start_time -= 522

    app._update_session_timer()

    assert "08:42" in app.session_timer_label.text()


def test_session_timer_hidden_when_setting_disabled(app, tmp_path):
    app.show_session_timer = False
    img = tmp_path / "a.png"
    img.write_bytes(b"")
    app.playlist = [img]

    app.start()

    assert app.session_timer_label.isHidden()


def test_stopping_session_hides_session_timer_label(app, tmp_path):
    img = tmp_path / "a.png"
    img.write_bytes(b"")
    app.playlist = [img]
    app.start()
    assert not app.session_timer_label.isHidden()

    app.stop()

    assert app.session_timer_label.isHidden()


def test_stopping_session_stops_the_session_timer_tick(app, tmp_path):
    img = tmp_path / "a.png"
    img.write_bytes(b"")
    app.playlist = [img]
    app.start()
    assert app.session_timer_tick.isActive()

    app.stop()

    assert not app.session_timer_tick.isActive()


def test_climax_handler_status_event_wired_to_label(app):
    app.climax_handler.status_changed_event.emit("ruined")
    assert app.climax_status_label.text() == "RUINED"


def test_climax_handler_fake_climax_event_wired_to_score_tracker(app):
    app.climax_handler.fake_climax_triggered_event.emit()
    assert app.score_tracker.fakeout_count == 1


def test_score_tracker_uses_app_settings(app):
    assert app.score_tracker.settings is app.settings


def test_show_statistics_passes_new_records(app, monkeypatch):
    app.score_tracker.last_session_new_records = {"total_dur_sec": 42.0}
    captured = {}

    class FakeDialog(_FakeDialogBase):
        def __init__(self, stats_data, new_records=None, **kwargs):
            captured["new_records"] = new_records

        def exec(self):
            pass

    monkeypatch.setattr("src.GoonerApp.StatisticsDialog", FakeDialog)

    app.show_statistics()

    assert captured["new_records"] == {"total_dur_sec": 42.0}


def test_statistics_menu_has_long_term_statistics_action(app, monkeypatch):
    from PyQt6.QtWidgets import QMenu

    captured = {}

    class FakeDialog(_FakeDialogBase):
        def __init__(self, history, all_time_bests, parent=None):
            captured["shown"] = True

        def exec(self):
            pass

    # Imported lazily inside show_long_term_statistics (keeps pyqtgraph out of startup),
    # so the patch has to land on the source module, not on a GoonerApp attribute.
    monkeypatch.setattr("src.LongTermStatisticsDialog.LongTermStatisticsDialog", FakeDialog)

    menu_bar = app.menuBar()
    stats_menu = next(m for m in menu_bar.findChildren(QMenu) if m.title() == "Statistics")
    action = next(a for a in stats_menu.actions() if a.text() == "Long-term Statistics")
    action.trigger()

    assert captured.get("shown") is True


def test_show_long_term_statistics_passes_history_and_bests(app, monkeypatch):
    captured = {}

    class FakeDialog(_FakeDialogBase):
        def __init__(self, history, all_time_bests, parent=None):
            captured["history"] = history
            captured["all_time_bests"] = all_time_bests

        def exec(self):
            pass

    # Imported lazily inside show_long_term_statistics (keeps pyqtgraph out of startup),
    # so the patch has to land on the source module, not on a GoonerApp attribute.
    monkeypatch.setattr("src.LongTermStatisticsDialog.LongTermStatisticsDialog", FakeDialog)

    app.show_long_term_statistics()

    assert captured["history"] == app.score_tracker.get_history()
    assert captured["all_time_bests"] == app.score_tracker.get_all_time_bests()


def test_defaults_dict_matches_init_defaults(app):
    for var_name, default_value in GoonerApp.DEFAULTS.items():
        assert getattr(app, var_name) == default_value


# --- startup splash ---


def test_show_startup_splash_defaults_to_true(app):
    assert app.show_startup_splash is True


def test_show_startup_splash_respects_saved_setting(qtbot, qsettings, data_store):
    qsettings.setValue("GoonerApp/show_startup_splash", False)
    window = GoonerApp(settings=qsettings, data_store=data_store)
    qtbot.addWidget(window)

    assert window.show_startup_splash is False


# --- what's new ---


def test_maybe_show_whats_new_shows_dialog_when_new_entries_exist(app, monkeypatch):
    monkeypatch.setattr("src.GoonerApp.get_current_version", lambda: "0.2.0")
    app.settings.setValue("GoonerApp/last_seen_version", "0.1.0")

    fake_entries = {"0.2.0": "new stuff"}
    monkeypatch.setattr("src.GoonerApp.changelog.entries_since", lambda last, current: fake_entries)

    captured = {}

    class FakeDialog(_FakeDialogBase):
        def __init__(self, entries, parent=None):
            captured["entries"] = entries
            captured["parent"] = parent

        def exec(self):
            captured["exec_called"] = True

    monkeypatch.setattr("src.GoonerApp.WhatsNewDialog", FakeDialog)

    app.maybe_show_whats_new_on_startup()

    assert captured["entries"] == fake_entries
    assert captured["parent"] is app
    assert captured.get("exec_called") is True
    assert app.settings.value("GoonerApp/last_seen_version") == "0.2.0"


def test_maybe_show_whats_new_skips_dialog_when_no_new_entries(app, monkeypatch):
    monkeypatch.setattr("src.GoonerApp.get_current_version", lambda: "0.2.0")
    app.settings.setValue("GoonerApp/last_seen_version", "0.2.0")
    monkeypatch.setattr("src.GoonerApp.changelog.entries_since", lambda last, current: {})

    called = {}

    class FakeDialog(_FakeDialogBase):
        def __init__(self, *a, **kw):
            called["constructed"] = True

        def exec(self):
            pass

    monkeypatch.setattr("src.GoonerApp.WhatsNewDialog", FakeDialog)

    app.maybe_show_whats_new_on_startup()

    assert "constructed" not in called
    assert app.settings.value("GoonerApp/last_seen_version") == "0.2.0"


def test_show_whats_new_dialog_shows_full_changelog(app, monkeypatch):
    captured = {}

    class FakeDialog(_FakeDialogBase):
        def __init__(self, entries, parent=None):
            captured["entries"] = entries

        def exec(self):
            captured["exec_called"] = True

    monkeypatch.setattr("src.GoonerApp.WhatsNewDialog", FakeDialog)

    app.show_whats_new_dialog()

    from src.changelog import CHANGELOG
    assert captured["entries"] == CHANGELOG
    assert captured.get("exec_called") is True


def test_help_menu_has_whats_new_action(app, monkeypatch):
    from PyQt6.QtWidgets import QMenu

    captured = {}

    class FakeDialog(_FakeDialogBase):
        def __init__(self, entries, parent=None):
            captured["shown"] = True

        def exec(self):
            pass

    monkeypatch.setattr("src.GoonerApp.WhatsNewDialog", FakeDialog)

    menu_bar = app.menuBar()
    help_menu = next(m for m in menu_bar.findChildren(QMenu) if m.title() == "Help")
    whats_new_action = next(a for a in help_menu.actions() if a.text() == "What's New")
    whats_new_action.trigger()

    assert captured.get("shown") is True


def test_help_menu_has_guide_action(app, monkeypatch):
    from PyQt6.QtWidgets import QMenu

    captured = {}

    class FakeDialog(_FakeDialogBase):
        def __init__(self, parent=None):
            captured["shown"] = True

        def exec(self):
            pass

    monkeypatch.setattr("src.GoonerApp.HelpDialog", FakeDialog)

    menu_bar = app.menuBar()
    help_menu = next(m for m in menu_bar.findChildren(QMenu) if m.title() == "Help")
    guide_action = next(a for a in help_menu.actions() if a.text() == "Guide")
    guide_action.trigger()

    assert captured.get("shown") is True


def test_guide_action_has_f1_shortcut(app):
    from PyQt6.QtGui import QKeySequence
    from PyQt6.QtWidgets import QMenu

    menu_bar = app.menuBar()
    help_menu = next(m for m in menu_bar.findChildren(QMenu) if m.title() == "Help")
    guide_action = next(a for a in help_menu.actions() if a.text() == "Guide")

    assert guide_action.shortcut() == QKeySequence("F1")


def test_btn_load_has_ctrl_o_shortcut(app):
    from PyQt6.QtGui import QKeySequence

    assert app.btn_load.shortcut() == QKeySequence("Ctrl+O")


def test_socials_menu_has_discord_action(app, monkeypatch):
    from PyQt6.QtWidgets import QMenu

    captured = {}
    monkeypatch.setattr(
        "src.GoonerApp.QDesktopServices.openUrl", lambda url: captured.setdefault("url", url.toString())
    )

    menu_bar = app.menuBar()
    socials_menu = next(m for m in menu_bar.findChildren(QMenu) if m.title() == "Socials")
    discord_action = next(a for a in socials_menu.actions() if a.text() == "Join Discord")
    discord_action.trigger()

    assert captured.get("url") == GoonerApp.DISCORD_INVITE_URL


# --- check for updates ---


def test_help_menu_has_check_for_updates_action(app):
    from PyQt6.QtWidgets import QMenu

    menu_bar = app.menuBar()
    help_menu = next(m for m in menu_bar.findChildren(QMenu) if m.title() == "Help")
    action = next(a for a in help_menu.actions() if a.text() == "Check for Updates...")

    assert action is not None


def test_check_for_updates_checks_when_confirmed(app, monkeypatch):
    monkeypatch.setattr(app, "_confirm_update_check", lambda: True)
    called = {}
    monkeypatch.setattr(app.update_checker, "check_now", lambda: called.setdefault("called", True))

    app.check_for_updates()

    assert called.get("called") is True


def test_check_for_updates_does_not_check_when_declined(app, monkeypatch):
    monkeypatch.setattr(app, "_confirm_update_check", lambda: False)
    monkeypatch.setattr(
        app.update_checker, "check_now", lambda: pytest.fail("should not check when declined")
    )

    app.check_for_updates()


def test_update_available_signal_shows_dialog(app, monkeypatch):
    captured = {}
    monkeypatch.setattr(
        app, "_show_update_available_dialog", lambda tag, url: captured.update(tag=tag, url=url)
    )

    app.update_checker.update_available.emit("v9.9.9", "https://example.com/release")

    assert captured == {"tag": "v9.9.9", "url": "https://example.com/release"}


def test_up_to_date_signal_shows_dialog(app, monkeypatch):
    called = {}
    monkeypatch.setattr(app, "_show_up_to_date_dialog", lambda: called.setdefault("called", True))

    app.update_checker.up_to_date.emit()

    assert called.get("called") is True


def test_check_failed_signal_shows_dialog(app, monkeypatch):
    captured = {}
    monkeypatch.setattr(
        app, "_show_update_check_failed_dialog", lambda msg: captured.setdefault("msg", msg)
    )

    app.update_checker.check_failed.emit("Host not found")

    assert captured.get("msg") == "Host not found"


def test_vid_loudness_is_restored_from_settings(qtbot, qsettings, data_store):
    qsettings.setValue("GoonerApp/vid_loudness", 0.25)

    window = GoonerApp(settings=qsettings, data_store=data_store)
    qtbot.addWidget(window)

    assert window.vid_loudness == 0.25


def test_importing_gooner_app_does_not_pull_in_pyqtgraph():
    """pyqtgraph + numpy cost ~0.5s warm and ~1.6s cold, for a dialog most sessions never
    open. Run in a subprocess so an earlier test's import can't mask a regression."""
    import subprocess

    from src.utils import get_project_root

    result = subprocess.run(
        [sys.executable, "-c", "import sys; import src.GoonerApp; print('pyqtgraph' in sys.modules)"],
        capture_output=True, text=True, cwd=str(get_project_root()),
        env={**os.environ, "QT_QPA_PLATFORM": "offscreen"},
    )

    assert result.stdout.strip() == "False", result.stderr


# --- P3: session lifecycle and dialog lifetimes ---


def test_stop_stops_video_playback(app, monkeypatch, tmp_path):
    """The player used to keep running behind the statistics dialog, and its EndOfMedia
    then restarted the whole slideshow with no session behind it."""
    app.media_player = MagicMock()
    app.playlist = [tmp_path / "a.png"]
    app.start()
    # load_media() stops the player on every slide, so only calls after this point count.
    app.media_player.stop.reset_mock()

    app.stop()

    app.media_player.stop.assert_called_once()


def test_stop_stops_a_running_gif(app, tmp_path):
    app.playlist = [tmp_path / "a.png"]
    app.start()
    stopped = []
    app.current_movie = type("FakeMovie", (), {"stop": lambda self: stopped.append(True)})()

    app.stop()

    assert stopped == [True]


def test_end_of_media_is_ignored_once_the_session_stopped(app, monkeypatch, tmp_path):
    advanced = []
    monkeypatch.setattr(app, "show_next", lambda: advanced.append(True))
    app.video_start_time = 0

    app.is_running = False
    app.video_status_changed(QMediaPlayer.MediaStatus.EndOfMedia)

    assert advanced == []


def test_an_unplayable_video_advances_instead_of_stalling(app, monkeypatch, tmp_path):
    """No EndOfMedia ever arrives for a codec the backend can't open, and the autoplay
    timer is stopped for videos - so the session used to sit on a black frame forever."""
    app.playlist = [tmp_path / "a.mp4", tmp_path / "b.png"]
    app.start()
    # What the video branch leaves behind: no autoplay timer, waiting on EndOfMedia only.
    app.auto_play_timer.stop()

    app.video_status_changed(QMediaPlayer.MediaStatus.InvalidMedia)

    assert app.auto_play_timer.isActive()


def test_a_media_error_advances_instead_of_stalling(app, tmp_path):
    app.playlist = [tmp_path / "a.mp4", tmp_path / "b.png"]
    app.start()
    app.auto_play_timer.stop()

    app._on_media_error(QMediaPlayer.Error.ResourceError, "boom")

    assert app.auto_play_timer.isActive()


def test_a_media_error_outside_a_session_is_ignored(app):
    app.is_running = False

    app._on_media_error(QMediaPlayer.Error.ResourceError, "boom")

    assert not app.auto_play_timer.isActive()


def test_starting_a_new_session_cancels_a_pending_denied_stop(app, tmp_path):
    """The 5s stop armed by a denied outcome used to be an uncancellable singleShot, so it
    could land on a session started after the old one had already been stopped."""
    app.playlist = [tmp_path / "a.png"]
    app.start()
    app._on_climax_outcome("denied")
    assert app._denied_stop_timer.isActive()

    app.stop()
    app.start()

    assert not app._denied_stop_timer.isActive()


def _pending_dialogs(app, dialog_type):
    from PyQt6.QtCore import QEvent
    from PyQt6.QtWidgets import QApplication

    QApplication.sendPostedEvents(None, QEvent.Type.DeferredDelete)
    return app.findChildren(dialog_type)


def test_reopening_settings_does_not_accumulate_dialogs(app):
    from src.SettingsDialog import SettingsDialog as _SettingsDialog

    for _ in range(3):
        app.open_settings()

    assert _pending_dialogs(app, _SettingsDialog) == []


def test_reopening_the_guide_does_not_accumulate_dialogs(app):
    from src.HelpDialog import HelpDialog as _HelpDialog

    for _ in range(3):
        app.show_help_dialog()

    assert _pending_dialogs(app, _HelpDialog) == []


def test_the_folder_picker_is_released_after_use(app, monkeypatch, tmp_path):
    """This one holds the whole recursive file list of every folder - potentially tens of
    thousands of Path objects - so leaking one per open actually costs memory."""
    from src.MediaFolderPickerDialog import MediaFolderPickerDialog as _Picker

    for _ in range(3):
        app.open_folder()

    assert _pending_dialogs(app, _Picker) == []


def test_settings_keys_come_from_an_explicit_group_constant(app):
    assert GoonerApp.SETTINGS_GROUP == "GoonerApp"


# --- P4: dead code, stale overlays, quitting mid-session ---


def test_session_timer_stays_hidden_outside_a_session(app):
    """SettingsDialog calls this unconditionally on save, and it used to show a frozen clock
    built from the previous session's start time."""
    app.show_session_timer = True
    app.is_running = False

    app._update_session_timer()

    assert app.session_timer_label.isHidden()


def test_record_chase_stays_hidden_outside_a_session(app):
    app.show_record_chase = True
    app.is_running = False

    app._update_record_chase()

    assert app.record_chase_label.isHidden()


def test_session_timer_shows_during_a_session(app, tmp_path):
    app.show_session_timer = True
    app.playlist = [tmp_path / "a.png"]
    app.start()

    app._update_session_timer()

    assert not app.session_timer_label.isHidden()


def test_closing_the_window_records_the_session(app, qtbot, tmp_path):
    """Quitting mid-session used to drop it entirely - no history entry, no personal
    records, as if it never happened."""
    app.playlist = [tmp_path / "a.png"]
    app.start()
    before = len(app.score_tracker.get_history())

    app.close()

    assert len(app.score_tracker.get_history()) == before + 1
    assert app.is_running is False


def test_closing_the_window_does_not_open_the_statistics_dialog(app, monkeypatch, tmp_path):
    """Throwing a modal recap at someone who just hit the X is the opposite of what they
    asked for - the session is recorded silently instead."""
    shown = {}
    monkeypatch.setattr(app, "show_statistics", lambda: shown.setdefault("called", True))
    app.playlist = [tmp_path / "a.png"]
    app.start()

    app.close()

    assert shown == {}


def test_closing_the_window_without_a_session_is_harmless(app):
    before = len(app.score_tracker.get_history())

    app.close()

    assert len(app.score_tracker.get_history()) == before


def test_a_foreign_scheme_url_is_not_opened(app, monkeypatch):
    """release_url comes straight from the GitHub API response - anything but http(s)
    would hand an arbitrary protocol handler to the shell on one click."""
    opened = []
    monkeypatch.setattr("src.GoonerApp.QDesktopServices.openUrl", lambda url: opened.append(url))

    app._open_external_url("file:///C:/Windows/System32/calc.exe")

    assert opened == []


def test_an_https_url_is_opened(app, monkeypatch):
    opened = []
    monkeypatch.setattr("src.GoonerApp.QDesktopServices.openUrl", lambda url: opened.append(url))

    app._open_external_url("https://github.com/owner/repo/releases")

    assert [u.toString() for u in opened] == ["https://github.com/owner/repo/releases"]


def test_update_consent_text_mentions_the_user_agent(app):
    """The dialog claimed 'nothing else is sent' while the request carries a
    self-identifying User-Agent that lands in GitHub's access logs."""
    text = app._update_check_consent_text()

    assert "User-Agent" in text


# --- diagnostic logging (opt-in) ---


def test_diagnostic_log_is_off_by_default(app):
    """Opt-in on purpose: a log in an app like this is a usage trace, so it only exists
    when the user has asked for one."""
    assert app.diagnostic_log is False
    assert applog.log_file_paths(app.data_store.base_dir) == []


def test_enabling_the_diagnostic_log_starts_writing(app):
    app.set_diagnostic_log(True)

    applog.get_logger("src.Test").info("now recording")

    assert "now recording" in applog.log_file_path(app.data_store.base_dir).read_text(encoding="utf-8")


def test_disabling_the_diagnostic_log_stops_writing(app):
    app.set_diagnostic_log(True)
    applog.get_logger("src.Test").info("kept")
    app.set_diagnostic_log(False)

    applog.get_logger("src.Test").info("dropped")

    contents = applog.log_file_path(app.data_store.base_dir).read_text(encoding="utf-8")
    assert "kept" in contents
    assert "dropped" not in contents


def test_the_diagnostic_log_setting_is_persisted(app):
    app.set_diagnostic_log(True)

    assert app.settings.value("GoonerApp/diagnostic_log", type=bool) is True


def test_a_saved_diagnostic_log_setting_is_restored(qtbot, qsettings, data_store):
    qsettings.setValue("GoonerApp/diagnostic_log", True)

    window = GoonerApp(settings=qsettings, data_store=data_store)
    qtbot.addWidget(window)

    assert window.diagnostic_log is True
    window.set_diagnostic_log(False)


def test_media_paths_never_reach_the_log(app, monkeypatch, tmp_path):
    """The one rule this log has to keep: it may name a file that failed, never the folder
    it came from - that would put the location of the collection on disk."""
    app.set_diagnostic_log(True)
    secret = tmp_path / "very-private-folder"
    secret.mkdir()
    files = [secret / "clip.mp4"]
    monkeypatch.setattr(
        "src.GoonerApp.MediaFolderPickerDialog", _fake_picker_dialog(QDialog.DialogCode.Accepted, files)
    )

    app.open_folder()

    contents = applog.log_file_path(app.data_store.base_dir).read_text(encoding="utf-8")
    assert "very-private-folder" not in contents


def test_the_diagnostic_log_level_defaults_to_info(app):
    assert app.diagnostic_log_level == applog.DEFAULT_LEVEL


def test_a_saved_diagnostic_log_level_is_restored(qtbot, qsettings, data_store):
    qsettings.setValue("GoonerApp/diagnostic_log_level", "ERROR")

    window = GoonerApp(settings=qsettings, data_store=data_store)
    qtbot.addWidget(window)

    assert window.diagnostic_log_level == "ERROR"


# --- session recording (feeds the Session Explorer) ---


def test_showing_a_medium_is_recorded(app, tmp_path):
    img = tmp_path / "a.png"
    img.write_bytes(b"")
    app.playlist = [img]
    app.start()

    paths = [path for _at, path in app.session_recorder._media]

    assert str(img) in paths


def test_starting_a_segment_is_recorded(app, tmp_path):
    app.playlist = [tmp_path / "a.png"]
    app.start()

    assert app.session_recorder._segments
    assert app.session_recorder._segments[0][1] is app.beat_handler.current_segment


def test_the_recording_is_reset_for_each_session(app, tmp_path):
    app.playlist = [tmp_path / "a.png", tmp_path / "b.png"]
    app.start()
    app.show_next()
    app._end_session(show_statistics=False)
    assert len(app.session_recorder._media) >= 2

    app.start()

    # Only what the fresh session has shown so far - the previous one is gone.
    assert len(app.session_recorder._media) == 1


def test_the_timeline_reaches_the_statistics_dialog(app, tmp_path, monkeypatch):
    captured = {}

    class FakeStatisticsDialog:
        def __init__(self, stats_data, new_records=None, timeline=None, parent=None, **kwargs):
            captured["timeline"] = timeline

        def exec(self):
            return None

        def deleteLater(self):
            return None

    monkeypatch.setattr("src.GoonerApp.StatisticsDialog", FakeStatisticsDialog)
    app.playlist = [tmp_path / "a.png"]
    app.start()
    app._end_session(show_statistics=True)

    assert captured["timeline"]["segments"]


def test_a_fake_climax_is_recorded_for_the_timeline(app, tmp_path):
    app.playlist = [tmp_path / "a.png"]
    app.start()

    app.climax_handler.fake_climax_triggered_event.emit()

    assert app.session_recorder.timeline()["fake_climaxes"]


# --- replaying a saved session ---


def _replay_script(media, ignore_paths=False, segments=None):
    from src.SessionScript import SessionScript

    return SessionScript({
        "duration_sec": 100.0,
        "segments": segments or [{"kind": "beat", "pattern": "Standard Beat",
                                  "freq": 2.0, "duration_sec": 100.0}],
        "custom_patterns": {}, "climax": None, "fake_climaxes": [], "media": media,
    }, ignore_paths=ignore_paths)


def test_a_replay_shows_the_recorded_media_in_order(app, tmp_path):
    first, second = tmp_path / "one.png", tmp_path / "two.png"
    for path in (first, second):
        path.write_bytes(b"")
    script = _replay_script([{"at_sec": 0.0, "path": str(first)},
                             {"at_sec": 4.0, "path": str(second)}])

    app.start(script=script)

    assert str(first) in [path for _at, path in app.session_recorder._media]


def test_a_replay_uses_the_recorded_media_gaps(app, tmp_path):
    """The pacing is part of what was saved, not just the beats."""
    app.playlist = [tmp_path / "a.png"]
    script = _replay_script([{"at_sec": 0.0, "path": str(tmp_path / "a.png")},
                             {"at_sec": 7.0, "path": str(tmp_path / "a.png")}])

    app.start(script=script)

    assert app.auto_play_timer.interval() == 7000


def test_a_replay_falls_back_to_the_settings_once_the_script_runs_out(app, tmp_path):
    app.playlist = [tmp_path / "a.png"]
    app.min_dur = app.max_dur = 2.0
    app.start(script=_replay_script([{"at_sec": 0.0, "path": str(tmp_path / "a.png")}]))

    app.recalc_autoplay_timer()  # the single recorded gap is already spent

    assert app.auto_play_timer.interval() == 2000


def test_ignoring_the_paths_uses_the_loaded_playlist(app, tmp_path):
    """Replaying someone else's difficulty against your own library."""
    mine = tmp_path / "mine.png"
    mine.write_bytes(b"")
    app.playlist = [mine]
    script = _replay_script([{"at_sec": 0.0, "path": r"C:\someone\else.png"}], ignore_paths=True)

    app.start(script=script)

    shown = [path for _at, path in app.session_recorder._media]
    assert shown == [str(mine)]


def test_a_replay_without_paths_keeps_the_recorded_gaps(app, tmp_path):
    app.playlist = [tmp_path / "a.png"]
    app.min_dur = app.max_dur = 99.0
    script = _replay_script([{"at_sec": 0.0}, {"at_sec": 6.0}], ignore_paths=True)

    app.start(script=script)

    assert app.auto_play_timer.interval() == 6000


def test_a_replayed_video_does_not_escape_its_recorded_gap(app, tmp_path, monkeypatch):
    """load_media normally stops the autoplay timer for video and advances on EndOfMedia -
    in a replay the recorded gap wins and cuts the clip where it was cut before."""
    app.media_player = MagicMock()
    app.audio_output = MagicMock()
    clip = tmp_path / "clip.mp4"
    clip.write_bytes(b"")
    script = _replay_script([{"at_sec": 0.0, "path": str(clip)}, {"at_sec": 5.0, "path": str(clip)}])

    app.start(script=script)

    assert app.auto_play_timer.isActive()
    assert app.auto_play_timer.interval() == 5000


def test_a_normal_session_afterwards_is_not_scripted(app, tmp_path):
    app.playlist = [tmp_path / "a.png"]
    app.min_dur = app.max_dur = 3.0
    app.start(script=_replay_script([{"at_sec": 0.0, "path": str(tmp_path / "a.png")},
                                     {"at_sec": 9.0, "path": str(tmp_path / "a.png")}]))
    app._end_session(show_statistics=False)

    app.start()

    assert app.auto_play_timer.interval() == 3000


def test_a_video_starting_a_session_is_not_cut_short_by_the_autoplay_timer(app, tmp_path):
    """load_media deliberately leaves video off the autoplay timer - it advances on
    EndOfMedia, honouring video_min_dur. start() used to restart the timer right after,
    so the first clip of a session was cut after a random 0.5-4s."""
    app.media_player = MagicMock()
    app.audio_output = MagicMock()
    clip = tmp_path / "clip.mp4"
    clip.write_bytes(b"")
    app.playlist = [clip]

    app.start()

    assert app.auto_play_timer.isActive() is False


# --- saving a session for later ---


def _recorded_timeline():
    return {
        "started_at": 100.0,
        "ended_at": 200.0,
        "climax_at": 190.0,
        "climax_outcome": "real",
        "fake_climaxes": [150.0],
        "segments": [
            {"kind": "beat", "pattern": "My Rhythm", "freq": 2.0, "start": 100.0, "end": 160.0,
             "media": [{"path": "a.png", "start": 100.0, "end": 160.0, "carried_over": False}]},
            {"kind": "finale", "pattern": "Standard Beat", "freq": 5.0, "start": 160.0,
             "end": 200.0, "media": []},
        ],
    }


def test_saving_the_session_puts_it_on_the_shelf(app, monkeypatch):
    monkeypatch.setattr(app.session_recorder, "timeline", _recorded_timeline)

    assert app.save_current_session() is True

    stored = session_files.load_saved_sessions(app.data_store)
    assert len(stored) == 1
    assert [s["kind"] for s in stored[0]["segments"]] == ["beat", "finale"]
    assert stored[0]["climax"]["outcome"] == "real"


def test_a_saved_session_carries_the_custom_rhythms_it_played(app, monkeypatch):
    """Without the definition, a replay on another machine reaches that segment with
    nothing to play."""
    monkeypatch.setattr(app.session_recorder, "timeline", _recorded_timeline)
    app.beat_handler.custom_beat_patterns = {"My Rhythm": [1, 2, -1], "Unused": [1]}

    app.save_current_session()

    stored = session_files.load_saved_sessions(app.data_store)
    assert stored[0]["custom_patterns"] == {"My Rhythm": [1, 2, -1]}


def test_the_statistics_dialog_is_handed_the_way_to_save(app, monkeypatch):
    monkeypatch.setattr(app.session_recorder, "timeline", _recorded_timeline)
    built = {}

    class _FakeStats:
        def __init__(self, *args, save_session=None, **kwargs):
            built["save_session"] = save_session

        def exec(self):
            pass

        def deleteLater(self):
            pass

    monkeypatch.setattr("src.GoonerApp.StatisticsDialog", _FakeStats)

    app.show_statistics()

    assert built["save_session"] == app.save_current_session


def test_a_replay_reaches_the_climax_handler_with_the_recorded_times(app, tmp_path):
    """The planner is told the script directly; the climax only hears about the session
    through session_planned_event, so the script has to travel with it."""
    saved = session_files.to_saved_session(_recorded_timeline())
    app.playlist = [tmp_path / "a.png"]

    app.start(script=SessionScript(saved))

    # As an offset - pytest.approx is relative, so against a Unix timestamp its tolerance
    # runs to well over an hour and any error at all would pass.
    assert app.climax_handler.finale_at - app.beat_handler.session_start_time == pytest.approx(
        90.0
    )
    assert app.climax_handler.outcome == "real"
    assert app.climax_handler.scripted_fake_count == 1


# --- replaying a saved session ---


def _saved_with_media(tmp_path, count=3):
    files = []
    for index in range(count):
        path = tmp_path / f"rec{index}.png"
        path.write_bytes(b"x")
        files.append(str(path))
    return {
        "format": 1,
        "duration_sec": 90.0,
        "segments": [{"kind": "beat", "pattern": "Standard Beat", "freq": 2.0,
                      "duration_sec": 90.0}],
        "custom_patterns": {},
        "climax": {"at_sec": 80.0, "outcome": "real"},
        "fake_climaxes": [],
        "media": [{"at_sec": index * 30.0, "path": path} for index, path in enumerate(files)],
    }


def test_replaying_a_session_plays_the_recorded_files_in_the_recorded_order(app, tmp_path):
    """Not shuffled: the order is part of what was saved, and it is what the scripted gaps
    were measured against."""
    saved = _saved_with_media(tmp_path)

    assert app.replay_session(saved) is True

    assert [str(path) for path in app.playlist] == session_files.recorded_paths(saved)
    assert app.current_index == 0
    assert app.is_running is True


def test_replaying_a_session_replays_its_segments_and_its_climax(app, tmp_path):
    saved = _saved_with_media(tmp_path)

    app.replay_session(saved)

    assert app.beat_handler.current_segment.duration_sec == pytest.approx(90.0)
    assert app.climax_handler.outcome == "real"


def test_replaying_against_your_own_library_leaves_the_playlist_alone(app, tmp_path):
    saved = _saved_with_media(tmp_path)
    mine = [tmp_path / "mine1.png", tmp_path / "mine2.png"]
    app.playlist = list(mine)

    assert app.replay_session(saved, ignore_paths=True) is True

    assert app.playlist == mine


def test_replaying_against_your_own_library_needs_one_to_be_loaded(app, tmp_path):
    saved = _saved_with_media(tmp_path)
    app.playlist = []

    assert app.replay_session(saved, ignore_paths=True) is False
    assert app.is_running is False


def test_a_replay_ends_the_session_that_is_already_running(app, tmp_path):
    app.playlist = [tmp_path / "a.png"]
    app.start()
    ended = []
    app.session_ended_event.connect(lambda: ended.append(True))

    app.replay_session(_saved_with_media(tmp_path))

    assert ended == [True]
    assert app.is_running is True


def test_a_replay_starts_its_recording_fresh(app, tmp_path):
    """The replay is a session of its own - it can be saved again, and what it records has
    to be what it just played, not the tail of whatever came before."""
    app.playlist = [tmp_path / "a.png"]
    app.start()
    app.show_next()

    app.replay_session(_saved_with_media(tmp_path))

    assert len(app.session_recorder._media) == 1


def test_the_sessions_menu_opens_the_saved_sessions_manager(app, monkeypatch):
    from PyQt6.QtWidgets import QMenu

    captured = {}

    class FakeDialog(_FakeDialogBase):
        def __init__(self, main_app, parent=None):
            captured["main_app"] = main_app

        def exec(self):
            pass

    monkeypatch.setattr("src.SavedSessionsDialog.SavedSessionsDialog", FakeDialog)

    menu_bar = app.menuBar()
    sessions_menu = next(m for m in menu_bar.findChildren(QMenu) if m.title() == "Sessions")
    action = next(a for a in sessions_menu.actions() if a.text() == "Saved Sessions...")
    action.trigger()

    assert captured.get("main_app") is app


def test_replaying_against_your_own_library_starts_it_at_the_beginning(app, tmp_path):
    """The index is left over from whatever played before - and a replay of a long session
    leaves it far past the end of a short own library, which walked straight off it."""
    app.playlist = [tmp_path / "mine1.png", tmp_path / "mine2.png"]
    app.current_index = 7

    assert app.replay_session(_saved_with_media(tmp_path), ignore_paths=True) is True

    assert app.current_index == 0


# --- reporting what actually happened ---


def test_the_outcome_buttons_stay_hidden_until_something_is_announced(app):
    assert app.outcome_row.isVisible() is False


def test_a_real_climax_asks_what_happened(app, tmp_path, qtbot):
    app.playlist = [tmp_path / "a.png"]
    app.start()
    app.showMaximized()
    qtbot.waitExposed(app)

    app.climax_handler.outcome_decided_event.emit("real")

    assert app.outcome_row.isVisible() is True


def test_a_fake_climax_asks_exactly_the_same_thing(app, tmp_path, qtbot):
    """If the buttons only showed up for the real one they would *be* the announcement -
    a fake only works while it is indistinguishable."""
    app.playlist = [tmp_path / "a.png"]
    app.start()
    app.showMaximized()
    qtbot.waitExposed(app)

    app.climax_handler.fake_climax_triggered_event.emit()

    assert app.outcome_row.isVisible() is True
    assert app.btn_came.text() == "I Came"


def test_reporting_at_a_real_climax_is_written_down(app, tmp_path):
    app.playlist = [tmp_path / "a.png"]
    app.start()
    app.climax_handler.outcome_decided_event.emit("denied")

    app.btn_came.click()

    assert app.score_tracker.reported_outcome == "came"
    assert app.score_tracker.climax_outcome == "denied"


def test_reporting_hides_the_buttons_again(app, tmp_path, qtbot):
    app.playlist = [tmp_path / "a.png"]
    app.start()
    app.showMaximized()
    qtbot.waitExposed(app)
    app.climax_handler.outcome_decided_event.emit("real")

    app.btn_ruined.click()

    assert app.outcome_row.isVisible() is False
    assert app.score_tracker.reported_outcome == "ruined"


def test_coming_at_a_fake_out_counts_as_falling_for_it(app, tmp_path):
    app.playlist = [tmp_path / "a.png"]
    app.start()
    app.climax_handler.fake_climax_triggered_event.emit()

    app.btn_came.click()

    assert app.score_tracker.fakeouts_fallen_for == 1
    # Not the session's outcome: the session is still running, and the real one is still due.
    assert app.score_tracker.reported_outcome is None


def test_holding_out_through_a_fake_out_is_not_counted_as_falling_for_it(app, tmp_path):
    app.playlist = [tmp_path / "a.png"]
    app.start()
    app.climax_handler.fake_climax_triggered_event.emit()

    app.btn_stopped.click()

    assert app.score_tracker.fakeouts_fallen_for == 0


def test_an_unanswered_fake_out_takes_its_buttons_away_at_the_reveal(app, tmp_path, qtbot):
    app.playlist = [tmp_path / "a.png"]
    app.start()
    app.showMaximized()
    qtbot.waitExposed(app)
    app.climax_handler.fake_climax_triggered_event.emit()

    app.climax_handler.fake_climax_revealed_event.emit()

    assert app.outcome_row.isVisible() is False
    assert app.score_tracker.fakeouts_fallen_for == 0


def test_a_denied_session_waits_for_the_answer_instead_of_stopping_after_five_seconds(
    app, tmp_path
):
    """The buttons would otherwise be gone before the user could reach them."""
    app.playlist = [tmp_path / "a.png"]
    app.start()

    app.climax_handler.outcome_decided_event.emit("denied")

    assert app.is_running is True
    assert app._denied_stop_timer.isActive() is True
    assert app._denied_stop_timer.interval() == GoonerApp.DENIED_ANSWER_TIMEOUT_MS


def test_answering_after_a_denial_ends_the_session(app, tmp_path, monkeypatch):
    ended = []
    monkeypatch.setattr(app, "_end_session", lambda show_statistics: ended.append(show_statistics))
    app.playlist = [tmp_path / "a.png"]
    app.start()
    app.climax_handler.outcome_decided_event.emit("denied")

    app.btn_came.click()

    assert ended == [True]


def test_stopping_by_hand_asks_how_it_ended(app, tmp_path, monkeypatch):
    """A session the user ends has no outcome at all otherwise, and every average silently
    counts it as a session that never climaxed."""
    monkeypatch.setattr(app, "_ask_how_it_ended", lambda: "stopped")
    app.playlist = [tmp_path / "a.png"]
    app.start()

    app.stop()

    assert app.score_tracker.get_history()[-1]["reported_outcome"] == "stopped"


def test_stopping_does_not_ask_again_when_the_climax_already_did(app, tmp_path, monkeypatch):
    asked = []
    monkeypatch.setattr(app, "_ask_how_it_ended", lambda: asked.append(True) or "came")
    app.playlist = [tmp_path / "a.png"]
    app.start()
    app.climax_handler.outcome_decided_event.emit("real")
    app.btn_came.click()

    app.stop()

    assert asked == []


def test_closing_the_window_never_asks(app, tmp_path, monkeypatch):
    """Someone who just hit the X wants the window gone, not a question - same reasoning
    that already keeps the statistics recap out of closeEvent."""
    from PyQt6.QtGui import QCloseEvent

    asked = []
    monkeypatch.setattr(app, "_ask_how_it_ended", lambda: asked.append(True) or "came")
    app.playlist = [tmp_path / "a.png"]
    app.start()

    app.closeEvent(QCloseEvent())

    assert asked == []


def test_the_question_can_be_switched_off(app, tmp_path, monkeypatch):
    asked = []
    monkeypatch.setattr(app, "_ask_how_it_ended", lambda: asked.append(True) or "came")
    app.ask_for_outcome = False
    app.playlist = [tmp_path / "a.png"]
    app.start()

    app.climax_handler.outcome_decided_event.emit("real")
    assert app.outcome_row.isVisible() is False

    app.stop()
    assert asked == []


def test_falling_for_a_fake_out_gets_its_own_line(app, tmp_path, monkeypatch):
    """Not the pause line and not the reveal - the reveal is still seconds out, and being
    congratulated before being told it was never real is the whole point."""
    spoken = []
    monkeypatch.setattr(
        app.callout_handler, "force_output_sentence", lambda key: spoken.append(key)
    )
    app.playlist = [tmp_path / "a.png"]
    app.start()
    app.climax_handler.fake_climax_triggered_event.emit()

    app.btn_came.click()

    assert spoken == ["fake_climax_fell_for"]


def test_the_outcome_buttons_do_not_crush_the_beat_track(app, qtbot):
    """The footer has a fixed height so the media above never wobbles. Squeezing a third
    widget into it left the note track 29px tall and the buttons too small to hit, so the
    footer grows for the question instead and shrinks back afterwards."""
    app.resize(1280, 800)
    app.showMaximized()
    qtbot.waitExposed(app)
    app.is_running = True
    app._update_climax_status_label("cum")
    qtbot.wait(10)
    track_without_question = app.beat_track.height()

    app._show_outcome_buttons("climax")
    qtbot.wait(10)

    assert app.beat_track.height() >= track_without_question
    assert app.outcome_row.height() >= GoonerApp.OUTCOME_ROW_HEIGHT
    assert app.footer_container.height() > GoonerApp.FOOTER_HEIGHT

    app._hide_outcome_buttons()
    qtbot.wait(10)
    assert app.footer_container.height() == GoonerApp.FOOTER_HEIGHT


# --- "I reached my Edge" ---


def test_the_edge_button_is_only_live_during_a_session(app, tmp_path):
    assert app.btn_edge.isEnabled() is False

    app.playlist = [tmp_path / "a.png"]
    app.start()

    assert app.btn_edge.isEnabled() is True


def test_reaching_your_edge_buys_a_pause_and_pushes_the_climax_back(app, tmp_path):
    app.playlist = [tmp_path / "a.png"]
    app.beat_handler.edge_pause_dur = 15
    app.climax_handler.climax_active = True
    app.climax_handler.min_climax_after = app.climax_handler.max_climax_after = 600.0
    app.start()
    climax_before = app.climax_handler.finale_at

    app.btn_edge.click()

    assert app.beat_handler.current_segment.kind == "pause"
    assert app.climax_handler.finale_at == pytest.approx(climax_before + 15.0)
    assert app.score_tracker.edge_count == 1


def test_reaching_your_edge_gets_its_own_line(app, tmp_path, monkeypatch):
    spoken = []
    monkeypatch.setattr(
        app.callout_handler, "force_output_sentence", lambda key: spoken.append(key)
    )
    app.playlist = [tmp_path / "a.png"]
    app.start()

    app.btn_edge.click()

    assert spoken == ["edge_reached"]


def test_a_second_edge_is_refused_until_the_cooldown_is_up(app, tmp_path):
    """Holding the key down would otherwise turn the session into a nap."""
    app.playlist = [tmp_path / "a.png"]
    app.beat_handler.edge_cooldown_sec = 60
    app.start()
    app.btn_edge.click()

    app.btn_edge.click()

    assert app.score_tracker.edge_count == 1
    assert app.btn_edge.isEnabled() is False
    assert "60" in app.btn_edge.text()


def test_the_cooldown_gives_the_button_back(app, tmp_path, qtbot):
    app.playlist = [tmp_path / "a.png"]
    app.beat_handler.edge_cooldown_sec = 1
    app.start()
    app.btn_edge.click()
    assert app.btn_edge.isEnabled() is False

    qtbot.waitUntil(lambda: app.btn_edge.isEnabled(), timeout=4000)

    assert app.btn_edge.text() == GoonerApp.EDGE_BUTTON_TEXT


def test_the_edge_button_goes_away_at_the_climax(app, tmp_path, qtbot):
    """Nothing left to be relieved of, and the planner refuses it anyway."""
    app.playlist = [tmp_path / "a.png"]
    app.start()
    app.showMaximized()
    qtbot.waitExposed(app)

    app.climax_handler.outcome_decided_event.emit("real")

    assert app.btn_edge.isVisible() is False


def test_a_new_session_hands_the_edge_button_back(app, tmp_path, qtbot):
    app.playlist = [tmp_path / "a.png"]
    app.start()
    app.showMaximized()
    qtbot.waitExposed(app)
    app.climax_handler.outcome_decided_event.emit("real")
    app._end_session(show_statistics=False)

    app.start()

    assert app.btn_edge.isVisible() is True
    assert app.btn_edge.isEnabled() is True


def test_the_edge_button_hides_entirely_when_switched_off(app, tmp_path, qtbot):
    app.beat_handler.edge_relief_active = False
    app.playlist = [tmp_path / "a.png"]
    app.showMaximized()
    qtbot.waitExposed(app)

    app.start()

    assert app.btn_edge.isVisible() is False


def test_an_edge_the_planner_refuses_costs_nothing(app, tmp_path, monkeypatch):
    """Refused mid-pause or after the climax - it must not burn the cooldown or count."""
    monkeypatch.setattr(app.beat_handler, "edge_relief", lambda: 0.0)
    app.playlist = [tmp_path / "a.png"]
    app.start()

    app.btn_edge.click()

    assert app.score_tracker.edge_count == 0
    assert app.btn_edge.isEnabled() is True


# --- achievements ---


def test_finishing_a_session_judges_it_for_achievements(app, tmp_path, monkeypatch):
    """Evaluated after the session is already in the history, so a rule that counts sessions
    can count this one."""
    seen = {}

    def fake_evaluate(played, history):
        seen["history_length"] = len(history)
        return []

    monkeypatch.setattr(app.achievement_tracker, "evaluate", fake_evaluate)
    app.playlist = [tmp_path / "a.png"]
    app.start()

    app._end_session(show_statistics=False)

    assert seen["history_length"] == len(app.score_tracker.get_history())


def test_newly_earned_achievements_reach_the_statistics_dialog(app, tmp_path, monkeypatch):
    from src.achievements import CATALOGUE

    captured = {}

    class FakeStats(_FakeDialogBase):
        def __init__(self, *args, new_achievements=None, **kwargs):
            captured["new_achievements"] = new_achievements

        def exec(self):
            pass

    monkeypatch.setattr("src.GoonerApp.StatisticsDialog", FakeStats)
    monkeypatch.setattr(app.achievement_tracker, "evaluate", lambda played, history: [CATALOGUE[0]])
    app.playlist = [tmp_path / "a.png"]
    app.start()

    app._end_session(show_statistics=True)

    assert captured["new_achievements"] == [CATALOGUE[0]]


def test_replaying_a_session_says_so_in_the_stats(app, tmp_path):
    saved = _saved_with_media(tmp_path)

    app.replay_session(saved)

    assert app.score_tracker.was_replay is True


def test_the_statistics_menu_opens_the_achievements(app, monkeypatch):
    from PyQt6.QtWidgets import QMenu

    captured = {}

    class FakeDialog(_FakeDialogBase):
        def __init__(self, tracker, history, parent=None):
            captured["tracker"] = tracker

        def exec(self):
            pass

    monkeypatch.setattr("src.AchievementsDialog.AchievementsDialog", FakeDialog)

    menu_bar = app.menuBar()
    stats_menu = next(m for m in menu_bar.findChildren(QMenu) if m.title() == "Statistics")
    action = next(a for a in stats_menu.actions() if a.text() == "Achievements")
    action.trigger()

    assert captured["tracker"] is app.achievement_tracker


def test_answering_after_any_climax_ends_the_session(app, tmp_path, monkeypatch):
    """You have said what happened - the session is over. It used to end only after a
    denial, so a real or ruined climax left the beat running with nothing left to come."""
    ended = []
    monkeypatch.setattr(app, "_end_session", lambda show_statistics: ended.append(show_statistics))
    for announced in ("real", "ruined", "denied"):
        ended.clear()
        app.playlist = [tmp_path / "a.png"]
        app.is_running = True
        app.climax_handler.outcome_decided_event.emit(announced)

        app.btn_came.click()

        assert ended == [True], announced
        assert app._denied_stop_timer.isActive() is False


def test_reporting_at_a_fake_out_leaves_the_session_running(app, tmp_path, monkeypatch):
    """The real climax is still to come - ending here would cut the session short on a joke."""
    ended = []
    monkeypatch.setattr(app, "_end_session", lambda show_statistics: ended.append(show_statistics))
    app.playlist = [tmp_path / "a.png"]
    app.start()
    app.climax_handler.fake_climax_triggered_event.emit()

    app.btn_came.click()

    assert ended == []
