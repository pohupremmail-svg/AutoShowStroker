import sys

from PyQt6.QtWidgets import QApplication

from src import theme
from src.GoonerApp import GoonerApp

if __name__ == "__main__":
    app = QApplication(sys.argv)
    app.setStyle("Fusion")
    app.setPalette(theme.build_palette())
    app.setStyleSheet(theme.GLOBAL_QSS)
    window = GoonerApp()
    window.showMaximized()
    window.maybe_show_whats_new_on_startup()
    sys.exit(app.exec())
