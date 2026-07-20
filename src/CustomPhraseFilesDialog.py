from PyQt6.QtWidgets import (
    QComboBox,
    QDialog,
    QFileDialog,
    QHBoxLayout,
    QLabel,
    QListWidget,
    QPushButton,
    QVBoxLayout,
)

from src import theme


class CustomPhraseFilesDialog(QDialog):
    """Manages user-chosen external callout phrase files (same {trigger_key: [phrases]}
    schema as res/callouts/<lang>.json), merged onto the built-in phrases for an explicitly
    picked language. The dialog only calls CalloutHandler mutators - CalloutHandler itself
    self-persists to QSettings on every add/remove, same split as PatternEditorDialog."""

    def __init__(self, callout_handler, parent=None):
        super().__init__(parent)
        self.callout_handler = callout_handler
        self.setWindowTitle("Manage Custom Phrase Files")
        self.setModal(True)
        self.resize(560, 360)

        layout = QVBoxLayout(self)

        add_row = QHBoxLayout()
        self.lang_combo = QComboBox()
        self.lang_combo.addItems(self.callout_handler.available_languages)
        self.add_file_button = QPushButton("Add File...")
        self.add_file_button.clicked.connect(self._on_add_file)
        add_row.addWidget(self.lang_combo)
        add_row.addWidget(self.add_file_button)
        layout.addLayout(add_row)

        self.error_label = QLabel("")
        self.error_label.setStyleSheet(f"color: {theme.DENIED};")
        layout.addWidget(self.error_label)

        self.file_list = QListWidget()
        layout.addWidget(self.file_list)

        self.remove_button = QPushButton("Remove Selected")
        self.remove_button.clicked.connect(self._on_remove_selected)
        layout.addWidget(self.remove_button)

        self.close_button = QPushButton("Close")
        self.close_button.setObjectName("primary")
        self.close_button.clicked.connect(self.accept)
        layout.addWidget(self.close_button)

        self._refresh_file_list()

    def _refresh_file_list(self):
        self.file_list.clear()
        for entry in self.callout_handler.custom_phrase_files:
            self.file_list.addItem(f"{entry['lang']}: {entry['path']}")

    def _on_add_file(self):
        self.error_label.setText("")
        path, _filter = QFileDialog.getOpenFileName(self, "Select a Custom Callout File", "", "JSON Files (*.json)")
        if not path:
            return
        try:
            self.callout_handler.load_custom_file(path, self.lang_combo.currentText())
        except ValueError as exc:
            self.error_label.setText(str(exc))
            return
        self._refresh_file_list()

    def _on_remove_selected(self):
        self.error_label.setText("")
        item = self.file_list.currentItem()
        if item is None:
            return
        row = self.file_list.currentRow()
        path = self.callout_handler.custom_phrase_files[row]["path"]
        self.callout_handler.unload_custom_file(path)
        self._refresh_file_list()
