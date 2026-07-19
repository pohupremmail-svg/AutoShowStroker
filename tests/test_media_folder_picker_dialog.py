import json
import shutil

from PyQt6.QtCore import Qt
from PyQt6.QtGui import QImage
from PyQt6.QtWidgets import QApplication, QDialog, QFileDialog, QLabel

from src import media_kinds
from src.MediaFolderPickerDialog import (
    BUSY_INDICATOR_DELAY_MS,
    GRID_MARGIN,
    GRID_SPACING,
    THUMBNAIL_CELL_SIZE,
    MediaFolderPickerDialog,
)


def _make_folder_with_files(tmp_path, name, count):
    folder = tmp_path / name
    folder.mkdir()
    for i in range(count):
        (folder / f"img{i}.png").write_bytes(b"x")
    return str(folder)


def _make_folder_with_videos(tmp_path, name, count):
    folder = tmp_path / name
    folder.mkdir()
    for i in range(count):
        (folder / f"clip{i}.mp4").write_bytes(b"x")
    return str(folder)


def test_starts_with_no_folders_and_start_disabled(app, qtbot):
    dialog = MediaFolderPickerDialog(parent=app)
    qtbot.addWidget(dialog)

    assert dialog.folders == []
    assert dialog.btn_start.isEnabled() is False


def test_add_folder_appends_and_rescans(app, qtbot, monkeypatch, tmp_path):
    folder = _make_folder_with_files(tmp_path, "a", 3)
    monkeypatch.setattr(QFileDialog, "getExistingDirectory", lambda *a, **k: folder)

    dialog = MediaFolderPickerDialog(parent=app)
    qtbot.addWidget(dialog)

    dialog._on_add_folder()

    assert dialog.folders == [folder]
    assert len(dialog._per_folder_files[folder]) == 3
    assert dialog.folder_list.count() == 1
    assert dialog.btn_start.isEnabled() is True


def test_add_folder_cancelled_is_noop(app, qtbot, monkeypatch):
    monkeypatch.setattr(QFileDialog, "getExistingDirectory", lambda *a, **k: "")

    dialog = MediaFolderPickerDialog(parent=app)
    qtbot.addWidget(dialog)

    dialog._on_add_folder()

    assert dialog.folders == []


def test_add_duplicate_folder_is_noop(app, qtbot, monkeypatch, tmp_path):
    folder = _make_folder_with_files(tmp_path, "a", 1)
    monkeypatch.setattr(QFileDialog, "getExistingDirectory", lambda *a, **k: folder)

    dialog = MediaFolderPickerDialog(parent=app)
    qtbot.addWidget(dialog)

    dialog._on_add_folder()
    dialog._on_add_folder()

    assert dialog.folders == [folder]


def test_remove_selected_folder(app, qtbot, monkeypatch, tmp_path):
    folder_a = _make_folder_with_files(tmp_path, "a", 2)
    folder_b = _make_folder_with_files(tmp_path, "b", 2)

    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[folder_a, folder_b])
    qtbot.addWidget(dialog)

    dialog.folder_list.setCurrentRow(0)
    dialog._on_remove_folder()

    assert dialog.folders == [folder_b]
    assert folder_a not in dialog._per_folder_files


def test_start_disabled_when_all_folders_empty(app, qtbot, tmp_path):
    empty_folder = tmp_path / "empty"
    empty_folder.mkdir()

    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[str(empty_folder)])
    qtbot.addWidget(dialog)

    assert dialog.btn_start.isEnabled() is False


def test_start_enabled_when_any_folder_has_files(app, qtbot, tmp_path):
    empty_folder = tmp_path / "empty"
    empty_folder.mkdir()
    folder_with_files = _make_folder_with_files(tmp_path, "b", 1)

    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[str(empty_folder), folder_with_files])
    qtbot.addWidget(dialog)

    assert dialog.btn_start.isEnabled() is True


