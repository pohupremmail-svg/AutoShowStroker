import time
from unittest.mock import MagicMock

import pytest
from PyQt6.QtMultimedia import QMediaPlayer
from PyQt6.QtWidgets import QFileDialog

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


def test_open_folder_cancelled_leaves_playlist_untouched(app, monkeypatch):
    monkeypatch.setattr(QFileDialog, "getExistingDirectory", lambda *a, **k: "")
    app.open_folder()
    assert app.playlist == []
    assert app.is_running is False


def test_open_folder_no_supported_files_shows_message_and_stays_stopped(app, monkeypatch, tmp_path):
    monkeypatch.setattr(QFileDialog, "getExistingDirectory", lambda *a, **k: str(tmp_path))
    app.open_folder()
    assert app.image_label.text() == "Keine Dateien gefunden."
    assert app.is_running is False


def test_open_folder_with_files_starts_session(app, monkeypatch, tmp_path):
    (tmp_path / "a.png").write_bytes(b"")
    (tmp_path / "b.png").write_bytes(b"")
    monkeypatch.setattr(QFileDialog, "getExistingDirectory", lambda *a, **k: str(tmp_path))

    app.open_folder()

    assert {p.name for p in app.playlist} == {"a.png", "b.png"}
    assert app.current_index == 0
    assert app.is_running is True


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
