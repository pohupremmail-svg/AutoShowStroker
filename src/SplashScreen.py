from PyQt6.QtCore import QEasingCurve, QPropertyAnimation, Qt, QTimer, pyqtSignal
from PyQt6.QtGui import QColor, QPixmap
from PyQt6.QtWidgets import QGraphicsDropShadowEffect, QLabel, QVBoxLayout, QWidget

from src import theme
from src.utils import get_project_root

LOGO_SIZE = 320


class SplashScreen(QWidget):
    """Non-modal startup flourish: fades the app logo + name in, holds, fades out.

    Uses QWidget (not QDialog) and event-driven QPropertyAnimation/QTimer so it never
    needs a real modal event loop - tests can drive it with tiny durations and
    qtbot.waitSignal(splash.finished).
    """

    finished = pyqtSignal()

    def __init__(self, fade_in_ms=400, hold_ms=1000, fade_out_ms=400, parent=None):
        super().__init__(parent)
        self._hold_ms = hold_ms

        self.setWindowFlags(Qt.WindowType.FramelessWindowHint | Qt.WindowType.WindowStaysOnTopHint)
        self.setAutoFillBackground(True)
        self.setStyleSheet(f"background-color: {theme.BACKGROUND};")
        self.setFixedSize(LOGO_SIZE + 80, LOGO_SIZE + 140)
        self.setWindowOpacity(0.0)

        layout = QVBoxLayout(self)
        layout.setAlignment(Qt.AlignmentFlag.AlignCenter)

        logo_label = QLabel()
        logo_path = get_project_root() / "res" / "icons" / "qUloN.png"
        pixmap = QPixmap(str(logo_path))
        if not pixmap.isNull():
            pixmap = pixmap.scaled(
                LOGO_SIZE, LOGO_SIZE,
                Qt.AspectRatioMode.KeepAspectRatio,
                Qt.TransformationMode.SmoothTransformation,
            )
        logo_label.setPixmap(pixmap)
        logo_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(logo_label)

        name_label = QLabel("GoonerApp")
        name_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        name_label.setStyleSheet(f"color: {theme.TEXT}; font-size: 28px; font-weight: bold;")
        name_glow = QGraphicsDropShadowEffect()
        name_glow.setColor(QColor(theme.ACCENT))
        name_glow.setBlurRadius(40)
        name_glow.setOffset(0, 0)
        name_label.setGraphicsEffect(name_glow)
        layout.addWidget(name_label)

        if parent is not None:
            parent_geo = parent.frameGeometry()
            self.move(
                parent_geo.center().x() - self.width() // 2,
                parent_geo.center().y() - self.height() // 2,
            )

        self._fade_in = QPropertyAnimation(self, b"windowOpacity")
        self._fade_in.setDuration(fade_in_ms)
        self._fade_in.setStartValue(0.0)
        self._fade_in.setEndValue(1.0)
        self._fade_in.setEasingCurve(QEasingCurve.Type.InOutQuad)
        self._fade_in.finished.connect(self._on_fade_in_finished)

        self._fade_out = QPropertyAnimation(self, b"windowOpacity")
        self._fade_out.setDuration(fade_out_ms)
        self._fade_out.setStartValue(1.0)
        self._fade_out.setEndValue(0.0)
        self._fade_out.setEasingCurve(QEasingCurve.Type.InOutQuad)
        self._fade_out.finished.connect(self._on_fade_out_finished)

    def showEvent(self, event):
        super().showEvent(event)
        self._fade_in.start()

    def _on_fade_in_finished(self):
        QTimer.singleShot(self._hold_ms, self._start_fade_out)

    def _start_fade_out(self):
        self._fade_out.start()

    def _on_fade_out_finished(self):
        self.close()
        self.finished.emit()