def test_on_start_sets_selected_files_as_union_of_all_folders(app, qtbot, tmp_path):
    folder_a = _make_folder_with_files(tmp_path, "a", 2)
    folder_b = _make_folder_with_files(tmp_path, "b", 3)

    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[folder_a, folder_b])
    qtbot.addWidget(dialog)

    dialog._on_start()

    assert len(dialog.selected_files) == 5
    assert dialog.result() == QDialog.DialogCode.Accepted


def test_on_start_persists_folders_to_settings(app, qtbot, tmp_path):
    folder_a = _make_folder_with_files(tmp_path, "a", 1)

    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[folder_a])
    qtbot.addWidget(dialog)
    dialog._on_start()

    persisted = json.loads(app.settings.value("GoonerApp/last_selected_folders"))
    assert persisted == [folder_a]


def test_reopening_dialog_prefills_persisted_folders(app, qtbot, tmp_path):
    folder_a = _make_folder_with_files(tmp_path, "a", 1)
    app.settings.setValue("GoonerApp/last_selected_folders", json.dumps([folder_a]))

    dialog = MediaFolderPickerDialog(parent=app)
    qtbot.addWidget(dialog)

    assert dialog.folders == [folder_a]


def test_cancel_does_not_persist_folders(app, qtbot, tmp_path):
    folder_a = _make_folder_with_files(tmp_path, "a", 1)

    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[folder_a])
    qtbot.addWidget(dialog)
    dialog.reject()

    assert app.settings.value("GoonerApp/last_selected_folders") is None


def _attach_fake_movie(widget):
    stopped = []
    widget._movie = type("FakeMovie", (), {"stop": lambda self: stopped.append(True)})()
    return stopped


def _attach_fake_player(widget):
    stopped = []
    widget._player = type(
        "FakePlayer", (), {"setSource": lambda self, url: None, "stop": lambda self: stopped.append(True)}
    )()
    return stopped


def test_accept_stops_live_cells(app, qtbot, tmp_path):
    folder = _make_folder_with_files(tmp_path, "a", 1)
    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[folder])
    qtbot.addWidget(dialog)

    movie_stopped = _attach_fake_movie(dialog._thumbnail_cells[0])
    player_stopped = _attach_fake_player(dialog._thumbnail_cells[0])

    dialog._on_start()

    assert movie_stopped == [True]
    assert player_stopped == [True]


def test_reject_stops_live_cells(app, qtbot, tmp_path):
    folder = _make_folder_with_files(tmp_path, "a", 1)
    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[folder])
    qtbot.addWidget(dialog)

    movie_stopped = _attach_fake_movie(dialog._thumbnail_cells[0])
    player_stopped = _attach_fake_player(dialog._thumbnail_cells[0])

    dialog.reject()

    assert movie_stopped == [True]
    assert player_stopped == [True]


def test_closing_via_done_directly_stops_live_cells(app, qtbot, tmp_path):
    """Covers the native window-close (X button) path, which routes through done()
    the same way accept()/reject() do, rather than a dedicated closeEvent."""
    folder = _make_folder_with_files(tmp_path, "a", 1)
    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[folder])
    qtbot.addWidget(dialog)

    movie_stopped = _attach_fake_movie(dialog._thumbnail_cells[0])

    dialog.done(QDialog.DialogCode.Rejected)

    assert movie_stopped == [True]


def test_thumbnail_grid_count_matches_computed_grid_size(app, qtbot, tmp_path):
    folder = _make_folder_with_files(tmp_path, "a", 200)

    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[folder])
    qtbot.addWidget(dialog)

    expected = dialog._current_grid_count()
    assert len(dialog._current_thumbnails) == min(expected, 200)
    assert len(dialog._thumbnail_cells) == len(dialog._current_thumbnails)


