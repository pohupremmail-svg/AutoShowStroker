from PyQt6.QtGui import QGuiApplication
from PyQt6.QtWidgets import QLabel

from src.SplashScreen import SplashScreen


def make_splash(qtbot):
    splash = SplashScreen(fade_in_ms=1, hold_ms=1, fade_out_ms=1)
    qtbot.addWidget(splash)
    return splash


def test_starts_fully_transparent(qtbot):
    splash = make_splash(qtbot)
    assert splash.windowOpacity() == 0


def test_logo_pixmap_loads(qtbot):
    splash = make_splash(qtbot)
    logo_labels = [
        w for w in splash.findChildren(QLabel)
        if w.pixmap() is not None and not w.pixmap().isNull()
    ]
    assert len(logo_labels) == 1


def test_centers_itself_on_the_screen(qtbot):
    splash = make_splash(qtbot)
    screen_center = QGuiApplication.primaryScreen().availableGeometry().center()
    splash_center = splash.frameGeometry().center()

    assert abs(splash_center.x() - screen_center.x()) <= 1
    assert abs(splash_center.y() - screen_center.y()) <= 1


def test_fade_sequence_finishes_and_closes(qtbot):
    splash = make_splash(qtbot)
    with qtbot.waitSignal(splash.finished, timeout=2000):
        splash.show()
    assert splash.isVisible() is False
