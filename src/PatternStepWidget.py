from PyQt6.QtCore import QRectF, Qt, pyqtSignal
from PyQt6.QtGui import QColor, QMouseEvent, QPainter, QPen
from PyQt6.QtWidgets import QPushButton, QVBoxLayout, QWidget

from src import theme

MIN_WEIGHT = 1
MAX_WEIGHT = 4
_WIDTH = 64
_BAR_HEIGHT = 100


class _DurationBar(QWidget):
    """Draggable bar controlling a step's duration weight (1=longest, MAX_WEIGHT=shortest).

    Purely a length control - it never touches the audible/muted state, so a paused
    step's length can be adjusted without accidentally un-pausing it.
    """

    changed = pyqtSignal()

    def __init__(self, weight=MIN_WEIGHT, muted=False, parent=None):
        super().__init__(parent)
        self._weight = weight
        self._muted = muted
        self._highlighted = False
        self.setFixedSize(_WIDTH, _BAR_HEIGHT)
        self.setCursor(Qt.CursorShape.PointingHandCursor)
        self._update_tooltip()

    def weight(self) -> int:
        return self._weight

    def set_weight(self, weight: int, emit: bool = True) -> None:
        self._weight = max(MIN_WEIGHT, min(MAX_WEIGHT, weight))
        self._update_tooltip()
        self.update()
        if emit:
            self.changed.emit()

    def set_muted(self, muted: bool) -> None:
        self._muted = muted
        self.update()

    def set_highlighted(self, highlighted: bool) -> None:
        self._highlighted = highlighted
        self.update()

    def _update_tooltip(self) -> None:
        self.setToolTip(f"Duration weight {self._weight} (1 = longest step, {MAX_WEIGHT} = shortest)")

    def _set_weight_from_mouse(self, event: QMouseEvent) -> None:
        self.set_weight(self._weight_from_y(event.position().y()))

    def mousePressEvent(self, event: QMouseEvent) -> None:
        self._set_weight_from_mouse(event)

    def mouseMoveEvent(self, event: QMouseEvent) -> None:
        if event.buttons() & Qt.MouseButton.LeftButton:
            self._set_weight_from_mouse(event)

    def _weight_from_y(self, y: float) -> int:
        # y=0 (top) -> MIN_WEIGHT (longest step, tallest bar), y=height (bottom) -> MAX_WEIGHT
        ratio = max(0.0, min(1.0, y / self.height()))
        level = MIN_WEIGHT + round(ratio * (MAX_WEIGHT - MIN_WEIGHT))
        return max(MIN_WEIGHT, min(MAX_WEIGHT, level))

    def paintEvent(self, event) -> None:
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)

        track_rect = QRectF(2, 2, self.width() - 4, self.height() - 4)
        painter.setPen(Qt.PenStyle.NoPen)
        painter.setBrush(QColor(theme.SURFACE_DARK))
        painter.drawRoundedRect(track_rect, 6, 6)

        bar_color = QColor(theme.PAUSE) if self._muted else QColor(theme.ACCENT)
        # Inverse of weight: weight 1 (longest step) draws tallest, weight MAX_WEIGHT (shortest) draws shortest.
        bar_ratio = (MAX_WEIGHT - self._weight) / (MAX_WEIGHT - MIN_WEIGHT) if MAX_WEIGHT > MIN_WEIGHT else 1.0
        min_bar_height = track_rect.height() * 0.15
        bar_height = min_bar_height + bar_ratio * (track_rect.height() - min_bar_height)
        bar_rect = QRectF(
            track_rect.left(),
            track_rect.bottom() - bar_height,
            track_rect.width(),
            bar_height,
        )
        painter.setBrush(bar_color)
        painter.drawRoundedRect(bar_rect, 6, 6)

        if self._highlighted:
            painter.setPen(QPen(QColor(theme.TEXT), 2))
            painter.setBrush(Qt.BrushStyle.NoBrush)
            painter.drawRoundedRect(track_rect, 6, 6)

        painter.end()


class PatternStepWidget(QWidget):
    """One step of a beat pattern: a duration bar plus a Beat/Pause toggle button.

    Encodes the same signed int BeatHandler patterns use: sign = audible/muted,
    magnitude (1-4) = relative duration of the step. BeatHandler divides the base
    step time by this magnitude, so it's an *inverse*-duration multiplier: weight 1
    is the longest step, weight 4 the shortest - not a loudness/intensity value.

    Length (the bar) and mute state (the button) are deliberately separate controls,
    so a pause can have its own length without the act of adjusting it un-pausing it.
    """

    changed = pyqtSignal()

    def __init__(self, value=1, parent=None):
        super().__init__(parent)
        audible = value > 0
        weight = max(MIN_WEIGHT, min(MAX_WEIGHT, abs(value))) if value != 0 else MIN_WEIGHT

        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(4)

        self._bar = _DurationBar(weight, muted=not audible)
        self._bar.changed.connect(self.changed)
        layout.addWidget(self._bar)

        self._pause_button = QPushButton()
        self._pause_button.setCheckable(True)
        self._pause_button.setStyleSheet(
            f"QPushButton:checked {{ background-color: {theme.PAUSE}; color: {theme.TEXT}; font-weight: bold; }}"
        )
        self._pause_button.toggled.connect(self._on_pause_toggled)
        self._pause_button.setChecked(not audible)
        self._set_pause_button_text(not audible)
        layout.addWidget(self._pause_button)

    def _on_pause_toggled(self, checked: bool) -> None:
        self._bar.set_muted(checked)
        self._set_pause_button_text(checked)
        self.changed.emit()

    def _set_pause_button_text(self, muted: bool) -> None:
        self._pause_button.setText("Pause" if muted else "Beat")

    def get_value(self) -> int:
        weight = self._bar.weight()
        return -weight if self._pause_button.isChecked() else weight

    def set_value(self, value: int, emit: bool = True) -> None:
        audible = value > 0
        weight = max(MIN_WEIGHT, min(MAX_WEIGHT, abs(value))) if value != 0 else MIN_WEIGHT
        self._bar.set_weight(weight, emit=False)
        self._pause_button.blockSignals(True)
        self._pause_button.setChecked(not audible)
        self._pause_button.blockSignals(False)
        self._bar.set_muted(not audible)
        self._set_pause_button_text(not audible)
        if emit:
            self.changed.emit()

    def set_highlighted(self, highlighted: bool) -> None:
        self._bar.set_highlighted(highlighted)