def test_adjust_thumbnail_count_grows_without_touching_existing_cells(app, qtbot, tmp_path):
    folder = _make_folder_with_files(tmp_path, "a", 200)
    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[folder])
    qtbot.addWidget(dialog)

    start_count = 10
    dialog._current_thumbnails = []
    dialog._thumbnail_cells = []
    for path in list(dialog._per_folder_files[folder])[:start_count]:
        dialog._add_thumbnail_cell(path)
    original_cells = list(dialog._thumbnail_cells)
    original_thumbnails = list(dialog._current_thumbnails)

    dialog._adjust_thumbnail_count(15, columns=4)

    assert len(dialog._current_thumbnails) == 15
    assert dialog._thumbnail_cells[:start_count] == original_cells
    assert dialog._current_thumbnails[:start_count] == original_thumbnails


def test_adjust_thumbnail_count_shrinks_by_dropping_from_the_end(app, qtbot, tmp_path):
    folder = _make_folder_with_files(tmp_path, "a", 200)
    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[folder])
    qtbot.addWidget(dialog)

    dialog._current_thumbnails = []
    dialog._thumbnail_cells = []
    for path in list(dialog._per_folder_files[folder])[:15]:
        dialog._add_thumbnail_cell(path)
    kept_thumbnails = list(dialog._current_thumbnails[:10])
    kept_cells = list(dialog._thumbnail_cells[:10])

    dialog._adjust_thumbnail_count(10, columns=4)

    assert dialog._current_thumbnails == kept_thumbnails
    assert dialog._thumbnail_cells == kept_cells


def test_rescan_always_fully_replaces_thumbnails_even_if_count_unchanged(app, qtbot, monkeypatch, tmp_path):
    folder = _make_folder_with_files(tmp_path, "a", 200)
    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[folder])
    qtbot.addWidget(dialog)

    first_cells = list(dialog._thumbnail_cells)

    dialog._rescan_and_refresh()

    assert dialog._thumbnail_cells != first_cells
    for cell in first_cells:
        assert cell not in dialog._thumbnail_cells


def test_apply_pending_resize_reflows_even_when_count_unchanged_but_columns_differ(app, qtbot, monkeypatch, tmp_path):
    """A resize that keeps columns*rows equal while columns/rows individually swap (e.g.
    5x4 -> 4x5) must still trigger a reflow - gating purely on the product misses it and
    leaves the grid rendered with a stale column count that no longer matches the viewport."""
    folder = _make_folder_with_files(tmp_path, "a", 200)
    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[folder])
    qtbot.addWidget(dialog)

    calls = []
    monkeypatch.setattr(dialog, "_adjust_thumbnail_count", lambda count, columns: calls.append((count, columns)))
    monkeypatch.setattr(dialog, "_current_grid_dimensions", lambda: (4, 5, 20))
    dialog._last_grid_count = 20  # same count as before, different shape (was e.g. 5x4)

    dialog._apply_pending_resize()

    assert calls == [(20, 4)]


def test_rapid_resize_events_only_trigger_one_adjust_after_debounce(app, qtbot, monkeypatch, tmp_path):
    folder = _make_folder_with_files(tmp_path, "a", 200)
    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[folder])
    qtbot.addWidget(dialog)
    dialog.show()

    calls = []
    monkeypatch.setattr(dialog, "_adjust_thumbnail_count", lambda count, columns: calls.append(count))
    dialog._last_grid_count = -1  # force a mismatch so any settled size triggers an adjust

    for width in (950, 1000, 1050):
        dialog.resize(width, 700)
        qtbot.wait(30)  # faster than the debounce interval - simulates a drag in progress

    assert len(calls) == 0  # nothing fires yet, still within the debounce window

    qtbot.wait(250)  # settle past RESIZE_DEBOUNCE_MS

    assert len(calls) == 1


