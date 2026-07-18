from PyQt6.QtWidgets import QDialog, QLabel, QPushButton, QScrollArea, QVBoxLayout, QWidget

from src import theme


class WhatsNewDialog(QDialog):
    def __init__(self, entries: dict, parent=None):
        super().__init__(parent)
        self.setWindowTitle("What's New")
        self.setModal(True)
        self.resize(480, 420)

        layout = QVBoxLayout(self)

        title = QLabel("What's new")
        title.setStyleSheet(f"font-size: 22px; font-weight: bold; color: {theme.ACCENT}; margin-bottom: 8px;")
        layout.addWidget(title)

        content = QWidget()
        content_layout = QVBoxLayout(content)

        for version, text in entries.items():
            version_label = QLabel(f"Version {version}")
            version_label.setStyleSheet(
                f"font-size: 15px; font-weight: bold; color: {theme.TEXT}; margin-top: 10px;"
            )
            content_layout.addWidget(version_label)

            text_label = QLabel(text)
            text_label.setWordWrap(True)
            content_layout.addWidget(text_label)

        content_layout.addStretch()

        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setWidget(content)
        layout.addWidget(scroll)

        self.button = QPushButton("Got it")
        self.button.setObjectName("primary")
        self.button.clicked.connect(self.accept)
        layout.addWidget(self.button)
