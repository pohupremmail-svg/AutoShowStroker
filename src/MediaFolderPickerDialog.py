import json
import random
import time
from pathlib import Path

from PyQt6.QtCore import QSize, Qt, QTimer, QUrl
from PyQt6.QtGui import QMovie, QPixmap
from PyQt6.QtMultimedia import QAudioOutput, QMediaPlayer, QVideoSink
from PyQt6.QtMultimediaWidgets import QVideoWidget
from PyQt6.QtWidgets import (
    QAbstractItemView,
    QApplication,
    QCheckBox,
    QDialog,
    QFileDialog,
    QGridLayout,
    QHBoxLayout,
    QLabel,
    QListWidget,
    QListWidgetItem,
    QPushButton,
    QScrollArea,
    QStyle,
    QVBoxLayout,
    QWidget,
)

from src import media_kinds, theme
from src.thumbnail_sampling import compute_thumbnail_grid, sample_thumbnails_with_video_cap

THUMBNAIL_CELL_SIZE = (140, 140)
GRID_SPACING = 8
GRID_MARGIN = 8
# Fallback only - _scrollbar_width_reserve() queries the real metric from the active style
# (QStyle.PixelMetric.PM_ScrollBarExtent) so this guessed value doesn't need to match any
# particular theme/DPI setting exactly. Reserved so a vertical scrollbar appearing (it eats
# viewport width) never shrinks the usable width *after* columns were already decided - see
# compute_thumbnail_grid's docstring for why an inaccurate footprint calc causes both a
# stray scrollbar and a partially-empty last row.
SCROLLBAR_WIDTH_RESERVE = 24
RESIZE_DEBOUNCE_MS = 150

# Videos are far heavier to thumbnail/animate than images or gifs (each animated one needs
# its own live QMediaPlayer), so the sampling itself never picks more than this many across
# the whole grid, regardless of grid size.
MAX_VIDEO_THUMBNAILS = 4
VIDEO_LOOP_DURATION_MS = 5000
INITIAL_SEEK_RETRY_MS = 300

# A plain-image rebuild is near-instant (tens of ms) - only show the busy cursor/disabled
# buttons if a rebuild is still running after this long, so the common fast case stays
# exactly as unobtrusive as before this feedback existed. Video frame-grabbing is what
# actually crosses this threshold in practice.
BUSY_INDICATOR_DELAY_MS = 250

VIDEO_METADATA_WAIT_TIMEOUT_S = 1.0
VIDEO_FRAME_WAIT_TIMEOUT_S = 1.0
VIDEO_FRAME_GRAB_ATTEMPTS = 3
VIDEO_BLACK_FRAME_BRIGHTNESS_THRESHOLD = 20