def test_grid_dimensions_reserve_space_for_a_possible_scrollbar(app, qtbot, monkeypatch):
    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[])
    qtbot.addWidget(dialog)

    monkeypatch.setattr(dialog, "_viewport_size", lambda: (996, 700))

    columns, _rows, _count = dialog._current_grid_dimensions()

    # Naive (unreserved, no spacing/margin) math would fit 996 // 140 = 7 columns; the
    # scrollbar reserve plus real spacing/margin must bring that down so a scrollbar
    # appearing later never squeezes columns out from under an already-decided layout.
    cell_w, _cell_h = THUMBNAIL_CELL_SIZE
    naive_columns = 996 // cell_w
    assert columns < naive_columns

    usable_width = 996 - dialog._scrollbar_width_reserve() - 2 * GRID_MARGIN
    expected_columns = max(1, (usable_width + GRID_SPACING) // (cell_w + GRID_SPACING))
    assert columns == expected_columns


def test_scrollbar_width_reserve_queries_the_real_style_metric(app, qtbot):
    from PyQt6.QtWidgets import QStyle

    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[])
    qtbot.addWidget(dialog)

    expected = dialog.style().pixelMetric(QStyle.PixelMetric.PM_ScrollBarExtent)
    assert dialog._scrollbar_width_reserve() == expected
    assert expected > 0  # sanity - a real desktop style always reports a positive extent


def test_grid_footprint_never_exceeds_viewport_width(app, qtbot, monkeypatch):
    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[])
    qtbot.addWidget(dialog)

    cell_w, _cell_h = THUMBNAIL_CELL_SIZE
    for width in range(400, 2000, 73):
        monkeypatch.setattr(dialog, "_viewport_size", lambda w=width: (w, 700))
        columns, _rows, _count = dialog._current_grid_dimensions()
        footprint = columns * cell_w + (columns - 1) * GRID_SPACING + 2 * GRID_MARGIN
        # The reserve is exactly the safety margin a real scrollbar could eat - footprint
        # must stay within the viewport even *without* subtracting that reserve again here.
        assert footprint <= width


# --- animated thumbnails ---
#
# None of these tests may construct a real QMovie/QMediaPlayer against a real media file -
# same reasoning as never touching a real QSoundEffect/QMediaPlayer elsewhere in this suite.
# _grab_video_frame and _make_video_loop_cell are always monkeypatched away before any
# folder containing a (fake, garbage-bytes) .mp4 file gets scanned.


def test_animate_videos_checkbox_defaults_to_unchecked(app, qtbot):
    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[])
    qtbot.addWidget(dialog)

    assert dialog.animate_videos_checkbox.isChecked() is False


def test_gif_kind_dispatches_to_make_gif_cell(app, qtbot, monkeypatch, tmp_path):
    monkeypatch.setattr(MediaFolderPickerDialog, "_make_gif_cell", lambda self, path: QLabel("gif-stub"))
    folder = tmp_path / "a"
    folder.mkdir()
    (folder / "clip.gif").write_bytes(b"x")

    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[str(folder)])
    qtbot.addWidget(dialog)

    assert len(dialog._thumbnail_cells) == 1
    assert dialog._thumbnail_cells[0].text() == "gif-stub"


def test_video_kind_uses_grab_video_frame_when_checkbox_unchecked(app, qtbot, monkeypatch, tmp_path):
    calls = []
    monkeypatch.setattr(
        MediaFolderPickerDialog, "_grab_video_frame", lambda self, path: (calls.append(path), None)[1]
    )
    monkeypatch.setattr(
        MediaFolderPickerDialog,
        "_make_video_loop_cell",
        lambda self, path: (_ for _ in ()).throw(AssertionError("should not animate when unchecked")),
    )
    folder = _make_folder_with_videos(tmp_path, "a", 1)

    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[folder])
    qtbot.addWidget(dialog)

    assert len(calls) == 1


