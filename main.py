import sys

from PyQt6.QtWidgets import QApplication

from src import theme
from src.GoonerApp import GoonerApp
from src.SplashScreen import SplashScreen

if __name__ == "__main__":
    app = QApplication(sys.argv)
    app.setStyle("Fusion")
    app.setPalette(theme.build_palette())
    app.setStyleSheet(theme.GLOBAL_QSS)
    window = GoonerApp()

    def _reveal_main_window():
        window.showMaximized()
        window.maybe_show_whats_new_on_startup()

    if window.show_startup_splash:
        splash = SplashScreen()
        splash.finished.connect(_reveal_main_window)
        splash.show()
    else:
        _reveal_main_window()

    sys.exit(app.exec())
