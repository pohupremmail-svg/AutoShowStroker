from PyQt6.QtCore import Qt
from PyQt6.QtWidgets import (
    QDialog,
    QFrame,
    QHBoxLayout,
    QLabel,
    QPushButton,
    QScrollArea,
    QVBoxLayout,
    QWidget,
)

from src import theme


class WhatsNewDialog(QDialog):
    def __init__(self, entries: dict, parent=None):
        super().__init__(parent)
        self.setWindowTitle("What's New")
        self.setModal(True)
        self.resize(520, 480)

        layout = QVBoxLayout(self)

        title = QLabel("What's new")
        title.setStyleSheet(f"font-size: 22px; font-weight: bold; color: {theme.ACCENT}; margin-bottom: 8px;")
        layout.addWidget(title)

        content = QWidget()
        content_layout = QVBoxLayout(content)
        content_layout.setSpacing(14)

        for version, text in reversed(entries.items()):
            card = QFrame()
            card.setFrameShape(QFrame.Shape.NoFrame)
            card.setStyleSheet(
                f"QFrame {{"
                f"  background-color: {theme.SURFACE};"
                f"  border: 1px solid {theme.SECONDARY};"
                f"  border-left: 4px solid {theme.ACCENT};"
                f"  border-radius: 8px;"
                f"}}"
            )
            card_layout = QVBoxLayout(card)
            card_layout.setContentsMargins(14, 12, 14, 14)
            card_layout.setSpacing(6)

            badge_row = QHBoxLayout()
            badge_row.setContentsMargins(0, 0, 0, 0)
            version_label = QLabel(f"Version {version}")
            version_label.setStyleSheet(
                f"font-size: 12px; font-weight: bold; color: {theme.BACKGROUND};"
                f"background-color: {theme.ACCENT}; border-radius: 8px; padding: 3px 10px;"
            )
            badge_row.addWidget(version_label)
            badge_row.addStretch()
            card_layout.addLayout(badge_row)

            text_label = QLabel(text)
            text_label.setTextFormat(Qt.TextFormat.RichText)
            text_label.setWordWrap(True)
            text_label.setStyleSheet(f"color: {theme.TEXT}; font-size: 13px; background-color: transparent;")
            card_layout.addWidget(text_label)

            content_layout.addWidget(card)

        content_layout.addStretch()

        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setWidget(content)
        layout.addWidget(scroll)

        self.button = QPushButton("Got it")
        self.button.setObjectName("primary")
        self.button.clicked.connect(self.accept)
        layout.addWidget(self.button)