def test_video_kind_uses_video_loop_cell_when_checkbox_checked(app, qtbot, monkeypatch, tmp_path):
    # Construction happens with the checkbox at its unchecked default, so
    # _grab_video_frame legitimately fires once during initial build - only assert on
    # _make_video_loop_cell, which must fire once the checkbox is then switched on.
    monkeypatch.setattr(MediaFolderPickerDialog, "_grab_video_frame", lambda self, path: None)
    calls = []
    monkeypatch.setattr(
        MediaFolderPickerDialog,
        "_make_video_loop_cell",
        lambda self, path: (calls.append(path), QLabel("video-loop-stub"))[1],
    )
    folder = _make_folder_with_videos(tmp_path, "a", 1)

    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[folder])
    qtbot.addWidget(dialog)
    dialog.animate_videos_checkbox.setChecked(True)

    assert len(calls) == 1


def test_toggling_animate_videos_rebuilds_cells_without_resampling(app, qtbot, monkeypatch, tmp_path):
    monkeypatch.setattr(MediaFolderPickerDialog, "_grab_video_frame", lambda self, path: None)
    monkeypatch.setattr(MediaFolderPickerDialog, "_make_video_loop_cell", lambda self, path: QLabel("loop"))
    folder = _make_folder_with_videos(tmp_path, "a", 1)

    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[folder])
    qtbot.addWidget(dialog)
    thumbnails_before = list(dialog._current_thumbnails)

    dialog.animate_videos_checkbox.setChecked(True)

    assert dialog._current_thumbnails == thumbnails_before


def test_discard_cell_stops_movie_if_present(app, qtbot):
    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[])
    qtbot.addWidget(dialog)

    widget = QLabel()
    stopped = []
    widget._movie = type("FakeMovie", (), {"stop": lambda self: stopped.append(True)})()

    dialog._discard_cell(widget)

    assert stopped == [True]


def test_discard_cell_stops_player_if_present(app, qtbot):
    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[])
    qtbot.addWidget(dialog)

    widget = QLabel()
    stopped = []
    widget._player = type(
        "FakePlayer", (), {"setSource": lambda self, url: None, "stop": lambda self: stopped.append(True)}
    )()

    dialog._discard_cell(widget)

    assert stopped == [True]


def test_discard_cell_clears_source_before_stopping_player(app, qtbot):
    """Regression guard: measured against a real file, calling player.stop() while a
    QVideoWidget output is still attached to an actively-playing source hangs
    indefinitely - clearing the source first avoids that, but only if it happens
    strictly BEFORE stop()."""
    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[])
    qtbot.addWidget(dialog)

    widget = QLabel()
    calls = []
    widget._player = type(
        "FakePlayer",
        (),
        {
            "setSource": lambda self, url: calls.append(("setSource", url)),
            "stop": lambda self: calls.append(("stop",)),
        },
    )()

    dialog._discard_cell(widget)

    assert [c[0] for c in calls] == ["setSource", "stop"]


def test_discard_cell_handles_plain_widget_without_error(app, qtbot):
    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[])
    qtbot.addWidget(dialog)

    dialog._discard_cell(QLabel())  # must not raise


def test_video_cap_enforced_on_initial_refresh(app, qtbot, monkeypatch, tmp_path):
    monkeypatch.setattr(MediaFolderPickerDialog, "_grab_video_frame", lambda self, path: None)
    video_folder = _make_folder_with_videos(tmp_path, "videos", 50)
    image_folder = _make_folder_with_files(tmp_path, "images", 50)

    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[video_folder, image_folder])
    qtbot.addWidget(dialog)

    video_count = sum(1 for p in dialog._current_thumbnails if dialog._is_video(p))
    assert video_count <= 4


def test_video_cap_enforced_after_growing_via_resize(app, qtbot, monkeypatch, tmp_path):
    monkeypatch.setattr(MediaFolderPickerDialog, "_grab_video_frame", lambda self, path: None)
    video_folder = _make_folder_with_videos(tmp_path, "videos", 50)
    image_folder = _make_folder_with_files(tmp_path, "images", 50)

    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[video_folder, image_folder])
    qtbot.addWidget(dialog)

    dialog._adjust_thumbnail_count(40, columns=6)

    video_count = sum(1 for p in dialog._current_thumbnails if dialog._is_video(p))
    assert video_count <= 4


# --- black-frame detection (real QImage objects - no multimedia backend involved) ---


