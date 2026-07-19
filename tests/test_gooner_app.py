import time
from unittest.mock import MagicMock

import pytest
from PyQt6.QtMultimedia import QMediaPlayer
from PyQt6.QtWidgets import QDialog

from src.GoonerApp import GoonerApp

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


# --- folder scanning ---


def test_finde_unterstuetzte_dateien_finds_all_supported_extensions(app, tmp_path):
    names = ["a.mp4", "b.avi", "c.mov", "d.mkv", "e.gif", "f.png", "g.jpg", "h.jpeg", "i.bmp", "j.txt"]
    for name in names:
        (tmp_path / name).write_bytes(b"")

    found = app.finde_unterstützte_dateien(str(tmp_path))

    assert {f.name for f in found} == set(names) - {"j.txt"}


def test_finde_unterstuetzte_dateien_searches_recursively(app, tmp_path):
    nested = tmp_path / "sub"
    nested.mkdir()
    (nested / "deep.png").write_bytes(b"")

    found = app.finde_unterstützte_dateien(str(tmp_path))

    assert [f.name for f in found] == ["deep.png"]


# --- open_folder ---


def _fake_picker_dialog(exec_result, selected_files=None):
    class FakeDialog:
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
    assert app.image_label.text() == "Keine Dateien gefunden."
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
    app.video_min_dur = 5.0
    app.video_start_time = 100.0
    monkeypatch.setattr(time, "time", lambda: 102.0)

    app.video_status_changed(QMediaPlayer.MediaStatus.EndOfMedia)

    fake_player.play.assert_called_once()


def test_video_status_changed_advances_if_above_min_duration(app, monkeypatch):
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


def test_on_climax_outcome_denied_schedules_stop(app, monkeypatch):
    called = {}
    monkeypatch.setattr("src.GoonerApp.QTimer.singleShot", lambda ms, fn: called.update(ms=ms, fn=fn))

    app._on_climax_outcome("denied")

    assert called["ms"] == 5000
    assert called["fn"] == app.stop


@pytest.mark.parametrize("outcome", ["real", "ruined"])
def test_on_climax_outcome_non_denied_does_not_schedule_stop(app, monkeypatch, outcome):
    called = {}
    monkeypatch.setattr("src.GoonerApp.QTimer.singleShot", lambda ms, fn: called.update(ms=ms, fn=fn))

    app._on_climax_outcome(outcome)

    assert called == {}


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
    assert app.footer_layout.stretch(0) == 0
    assert app.footer_layout.stretch(1) == 1


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

    class FakeDialog:
        def __init__(self, stats_data, new_records=None, parent=None):
            captured["new_records"] = new_records

        def exec(self):
            pass

    monkeypatch.setattr("src.GoonerApp.StatisticsDialog", FakeDialog)

    app.show_statistics()

    assert captured["new_records"] == {"total_dur_sec": 42.0}


def test_statistics_menu_has_long_term_statistics_action(app, monkeypatch):
    from PyQt6.QtWidgets import QMenu

    captured = {}

    class FakeDialog:
        def __init__(self, history, all_time_bests, parent=None):
            captured["shown"] = True

        def exec(self):
            pass

    monkeypatch.setattr("src.GoonerApp.LongTermStatisticsDialog", FakeDialog)

    menu_bar = app.menuBar()
    stats_menu = next(m for m in menu_bar.findChildren(QMenu) if m.title() == "Statistics")
    action = next(a for a in stats_menu.actions() if a.text() == "Long-term Statistics")
    action.trigger()

    assert captured.get("shown") is True


def test_show_long_term_statistics_passes_history_and_bests(app, monkeypatch):
    captured = {}

    class FakeDialog:
        def __init__(self, history, all_time_bests, parent=None):
            captured["history"] = history
            captured["all_time_bests"] = all_time_bests

        def exec(self):
            pass

    monkeypatch.setattr("src.GoonerApp.LongTermStatisticsDialog", FakeDialog)

    app.show_long_term_statistics()

    assert captured["history"] == app.score_tracker.get_history()
    assert captured["all_time_bests"] == app.score_tracker.get_all_time_bests()


def test_defaults_dict_matches_init_defaults(app):
    for var_name, default_value in GoonerApp.DEFAULTS.items():
        assert getattr(app, var_name) == default_value


# --- what's new ---


def test_maybe_show_whats_new_shows_dialog_when_new_entries_exist(app, monkeypatch):
    monkeypatch.setattr("src.GoonerApp.get_current_version", lambda: "0.2.0")
    app.settings.setValue("GoonerApp/last_seen_version", "0.1.0")

    fake_entries = {"0.2.0": "new stuff"}
    monkeypatch.setattr("src.GoonerApp.changelog.entries_since", lambda last, current: fake_entries)

    captured = {}

    class FakeDialog:
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

    class FakeDialog:
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

    class FakeDialog:
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

    class FakeDialog:
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

    class FakeDialog:
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
