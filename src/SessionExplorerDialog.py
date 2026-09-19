from pathlib import Path

from PyQt6.QtCore import QRectF, Qt, QUrl, pyqtSignal
from PyQt6.QtGui import QColor, QDesktopServices, QPainter, QPen, QPixmap
from PyQt6.QtWidgets import (
    QDialog,
    QFrame,
    QHBoxLayout,
    QLabel,
    QMessageBox,
    QPushButton,
    QVBoxLayout,
    QWidget,
)

from src import media_kinds, theme
from src.utils import format_clock, load_scaled_pixmap
from src.video_thumbnails import VideoThumbnailQueue

BAR_HEIGHT = 54
BAR_RADIUS = 6
PLAYHEAD_WIDTH = 2
PREVIEW_MIN_HEIGHT = 340

KIND_LABELS = {"beat": "Beat", "pause": "Pause", "finale": "Finale"}
# A pause has to read as a rest rather than a quiet beat, which is the same reason the
# Strokemeter gives it its own colour.
KIND_COLORS = {"beat": theme.SECONDARY, "pause": theme.PAUSE, "finale": theme.ACCENT}
# Same colours the climax banner uses in GoonerApp, so an outcome reads the same in both.
OUTCOME_COLORS = {"real": theme.ACCENT, "ruined": theme.RUINED, "denied": theme.DENIED}
OUTCOME_LABELS = {"real": "Climax", "ruined": "Ruined climax", "denied": "Denied"}
# How close the playhead has to be to a fake-out for the caption to name it. A fake lasts
# only until its reveal a few seconds later, so this is the window it was "happening" in.
FAKE_WINDOW_SEC = 4.0


class SessionTimelineBar(QFrame):
    """The session drawn end to end, scrubbed with the mouse.

    Every segment is a block as wide as it was long, so the shape of the session is visible
    at a glance - long slow stretches, the pauses cut into them, the run-in at the end. The
    playhead follows the cursor; the dialog turns that into "what was on screen here".
    """

    scrubbed = pyqtSignal(float)  # seconds from the start of the session

    def __init__(self, timeline, parent=None):
        super().__init__(parent)
        self._start = timeline.get("started_at") or 0.0
        self._duration = max(0.0, (timeline.get("ended_at") or self._start) - self._start)
        self._segments = timeline["segments"]
        self._outcome = timeline.get("climax_outcome")
        climax_at = timeline.get("climax_at")
        self.climax_offset = None if climax_at is None else climax_at - self._start
        self.fake_offsets = [at - self._start for at in timeline.get("fake_climaxes", [])]
        self.playhead_offset = None

        self.setFixedHeight(BAR_HEIGHT)
        self.setMouseTracking(True)  # scrub on hover, no click needed - as a video seek bar does
        self.setCursor(Qt.CursorShape.PointingHandCursor)

    # --- geometry ---

    def time_at_x(self, x) -> float:
        if self._duration <= 0 or self.width() <= 0:
            return 0.0
        return max(0.0, min(self._duration, x / self.width() * self._duration))

    def x_for_time(self, offset) -> float:
        if self._duration <= 0:
            return 0.0
        return offset / self._duration * self.width()

    # --- interaction ---

    def mouseMoveEvent(self, event):
        self.scrubbed.emit(self.time_at_x(event.position().x()))
        super().mouseMoveEvent(event)

    def mousePressEvent(self, event):
        self.scrubbed.emit(self.time_at_x(event.position().x()))
        super().mousePressEvent(event)

    def move_playhead(self, offset):
        self.playhead_offset = offset
        self.update()

    # --- painting ---

    def paintEvent(self, event):
        super().paintEvent(event)
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        painter.setPen(Qt.PenStyle.NoPen)

        painter.setBrush(QColor(theme.SURFACE_DARKEST))
        painter.drawRoundedRect(QRectF(0, 0, self.width(), self.height()), BAR_RADIUS, BAR_RADIUS)

        for data in self._segments:
            left = self.x_for_time(data["start"] - self._start)
            right = self.x_for_time(data["end"] - self._start)
            painter.setBrush(QColor(KIND_COLORS.get(data["kind"], theme.SECONDARY)))
            # Never thinner than a pixel: a two-second pause in a thirty-minute session
            # would otherwise round away to nothing at all.
            painter.drawRect(QRectF(left, 0, max(1.0, right - left), self.height()))

        # A seam at every boundary, or two beat segments of the same kind run together into
        # one block and the session looks like it changed half as often as it did.
        painter.setPen(QPen(QColor(theme.SURFACE_DARKEST), 1))
        for data in self._segments[1:]:
            x = int(self.x_for_time(data["start"] - self._start))
            painter.drawLine(x, 0, x, self.height())

        # Fake-outs first, so the real climax is drawn over one that landed on top of it.
        for offset in self.fake_offsets:
            x = int(self.x_for_time(offset))
            painter.setPen(QPen(QColor(theme.SURFACE_DARKEST), 5))
            painter.drawLine(x, 0, x, self.height())
            painter.setPen(QPen(QColor(theme.TEXT), 2, Qt.PenStyle.DashLine))
            painter.drawLine(x, 0, x, self.height())

        if self.climax_offset is not None:
            x = int(self.x_for_time(self.climax_offset))
            # Outlined, because the marker sits on the finale block and a "real" outcome is
            # the same colour as it - unoutlined it would be invisible exactly where it
            # matters most.
            painter.setPen(QPen(QColor(theme.SURFACE_DARKEST), 7))
            painter.drawLine(x, 0, x, self.height())
            painter.setPen(QPen(QColor(OUTCOME_COLORS.get(self._outcome, theme.ACCENT)), 3))
            painter.drawLine(x, 0, x, self.height())

        if self.playhead_offset is not None:
            painter.setPen(QPen(QColor(theme.TEXT), PLAYHEAD_WIDTH))
            x = self.x_for_time(self.playhead_offset)
            painter.drawLine(int(x), 0, int(x), self.height())
        painter.end()