def _solid_image(color):
    image = QImage(16, 16, QImage.Format.Format_RGB32)
    image.fill(color)
    return image


def test_is_mostly_black_detects_a_black_frame():
    assert MediaFolderPickerDialog._is_mostly_black(_solid_image(0x000000)) is True


def test_is_mostly_black_rejects_a_bright_frame():
    assert MediaFolderPickerDialog._is_mostly_black(_solid_image(0xFF00BF)) is False


def test_is_mostly_black_respects_custom_threshold():
    dark_gray = _solid_image(0x101010)  # average channel value 16
    assert MediaFolderPickerDialog._is_mostly_black(dark_gray, threshold=10) is False
    assert MediaFolderPickerDialog._is_mostly_black(dark_gray, threshold=20) is True


def test_average_brightness_orders_dark_to_bright():
    black = _solid_image(0x000000)
    dark_gray = _solid_image(0x101010)
    bright = _solid_image(0xFF00BF)

    assert MediaFolderPickerDialog._average_brightness(black) < MediaFolderPickerDialog._average_brightness(dark_gray)
    assert MediaFolderPickerDialog._average_brightness(dark_gray) < MediaFolderPickerDialog._average_brightness(bright)


# --- reentrancy guard ---
#
# _grab_video_frame blocks for up to several seconds pumping app.processEvents(), which can
# let a pending resize-debounce timeout (or a button click) dispatch *while* a rebuild is
# still mid-flight, mutating the same _thumbnail_cells/_current_thumbnails lists. These tests
# simulate "already mid-rebuild" directly rather than trying to time a real reentrant call.


def test_refresh_thumbnails_is_noop_while_already_rebuilding(app, qtbot, tmp_path):
    folder = _make_folder_with_files(tmp_path, "a", 5)
    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[folder])
    qtbot.addWidget(dialog)
    before_thumbnails = list(dialog._current_thumbnails)
    before_cells = list(dialog._thumbnail_cells)

    dialog._is_rebuilding = True
    dialog._refresh_thumbnails()
    dialog._is_rebuilding = False

    assert dialog._current_thumbnails == before_thumbnails
    assert dialog._thumbnail_cells == before_cells


def test_adjust_thumbnail_count_is_noop_while_already_rebuilding(app, qtbot, tmp_path):
    folder = _make_folder_with_files(tmp_path, "a", 200)  # more files than fit, so growth is real
    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[folder])
    qtbot.addWidget(dialog)
    before_thumbnails = list(dialog._current_thumbnails)

    dialog._is_rebuilding = True
    dialog._adjust_thumbnail_count(len(before_thumbnails) + 5, columns=4)
    dialog._is_rebuilding = False

    assert dialog._current_thumbnails == before_thumbnails


def test_rebuild_cells_in_place_is_noop_while_already_rebuilding(app, qtbot, tmp_path):
    folder = _make_folder_with_files(tmp_path, "a", 5)
    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[folder])
    qtbot.addWidget(dialog)
    before_cells = list(dialog._thumbnail_cells)

    dialog._is_rebuilding = True
    dialog._rebuild_cells_in_place()
    dialog._is_rebuilding = False

    assert dialog._thumbnail_cells == before_cells


def test_rebuild_flag_is_cleared_after_a_normal_refresh(app, qtbot, tmp_path):
    folder = _make_folder_with_files(tmp_path, "a", 5)
    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[folder])
    qtbot.addWidget(dialog)

    assert dialog._is_rebuilding is False


# --- busy feedback during a rebuild ---
#
# The busy cursor/disabled-buttons only show once a rebuild has been running longer than
# BUSY_INDICATOR_DELAY_MS - a fast rebuild (plain images, no video grab) never crosses that
# threshold and must show no visible feedback at all, matching how it behaved before this
# feedback existed. The _is_rebuilding correctness guard (Fix 5) is unaffected by any of
# this - it's set/cleared immediately regardless of whether the delay elapses.


