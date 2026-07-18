from PyQt6.QtCore import QRectF, Qt, pyqtSignal
from PyQt6.QtGui import QColor, QMouseEvent, QPainter
from PyQt6.QtWidgets import QWidget

from src import theme

MIN_WEIGHT = 1
MAX_WEIGHT = 4
_CLICK_DRAG_THRESHOLD_PX = 4
_WIDTH = 40
_HEIGHT = 120


class PatternStepWidget(QWidget):
    """One step of a beat pattern, drawn as a draggable step-sequencer velocity bar.

    Encodes the same signed int BeatHandler patterns use: sign = audible/muted,
    magnitude (1-4) = relative weight/duration of the step. A plain click toggles
    audible/muted; dragging vertically sets the weight (and activates a muted step).
    """

    changed = pyqtSignal()

    def __init__(self, value=1, parent=None):
        super().__init__(parent)
        self._audible = True
        self._weight = MIN_WEIGHT
        self.set_value(value, emit=False)
        self._press_pos = None
        self._dragged = False
        self.setFixedSize(_WIDTH, _HEIGHT)
        self.setCursor(Qt.CursorShape.PointingHandCursor)

    def get_value(self) -> int:
        return self._weight if self._audible else -self._weight

    def set_value(self, value: int, emit: bool = True) -> None:
        self._audible = value > 0
        self._weight = max(MIN_WEIGHT, min(MAX_WEIGHT, abs(value))) if value != 0 else MIN_WEIGHT
        self.update()
        if emit:
            self.changed.emit()

    def mousePressEvent(self, event: QMouseEvent) -> None:
        self._press_pos = event.position()
        self._dragged = False

    def mouseMoveEvent(self, event: QMouseEvent) -> None:
        if self._press_pos is None:
            return
        delta = (event.position() - self._press_pos).manhattanLength()
        if delta < _CLICK_DRAG_THRESHOLD_PX:
            return
        self._dragged = True
        self._audible = True
        self._weight = self._weight_from_y(event.position().y())
        self.update()
        self.changed.emit()

    def mouseReleaseEvent(self, event: QMouseEvent) -> None:
        if not self._dragged:
            self._audible = not self._audible
            self.update()
            self.changed.emit()
        self._press_pos = None
        self._dragged = False

    def _weight_from_y(self, y: float) -> int:
        # y=0 (top) -> MAX_WEIGHT, y=height (bottom) -> MIN_WEIGHT
        ratio = 1.0 - max(0.0, min(1.0, y / self.height()))
        level = MIN_WEIGHT + round(ratio * (MAX_WEIGHT - MIN_WEIGHT))
        return max(MIN_WEIGHT, min(MAX_WEIGHT, level))

    def paintEvent(self, event) -> None:
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)

        track_rect = QRectF(2, 2, self.width() - 4, self.height() - 4)
        painter.setPen(Qt.PenStyle.NoPen)
        painter.setBrush(QColor(theme.SURFACE_DARK))
        painter.drawRoundedRect(track_rect, 6, 6)

        bar_color = QColor(theme.ACCENT) if self._audible else QColor(theme.SECONDARY)
        bar_ratio = (self._weight - MIN_WEIGHT) / (MAX_WEIGHT - MIN_WEIGHT) if MAX_WEIGHT > MIN_WEIGHT else 1.0
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
        painter.end()