class SessionExplorerDialog(QDialog):
    """Scrub back through the session that just ended.

    The timeline runs along the bottom the way a video's seek bar does: move along it and
    the preview shows whatever was on screen at that moment, with the rhythm that was
    playing. The point is finding a particular picture again, so the folder it lives in is
    one click away.

    The timeline is handed in and never stored anywhere: it holds media paths, which this
    app keeps out of its data directory and out of its log.
    """

    def __init__(self, timeline, thumbnail_source=None, save_session=None, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Session Explorer")
        self.setModal(True)
        self.resize(940, 640)

        self._thumbnail_source = thumbnail_source or VideoThumbnailQueue(parent=self)
        self._session_start = timeline.get("started_at") or 0.0
        self._segments = timeline["segments"]
        self._media = self._flatten_media(timeline)
        self._climax_at = timeline.get("climax_at")
        self._outcome = timeline.get("climax_outcome")
        self._fake_climaxes = timeline.get("fake_climaxes", [])
        # Scrubbing crosses the same clip over and over; decoding it each time would make
        # the bar stutter on exactly the move it is built for.
        self._video_frames = {}
        self.selected_path = None
        # A callable returning whether the session landed. The explorer deliberately does
        # not know where it goes - it only knows which session is on screen.
        self._save_session = save_session

        layout = QVBoxLayout(self)
        layout.addWidget(self._build_heading(timeline))
        layout.addWidget(self._build_preview(), 1)
        layout.addWidget(self.moment_label_container())
        self.timeline_bar = SessionTimelineBar(timeline)
        self.timeline_bar.scrubbed.connect(self.scrub_to)
        layout.addWidget(self.timeline_bar)
        layout.addLayout(self._build_actions())

    @staticmethod
    def _flatten_media(timeline):
        """One entry per medium, in order, with duplicates from the carried-over listings
        dropped - the explorer asks "what was on screen at t", not "which segment was it
        filed under"."""
        seen = []
        for data in timeline["segments"]:
            for entry in data["media"]:
                if not entry["carried_over"]:
                    seen.append(entry)
        return seen

    # --- construction ---

    def _build_heading(self, timeline):
        duration = max(0.0, (timeline.get("ended_at") or 0.0) - self._session_start)
        parts = [f"{len(self._segments)} segments over {format_clock(duration)}"]
        if self._fake_climaxes:
            count = len(self._fake_climaxes)
            parts.append(f"{count} fake-out{'s' if count > 1 else ''} survived")
        if self._outcome:
            parts.append(OUTCOME_LABELS.get(self._outcome, self._outcome))
        label = QLabel("  -  ".join(parts))
        label.setStyleSheet(f"color: {theme.TEXT}; font-size: 14px; font-weight: bold;")

        row = QWidget()
        row_layout = QHBoxLayout(row)
        row_layout.setContentsMargins(0, 0, 0, 0)
        row_layout.addWidget(label, 1)
        self.save_button = self._build_save_button()
        if self.save_button is not None:
            row_layout.addWidget(self.save_button)
        return row

    def _build_save_button(self):
        """Saving belongs here: this dialog is already showing the session in question.

        None when the caller gave nowhere to save to - the explorer is also opened on
        sessions that have no shelf behind them, and a dead button is worse than none.
        """
        if self._save_session is None:
            return None
        button = QPushButton("Save this session")
        button.setObjectName("primary")
        button.setToolTip(
            "Keeps the beats, the climax and the media pacing so this session can be "
            "played again."
        )
        button.clicked.connect(self._on_save_clicked)
        return button

    def _build_preview(self):
        self.preview_label = QLabel("Move along the timeline below to see what was on screen.")
        self.preview_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.preview_label.setMinimumHeight(PREVIEW_MIN_HEIGHT)
        self.preview_label.setWordWrap(True)
        self.preview_label.setStyleSheet(
            f"background-color: {theme.SURFACE_DARKEST}; color: {theme.TEXT}; border-radius: 10px;"
        )
        return self.preview_label

    def moment_label_container(self):
        self.moment_label = QLabel("")
        self.moment_label.setStyleSheet(f"color: {theme.ACCENT}; font-size: 13px; font-weight: bold;")
        return self.moment_label

    def _build_actions(self):
        row = QHBoxLayout()
        self.file_label = QLabel("")
        self.file_label.setStyleSheet(f"color: {theme.TEXT}; font-size: 12px;")

        self.reveal_button = QPushButton("Show in folder")
        self.reveal_button.setEnabled(False)
        self.reveal_button.clicked.connect(self._reveal_selected)

        self.play_button = QPushButton("Open in player")
        self.play_button.clicked.connect(self._play_selected)
        self.play_button.hide()

        row.addWidget(self.file_label, 1)
        row.addWidget(self.play_button)
        row.addWidget(self.reveal_button)
        return row

    # --- saving ---

    def _on_save_clicked(self):
        if not self._save_session():
            QMessageBox.warning(
                self,
                "Could not save",
                "This session could not be saved. There may be no room left on the disk.",
            )
            return

        # Disabled rather than hidden: the same session saved twice is two identical
        # entries on the shelf, and a button that vanishes reads as a failure.
        self.save_button.setEnabled(False)
        self.save_button.setText("Saved")
        QMessageBox.information(
            self,
            "Session saved",
            "Saved under Sessions > Saved Sessions, where it can be played again.\n\n"
            "It records the paths of the media it showed, so replaying finds them again. "
            "Delete saved sessions under Help > Privacy & Data, and leave the paths out "
            "when exporting one to somebody else.",
        )

    # --- scrubbing ---

    def scrub_to(self, offset):
        """Shows the moment `offset` seconds into the session."""
        self.timeline_bar.move_playhead(offset)
        moment = self._session_start + offset
        self.moment_label.setText(self._describe_moment(offset, moment))
        self._show_medium(self._medium_at(moment))

    def _describe_moment(self, offset, moment):
        parts = [format_clock(offset)]
        segment = self._segment_at(moment)
        if segment is None:
            return parts[0]
        parts.append(KIND_LABELS.get(segment["kind"], segment["kind"]))
        if segment["freq"]:
            parts.append(f"{segment['freq']:.2f} Hz")
        if segment["pattern"]:
            parts.append(segment["pattern"])
        if any(abs(moment - at) <= FAKE_WINDOW_SEC for at in self._fake_climaxes):
            parts.append("- Fake-out")
        if self._climax_at is not None and segment["start"] <= self._climax_at < segment["end"]:
            parts.append(f"- {OUTCOME_LABELS.get(self._outcome, self._outcome)} landed here")
        return "   ".join(parts)

    def _segment_at(self, moment):
        for data in self._segments:
            if data["start"] <= moment < data["end"]:
                return data
        return self._segments[-1] if self._segments else None

    def _medium_at(self, moment):
        for entry in self._media:
            if entry["start"] <= moment < entry["end"]:
                return entry["path"]
        return None

    def _show_medium(self, path):
        self.selected_path = path
        self.reveal_button.setEnabled(path is not None)
        self.play_button.setVisible(path is not None and media_kinds.media_kind(path) == "video")

        if path is None:
            self.file_label.setText("")
            self.preview_label.setPixmap(QPixmap())
            self.preview_label.setText("Nothing was on screen yet at this point.")
            return

        self.file_label.setText(Path(path).name)
        if media_kinds.media_kind(path) == "video":
            self._show_video_frame(path)
            return
        self._paint(load_scaled_pixmap(path, self.preview_label.size()), path)

    def _show_video_frame(self, path):
        if path in self._video_frames:
            self._paint(self._video_frames[path], path)
            return
        self.preview_label.setPixmap(QPixmap())
        self.preview_label.setText(f"{Path(path).name}\n\nGrabbing a frame...")
        self._thumbnail_source.request(path, self._video_frame_ready)

    def _video_frame_ready(self, path, pixmap):
        self._video_frames[path] = pixmap
        if self.selected_path == path:  # the scrub may have moved on while it decoded
            self._paint(pixmap, path)

    def _paint(self, pixmap, path):
        if pixmap is None or pixmap.isNull():
            self.preview_label.setPixmap(QPixmap())
            self.preview_label.setText(f"{Path(path).name}\n\nNo preview available for this file.")
            return
        self.preview_label.setPixmap(
            pixmap.scaled(
                self.preview_label.size(),
                Qt.AspectRatioMode.KeepAspectRatio,
                Qt.TransformationMode.SmoothTransformation,
            )
        )

    # --- acting on what you found ---

    def _reveal_selected(self):
        if not self.selected_path:
            return
        # The containing folder rather than the file: opening the file would launch it in
        # whatever is registered for the type, which is what "Open in player" is for.
        QDesktopServices.openUrl(QUrl.fromLocalFile(str(Path(self.selected_path).parent)))

    def _play_selected(self):
        if self.selected_path:
            QDesktopServices.openUrl(QUrl.fromLocalFile(self.selected_path))

    def closeEvent(self, event):
        # Anything still decoding is for a dialog that is going away.
        self._thumbnail_source.cancel_all()
        super().closeEvent(event)