def test_no_busy_indicators_for_a_fast_rebuild(app, qtbot, tmp_path):
    folder = _make_folder_with_files(tmp_path, "a", 5)
    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[folder])
    qtbot.addWidget(dialog)

    dialog._rescan_and_refresh()

    assert QApplication.overrideCursor() is None
    assert dialog.btn_add_folder.isEnabled() is True
    assert dialog.btn_cancel.isEnabled() is True
    assert dialog.btn_start.isEnabled() is True


def test_busy_indicators_shown_once_a_rebuild_crosses_the_delay_threshold(app, qtbot, monkeypatch, tmp_path):
    folder = _make_folder_with_files(tmp_path, "a", 5)
    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[folder])
    qtbot.addWidget(dialog)

    seen = {}
    original_make_cell = dialog._make_thumbnail_cell

    def slow_make_cell(path):
        if "waited" not in seen:
            seen["waited"] = True
            qtbot.wait(BUSY_INDICATOR_DELAY_MS + 150)  # let the delay timer's tick land
        seen.setdefault("add_enabled", dialog.btn_add_folder.isEnabled())
        seen.setdefault("cancel_enabled", dialog.btn_cancel.isEnabled())
        seen.setdefault("cursor", QApplication.overrideCursor())
        return original_make_cell(path)

    monkeypatch.setattr(dialog, "_make_thumbnail_cell", slow_make_cell)
    dialog._rescan_and_refresh()

    assert seen["add_enabled"] is False
    assert seen["cancel_enabled"] is False
    assert seen["cursor"] is not None
    assert seen["cursor"].shape() == Qt.CursorShape.WaitCursor

    assert QApplication.overrideCursor() is None
    assert dialog.btn_add_folder.isEnabled() is True
    assert dialog.btn_cancel.isEnabled() is True
    assert dialog.btn_start.isEnabled() is True


# --- per-folder scan caching ---


def test_adding_a_folder_does_not_rewalk_previously_scanned_folders(app, qtbot, monkeypatch, tmp_path):
    folder_a = _make_folder_with_files(tmp_path, "a", 3)
    folder_b = _make_folder_with_files(tmp_path, "b", 2)

    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[folder_a])
    qtbot.addWidget(dialog)

    calls = []
    original = media_kinds.find_supported_files

    def spy(folder):
        calls.append(folder)
        return original(folder)

    monkeypatch.setattr(media_kinds, "find_supported_files", spy)
    monkeypatch.setattr(QFileDialog, "getExistingDirectory", lambda *a, **k: folder_b)

    dialog._on_add_folder()

    assert calls == [folder_b]
    assert len(dialog._per_folder_files[folder_a]) == 3
    assert len(dialog._per_folder_files[folder_b]) == 2


def test_removing_a_folder_does_not_rescan_remaining_folders(app, qtbot, monkeypatch, tmp_path):
    folder_a = _make_folder_with_files(tmp_path, "a", 3)
    folder_b = _make_folder_with_files(tmp_path, "b", 2)

    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[folder_a, folder_b])
    qtbot.addWidget(dialog)

    calls = []
    monkeypatch.setattr(media_kinds, "find_supported_files", lambda folder: calls.append(folder) or [])

    dialog.folder_list.setCurrentRow(0)
    dialog._on_remove_folder()

    assert calls == []
    assert folder_a not in dialog._per_folder_files
    assert len(dialog._per_folder_files[folder_b]) == 2


def test_rescan_invalidates_cache_for_a_folder_that_disappeared(app, qtbot, tmp_path):
    folder_a = _make_folder_with_files(tmp_path, "a", 3)
    dialog = MediaFolderPickerDialog(parent=app, initial_folders=[folder_a])
    qtbot.addWidget(dialog)
    assert len(dialog._per_folder_files[folder_a]) == 3

    shutil.rmtree(folder_a)
    dialog._rescan_and_refresh()

    assert dialog._per_folder_files[folder_a] == []
