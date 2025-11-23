import sys

from PyQt6.QtWidgets import QApplication

from src.GoonerApp import GoonerApp

if __name__ == "__main__":
    app = QApplication(sys.argv)
    window = GoonerApp()
    window.showMaximized()
    sys.exit(app.exec())