class MediaFolderPickerDialog(QDialog):
    def __init__(self, parent=None, initial_folders=None):
        super().__init__(parent)
        self.setWindowTitle("Select Gooning Folders")
        self.setModal(True)
        self.resize(900, 650)

        self.main_app = parent
        self.folders: list[str] = []
        self._per_folder_files: dict[str, list[Path]] = {}
        self.selected_files: list[Path] = []
        self._current_thumbnails: list[Path] = []
        self._thumbnail_cells: list[QWidget] = []
        self._last_grid_count = 0
        # _grab_video_frame blocks for up to several seconds pumping app.processEvents(),
        # which lets a pending resize-debounce timeout (or a button click) get dispatched
        # *while* a rebuild is still mid-flight, mutating these same lists reentrantly -
        # this guards _refresh_thumbnails/_adjust_thumbnail_count/_rebuild_cells_in_place
        # against running inside one another.
        self._is_rebuilding = False
        self._busy_indicators_shown = False

        self._resize_timer = QTimer(self)
        self._resize_timer.setSingleShot(True)
        self._resize_timer.timeout.connect(self._apply_pending_resize)

        self._busy_delay_timer = QTimer(self)
        self._busy_delay_timer.setSingleShot(True)
        self._busy_delay_timer.timeout.connect(self._show_busy_indicators)

        self._build_ui()
        self._load_initial_folders(initial_folders)

    def _build_ui(self):
        layout = QVBoxLayout(self)

        layout.addWidget(QLabel("Folders"))

        self.folder_list = QListWidget()
        self.folder_list.setSelectionMode(QAbstractItemView.SelectionMode.SingleSelection)
        self.folder_list.itemSelectionChanged.connect(self._update_remove_button_enabled)
        layout.addWidget(self.folder_list)

        folder_buttons = QHBoxLayout()
        self.btn_add_folder = QPushButton("Add Folder...")
        self.btn_add_folder.clicked.connect(self._on_add_folder)
        self.btn_remove_folder = QPushButton("Remove Selected")
        self.btn_remove_folder.setEnabled(False)
        self.btn_remove_folder.clicked.connect(self._on_remove_folder)
        folder_buttons.addWidget(self.btn_add_folder)
        folder_buttons.addWidget(self.btn_remove_folder)
        layout.addLayout(folder_buttons)

        layout.addWidget(QLabel("Preview"))

        # Always starts unchecked and is never persisted - a deliberate choice so a real
        # thumbnail (not a live decode) is the default every time the dialog opens.
        self.animate_videos_checkbox = QCheckBox("Animate video clips (5s loop)")
        self.animate_videos_checkbox.setChecked(False)
        self.animate_videos_checkbox.toggled.connect(self._on_animate_videos_toggled)
        layout.addWidget(self.animate_videos_checkbox)

        self.thumbnail_grid_widget = QWidget()
        self.thumbnail_grid_layout = QGridLayout(self.thumbnail_grid_widget)
        self.thumbnail_grid_layout.setSpacing(GRID_SPACING)
        self.thumbnail_grid_layout.setContentsMargins(GRID_MARGIN, GRID_MARGIN, GRID_MARGIN, GRID_MARGIN)

        self.thumbnail_scroll = QScrollArea()
        self.thumbnail_scroll.setWidgetResizable(True)
        self.thumbnail_scroll.setWidget(self.thumbnail_grid_widget)
        layout.addWidget(self.thumbnail_scroll, stretch=1)

        action_buttons = QHBoxLayout()
        self.btn_cancel = QPushButton("Cancel")
        self.btn_cancel.clicked.connect(self.reject)
        self.btn_start = QPushButton("Start")
        self.btn_start.setObjectName("primary")
        self.btn_start.setEnabled(False)
        self.btn_start.clicked.connect(self._on_start)
        action_buttons.addWidget(self.btn_cancel)
        action_buttons.addWidget(self.btn_start)
        layout.addLayout(action_buttons)

    def _load_initial_folders(self, initial_folders):
        if initial_folders is not None:
            self.folders = list(initial_folders)
        else:
            self.folders = self._read_persisted_folders()
        self._rescan_and_refresh()

    def _read_persisted_folders(self):
        settings = getattr(self.main_app, "settings", None)
        if settings is None:
            return []
        raw = settings.value("GoonerApp/last_selected_folders", "[]")
        try:
            return json.loads(raw)
        except (TypeError, ValueError):
            return []

    def _update_remove_button_enabled(self):
        self.btn_remove_folder.setEnabled(bool(self.folder_list.selectedItems()))

    def _on_add_folder(self):
        folder = QFileDialog.getExistingDirectory(self, "Add Gooning Folder")
        if folder and folder not in self.folders:
            self.folders.append(folder)
            self._rescan_and_refresh()

    def _on_remove_folder(self):
        items = self.folder_list.selectedItems()
        if not items:
            return
        folder = items[0].data(Qt.ItemDataRole.UserRole)
        if folder in self.folders:
            self.folders.remove(folder)
        self._rescan_and_refresh()

    def _rescan_and_refresh(self):
        # Only newly-added folders get walked - a folder already in the cache keeps its
        # scanned file list as-is (dropping it entirely on every add/remove elsewhere was
        # forcing a full recursive re-walk of every OTHER folder too, on every single
        # change, however large the library). This does mean an external change to an
        # already-added folder's contents won't be picked up until it's removed and
        # re-added - an acceptable trade-off for a dialog that's opened fresh each session.
        self._per_folder_files = {
            folder: files
            for folder, files in self._per_folder_files.items()
            if folder in self.folders and Path(folder).exists()
        }
        self.folder_list.clear()
        for folder in self.folders:
            exists = Path(folder).exists()
            if folder not in self._per_folder_files:
                self._per_folder_files[folder] = media_kinds.find_supported_files(folder) if exists else []
            files = self._per_folder_files[folder]

            label = f"{folder}  ({len(files)} files)" if exists else f"{folder}  (folder not found)"
            item = QListWidgetItem(label)
            item.setData(Qt.ItemDataRole.UserRole, folder)
            self.folder_list.addItem(item)

        has_any_files = any(self._per_folder_files.values())
        self.btn_start.setEnabled(has_any_files)

        self._refresh_thumbnails()

    # --- thumbnail grid ---

    @staticmethod
    def _is_video(path):
        return media_kinds.media_kind(path) == "video"

    def _sample_thumbnails(self, folder_files, count, video_budget):
        return sample_thumbnails_with_video_cap(folder_files, count, video_budget, is_video=self._is_video)

    def _viewport_size(self):
        viewport = self.thumbnail_scroll.viewport()
        return viewport.width(), viewport.height()

    def _scrollbar_width_reserve(self):
        """The actual scrollbar width for the active style/theme/DPI, rather than a fixed
        guess - a guessed value can be wrong in either direction: too small lets the exact
        overflow compute_thumbnail_grid's docstring describes recur, too large wastes a
        column of usable width on every layout."""
        metric = self.style().pixelMetric(QStyle.PixelMetric.PM_ScrollBarExtent)
        return metric if metric > 0 else SCROLLBAR_WIDTH_RESERVE

    def _current_grid_dimensions(self):
        """Returns (columns, rows, count) for the current viewport - computed once and
        threaded through the rest of a refresh/resize cycle so sampling-count and layout
        reflow never disagree on the column count (see compute_thumbnail_grid's docstring)."""
        width, height = self._viewport_size()
        width = max(0, width - self._scrollbar_width_reserve())
        cell_w, cell_h = THUMBNAIL_CELL_SIZE
        return compute_thumbnail_grid(width, height, cell_w, cell_h, GRID_SPACING, GRID_MARGIN)

    def _current_grid_count(self):
        return self._current_grid_dimensions()[2]

    def _refresh_thumbnails(self):
        """Full resample - discards and re-decodes everything. Only call this when the
        candidate file pool actually changed (folder added/removed/rescanned), never on
        a plain resize - see _adjust_thumbnail_count for the resize-only path.

        No-ops (returns immediately) if a rebuild is already in progress - see
        self._is_rebuilding's docstring in __init__."""
        if self._is_rebuilding:
            return
        self._begin_rebuild()
        try:
            for widget in self._thumbnail_cells:
                self._discard_cell(widget)
            self._thumbnail_cells = []
            self._current_thumbnails = []

            columns, _rows, count = self._current_grid_dimensions()
            self._last_grid_count = count

            for path in self._sample_thumbnails(self._per_folder_files, count, MAX_VIDEO_THUMBNAILS):
                self._add_thumbnail_cell(path)
            self._reflow_grid_layout(columns)
        finally:
            self._end_rebuild()

    def _adjust_thumbnail_count(self, target_count, columns):
        """Grows/shrinks the existing grid without resampling or re-decoding cells that
        stay - only new cells (on growth) get decoded, only excess cells (on shrink) get
        dropped. Called from the resize debounce, never a full resample.

        No-ops if a rebuild is already in progress - see self._is_rebuilding's docstring."""
        if self._is_rebuilding:
            return
        self._begin_rebuild()
        try:
            current_count = len(self._current_thumbnails)
            if target_count > current_count:
                additional = target_count - current_count
                existing_set = set(self._current_thumbnails)
                remaining = {
                    folder: [f for f in files if f not in existing_set]
                    for folder, files in self._per_folder_files.items()
                }
                current_video_count = sum(1 for p in self._current_thumbnails if self._is_video(p))
                video_budget = max(0, MAX_VIDEO_THUMBNAILS - current_video_count)
                for path in self._sample_thumbnails(remaining, additional, video_budget):
                    self._add_thumbnail_cell(path)
            elif target_count < current_count:
                for _ in range(current_count - target_count):
                    self._current_thumbnails.pop()
                    widget = self._thumbnail_cells.pop()
                    self._discard_cell(widget)

            self._reflow_grid_layout(columns)
        finally:
            self._end_rebuild()

    def _add_thumbnail_cell(self, path):
        cell = self._make_thumbnail_cell(path)
        self._current_thumbnails.append(path)
        self._thumbnail_cells.append(cell)

    def _discard_cell(self, widget):
        """Stops any live QMovie/QMediaPlayer a cell owns before hiding/deleting it - without
        this, a discarded animated cell keeps decoding/ticking in the background even though
        it's no longer shown."""
        movie = getattr(widget, "_movie", None)
        if movie is not None:
            movie.stop()
        loop_timer = getattr(widget, "_loop_timer", None)
        if loop_timer is not None:
            loop_timer.stop()
        player = getattr(widget, "_player", None)
        if player is not None:
            # Measured against a real file: calling stop() while a QVideoWidget output is
            # still attached to an actively-playing source hangs indefinitely - clearing
            # the source first (which itself halts playback) avoids that entirely.
            player.setSource(QUrl())
            player.stop()
        # hide() synchronously - deleteLater()'s actual destruction is deferred to the next
        # event loop pass, and a widget removed from a layout stays visible at its last
        # position until then, so without this it briefly renders as a stale/overlapping
        # leftover cell.
        widget.hide()
        widget.deleteLater()

    def _reflow_grid_layout(self, columns):
        while self.thumbnail_grid_layout.count():
            self.thumbnail_grid_layout.takeAt(0)

        columns = max(1, columns)
        for index, widget in enumerate(self._thumbnail_cells):
            row, col = divmod(index, columns)
            self.thumbnail_grid_layout.addWidget(widget, row, col)

    def _on_animate_videos_toggled(self, _checked):
        self._rebuild_cells_in_place()

    def _rebuild_cells_in_place(self):
        """Re-renders the *same* sampled paths (no resampling) - used when the animate-
        videos toggle changes, so switching it doesn't also shuffle in a fresh random set.

        No-ops if a rebuild is already in progress - see self._is_rebuilding's docstring."""
        if self._is_rebuilding:
            return
        self._begin_rebuild()
        try:
            old_cells = list(self._thumbnail_cells)
            self._thumbnail_cells = [self._make_thumbnail_cell(path) for path in self._current_thumbnails]
            for widget in old_cells:
                self._discard_cell(widget)
            columns, _rows, _count = self._current_grid_dimensions()
            self._reflow_grid_layout(columns)
        finally:
            self._end_rebuild()

    def _begin_rebuild(self):
        """Starts the reentrancy guard immediately (see self._is_rebuilding's docstring) -
        but the *visible* busy cursor/disabled buttons are deliberately deferred behind
        BUSY_INDICATOR_DELAY_MS via _show_busy_indicators, not shown here. A plain-image
        rebuild finishes in tens of ms with no event-loop pumping in between, so an
        immediate cursor/disable would flicker on and off for every dialog open even when
        nothing is actually slow - only _grab_video_frame's processEvents() spin-wait runs
        long enough to let this delay timer's tick actually land."""
        self._is_rebuilding = True
        self._busy_delay_timer.start(BUSY_INDICATOR_DELAY_MS)

    def _show_busy_indicators(self):
        self._busy_indicators_shown = True
        self.btn_add_folder.setEnabled(False)
        self.btn_remove_folder.setEnabled(False)
        self.btn_start.setEnabled(False)
        self.btn_cancel.setEnabled(False)
        QApplication.setOverrideCursor(Qt.CursorShape.WaitCursor)

    def _end_rebuild(self):
        self._busy_delay_timer.stop()
        if self._busy_indicators_shown:
            QApplication.restoreOverrideCursor()
            self._busy_indicators_shown = False
        self._is_rebuilding = False
        self.btn_add_folder.setEnabled(True)
        self.btn_cancel.setEnabled(True)
        self._update_remove_button_enabled()
        self.btn_start.setEnabled(any(self._per_folder_files.values()))

    # --- cell construction ---

    def _make_thumbnail_cell(self, path) -> QWidget:
        kind = media_kinds.media_kind(path)

        if kind == "gif":
            return self._make_gif_cell(path)

        if kind == "video":
            if self.animate_videos_checkbox.isChecked():
                return self._make_video_loop_cell(path)
            return self._make_static_cell(path, self._grab_video_frame(path))

        return self._make_static_cell(path, self._make_thumbnail_pixmap(path))

    def _make_static_cell(self, path, pixmap) -> QWidget:
        label = QLabel()
        label.setFixedSize(*THUMBNAIL_CELL_SIZE)
        label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        label.setWordWrap(True)
        label.setStyleSheet(
            f"background-color: {theme.SURFACE_DARK}; border-radius: 8px; color: {theme.TEXT};"
        )
        if pixmap is not None:
            label.setPixmap(pixmap)
        else:
            label.setText(Path(path).name)
        return label

    def _make_thumbnail_pixmap(self, path):
        size = THUMBNAIL_CELL_SIZE[0] - 8
        try:
            pixmap = QPixmap(str(path))
            if pixmap.isNull():
                return None
            return pixmap.scaled(
                size, size, Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.SmoothTransformation
            )
        except Exception:
            return None

    def _make_gif_cell(self, path) -> QWidget:
        label = QLabel()
        label.setFixedSize(*THUMBNAIL_CELL_SIZE)
        label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        label.setWordWrap(True)
        label.setStyleSheet(
            f"background-color: {theme.SURFACE_DARK}; border-radius: 8px; color: {theme.TEXT};"
        )

        try:
            movie = QMovie(str(path))
            movie.jumpToFrame(0)
            first_frame = movie.currentImage()
            if first_frame.isNull():
                label.setText(Path(path).name)
                return label

            size = THUMBNAIL_CELL_SIZE[0] - 8
            scaled_size = first_frame.size().scaled(QSize(size, size), Qt.AspectRatioMode.KeepAspectRatio)
            movie.setScaledSize(scaled_size)
            label.setMovie(movie)
            movie.start()
            label._movie = movie  # keep alive - see _discard_cell
        except Exception:
            label.setText(Path(path).name)
        return label

    def _wait_for(self, predicate, timeout_s):
        """Spins the event loop until `predicate()` is true or `timeout_s` elapses."""
        app = QApplication.instance()
        deadline = time.monotonic() + timeout_s
        while not predicate() and time.monotonic() < deadline:
            app.processEvents()
        return predicate()

    @staticmethod
    def _average_brightness(image):
        """Cheap average-brightness check on a downscaled copy - used to catch black
        leader frames/fades so the static thumbnail doesn't randomly land on one."""
        sample = image.scaled(16, 16)
        total = 0
        count = 0
        for y in range(sample.height()):
            for x in range(sample.width()):
                color = sample.pixelColor(x, y)
                total += (color.red() + color.green() + color.blue()) / 3
                count += 1
        return total / count if count > 0 else 0

    @classmethod
    def _is_mostly_black(cls, image, threshold=VIDEO_BLACK_FRAME_BRIGHTNESS_THRESHOLD):
        return cls._average_brightness(image) < threshold

    def _grab_video_frame(self, path):
        """Synchronous frame grab via QMediaPlayer+QVideoSink - deliberately seeks to a
        random position in the middle portion of the clip (never frame 0, which is prone
        to black leaders/fades) and retries a few times if the grabbed frame turns out
        mostly black, rather than settling for the first frame that arrives.

        The QVideoFrame must be converted to a QImage immediately inside the
        videoFrameChanged callback - doing it later (even a moment after the signal fires)
        reliably yields a null image."""
        player = QMediaPlayer()
        sink = QVideoSink()
        player.setVideoSink(sink)
        holder = {}

        def on_frame(frame):
            if frame.isValid():
                image = frame.toImage()
                if not image.isNull():
                    holder["image"] = image

        sink.videoFrameChanged.connect(on_frame)

        best_image = None
        best_brightness = -1
        try:
            player.setSource(QUrl.fromLocalFile(str(path)))
            player.play()

            self._wait_for(lambda: player.duration() > 0, VIDEO_METADATA_WAIT_TIMEOUT_S)
            duration_ms = player.duration()

            attempts = VIDEO_FRAME_GRAB_ATTEMPTS if duration_ms > 0 else 1
            for _ in range(attempts):
                holder.pop("image", None)
                if duration_ms > 0:
                    margin = duration_ms * 0.1
                    low, high = margin, max(margin, duration_ms - margin)
                    target_ms = int(random.uniform(low, high)) if high > low else int(duration_ms / 2)
                    player.setPosition(target_ms)

                self._wait_for(lambda: "image" in holder, VIDEO_FRAME_WAIT_TIMEOUT_S)
                image = holder.get("image")
                if image is None:
                    continue
                # keep the least-black frame seen across attempts, not just the first
                # successful grab - a later attempt can be dark-but-less-dark than an
                # earlier one without ever passing the "not mostly black" threshold
                brightness = self._average_brightness(image)
                if brightness > best_brightness:
                    best_image = image
                    best_brightness = brightness
                if brightness >= VIDEO_BLACK_FRAME_BRIGHTNESS_THRESHOLD:
                    break
        except Exception:
            return None
        finally:
            player.stop()

        if best_image is None:
            return None

        size = THUMBNAIL_CELL_SIZE[0] - 8
        return QPixmap.fromImage(best_image).scaled(
            size, size, Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.SmoothTransformation
        )

    def _make_video_loop_cell(self, path) -> QWidget:
        video_widget = QVideoWidget()
        video_widget.setFixedSize(*THUMBNAIL_CELL_SIZE)
        video_widget.setStyleSheet(f"background-color: {theme.SURFACE_DARK}; border-radius: 8px;")

        player = QMediaPlayer()
        # Infinite native looping is just a safety net for when the 5s window runs past
        # the end of a short clip - the wall-clock timer below is what actually enforces
        # the 5s segment, so this never causes the clip to play beyond that on its own.
        player.setLoops(QMediaPlayer.Loops.Infinite)
        audio_output = QAudioOutput()
        audio_output.setVolume(0.0)
        player.setAudioOutput(audio_output)
        player.setVideoOutput(video_widget)

        # Looping an arbitrary sub-window isn't a QMediaPlayer feature - a single timer does
        # double duty here. While start_ms is still unknown it retries the initial seek every
        # INITIAL_SEEK_RETRY_MS (duration()/isSeekable() turning true doesn't reliably mean a
        # setPosition() right then will actually stick - measured against a real file, seeking
        # immediately when durationChanged/mediaStatusChanged first fire was sometimes silently
        # dropped, while retrying a tick later always worked). Once the seek sticks, the same
        # timer's interval switches to VIDEO_LOOP_DURATION_MS and every tick after that just
        # jumps back to start_ms. A start position anywhere in the clip works, including near
        # the end, since native infinite looping (above) transparently wraps play-back to 0:00
        # if the 5s window would otherwise run past the end.
        state = {"start_ms": None}

        def on_tick():
            if state["start_ms"] is None:
                duration_ms = player.duration()
                if duration_ms <= 0 or not player.isSeekable():
                    return  # not ready yet - retry on the next tick
                # anywhere in the clip, not just early enough to leave 5s of runway after
                start_ms = random.randint(1, duration_ms - 1) if duration_ms > 1 else 0
                state["start_ms"] = start_ms
                player.setPosition(start_ms)
                loop_timer.setInterval(VIDEO_LOOP_DURATION_MS)
            else:
                player.setPosition(state["start_ms"])

        loop_timer = QTimer()
        loop_timer.setInterval(INITIAL_SEEK_RETRY_MS)
        loop_timer.timeout.connect(on_tick)

        player.setSource(QUrl.fromLocalFile(str(path)))
        player.play()
        loop_timer.start()

        video_widget._player = player  # keep alive - see _discard_cell
        video_widget._audio_output = audio_output
        video_widget._loop_timer = loop_timer  # keep alive + stopped in _discard_cell
        return video_widget

    def resizeEvent(self, event):
        super().resizeEvent(event)
        self._resize_timer.start(RESIZE_DEBOUNCE_MS)

    def _apply_pending_resize(self):
        # Always call _adjust_thumbnail_count rather than gating on "did count change" -
        # it already no-ops the grow/shrink work when the count matches, but still reflows
        # with the *current* column count. Gating here on count alone missed resizes that
        # keep columns*rows equal while columns/rows individually swap (e.g. 5x4 -> 4x5),
        # leaving the grid rendered with a stale column count that no longer matches the
        # viewport. The single-shot debounce timer (not this check) is what already
        # guarantees only one call per settled resize.
        columns, _rows, count = self._current_grid_dimensions()
        self._adjust_thumbnail_count(count, columns)
        self._last_grid_count = count

    # --- start/cancel ---

    def _on_start(self):
        self.selected_files = [f for files in self._per_folder_files.values() for f in files]
        settings = getattr(self.main_app, "settings", None)
        if settings is not None:
            settings.setValue("GoonerApp/last_selected_folders", json.dumps(self.folders))
        self.accept()

    def done(self, result):
        """accept(), reject(), and the native window-close button all funnel through this -
        without stopping cells here, any live GIF/video cell just keeps decoding/looping in
        the background indefinitely after the dialog closes."""
        for widget in self._thumbnail_cells:
            self._discard_cell(widget)
        self._thumbnail_cells = []
        super().done(result)
