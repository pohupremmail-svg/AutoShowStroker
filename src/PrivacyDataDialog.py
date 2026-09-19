from PyQt6.QtCore import Qt, QUrl
from PyQt6.QtGui import QDesktopServices
from PyQt6.QtWidgets import (
    QCheckBox,
    QComboBox,
    QDialog,
    QHBoxLayout,
    QLabel,
    QMessageBox,
    QPushButton,
    QVBoxLayout,
)

from src import applog, session_files, theme

log = applog.get_logger(__name__)


class PrivacyDataDialog(QDialog):
    """Says where the app keeps things, and lets the user delete any of it.

    The Guide's Privacy tab promises nothing leaves the machine. That promise is only
    half of it: a user who wants their traces gone also has to be able to find them and
    remove them. A portable .exe has no uninstaller, so without this dialog the only route
    was regedit plus Explorer, against locations the app never named.

    Deletion is per category rather than one big button - a user clearing the folder paths
    before handing the laptop over should not have to lose their session history too.
    """

    # key -> (label, description). The key doubles as the data-store file name for the
    # JSON categories; "diagnostic_log" and "settings" are handled specially.
    CATEGORIES = (
        ("session_history", "Session history", "every recorded session and your personal records"),
        ("saved_sessions", "Saved sessions",
         "the sessions you saved to replay - the one thing here that stores the paths of "
         "your media files"),
        ("achievements", "Achievements",
         "which achievements you have unlocked, and when"),
        ("custom_patterns", "Custom rhythm patterns", "the patterns you built in the pattern editor"),
        ("custom_phrase_files", "Custom phrase files", "the callout files you added"),
        ("last_selected_folders", "Last used media folders", "the folder paths the picker remembers"),
        ("diagnostic_log", "Diagnostic log", "the opt-in log file, if you turned it on"),
        ("settings", "All settings", "every slider, toggle and the selected language"),
    )

    # Plain-language names for applog.LEVELS - the stored value stays the level name.
    LEVEL_LABELS = {
        "INFO": "Everything (recommended)",
        "WARNING": "Problems only",
        "ERROR": "Errors only",
    }

    def __init__(self, main_app, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Privacy & Data")
        self.setModal(True)
        self.resize(620, 520)

        self.main_app = main_app
        self.checkboxes = {}

        layout = QVBoxLayout(self)

        title = QLabel("Your Data")
        title.setStyleSheet(f"font-size: 20px; font-weight: bold; color: {theme.ACCENT};")
        layout.addWidget(title)

        intro = QLabel(
            "GoonerApp never sends any of this anywhere. It does have to keep some of it on "
            "disk, though - here is exactly what, and where."
        )
        intro.setWordWrap(True)
        intro.setStyleSheet(f"color: {theme.TEXT};")
        layout.addWidget(intro)

        self.locations_label = QLabel(self.locations_text())
        self.locations_label.setWordWrap(True)
        # Selectable so the user can copy a path out rather than retyping it.
        self.locations_label.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
        self.locations_label.setStyleSheet(
            f"color: {theme.TEXT}; background-color: {theme.SURFACE_DARK}; "
            "border-radius: 6px; padding: 8px; font-family: monospace;"
        )
        layout.addWidget(self.locations_label)

        self.btn_open_folder = QPushButton("Open data folder")
        self.btn_open_folder.clicked.connect(self._on_open_folder)
        layout.addWidget(self.btn_open_folder)

        layout.addLayout(self._build_diagnostic_log_section())

        delete_header = QLabel("Delete my data")
        delete_header.setStyleSheet(
            f"font-size: 14px; font-weight: bold; color: {theme.ACCENT}; margin-top: 12px;"
        )
        layout.addWidget(delete_header)

        for key, _label, description in self.CATEGORIES:
            # Text is filled in by refresh_counts() below, which appends the live count.
            checkbox = QCheckBox()
            checkbox.setToolTip(description)
            checkbox.toggled.connect(self._update_delete_enabled)
            self.checkboxes[key] = checkbox
            layout.addWidget(checkbox)

        self.btn_delete = QPushButton("Delete selected...")
        self.btn_delete.setEnabled(False)
        self.btn_delete.clicked.connect(self._on_delete_clicked)
        layout.addWidget(self.btn_delete)

        layout.addStretch()

        button_row = QHBoxLayout()
        button_row.addStretch()
        self.btn_close = QPushButton("Close")
        self.btn_close.setObjectName("primary")
        self.btn_close.clicked.connect(self.accept)
        button_row.addWidget(self.btn_close)
        layout.addLayout(button_row)

        self.refresh_counts()

    # --- the diagnostic log ---

    def _build_diagnostic_log_section(self):
        """The log's on/off switch and level live here rather than in Settings.

        It is not a playback preference - it is the one thing in the app that *writes extra
        data about the user*, so it belongs next to where that data is disclosed and
        deleted. Both controls apply immediately: this dialog has no Save button, and Open
        folder and Delete already act on click.
        """
        section = QVBoxLayout()

        header = QLabel("Diagnostic log")
        header.setStyleSheet(
            f"font-size: 14px; font-weight: bold; color: {theme.ACCENT}; margin-top: 12px;"
        )
        section.addWidget(header)

        explanation = QLabel(
            "Off unless you switch it on. Records what the app is doing so a problem can be "
            "traced - which also means it records when you used it. It never writes down the "
            "folders you play from; a file that fails to load is named, nothing else is."
        )
        explanation.setWordWrap(True)
        explanation.setStyleSheet(f"color: {theme.TEXT};")
        section.addWidget(explanation)

        self.diagnostic_log_checkbox = QCheckBox("Write a diagnostic log file")
        self.diagnostic_log_checkbox.setChecked(self.main_app.diagnostic_log)
        self.diagnostic_log_checkbox.toggled.connect(self._on_diagnostic_log_changed)
        section.addWidget(self.diagnostic_log_checkbox)

        level_row = QHBoxLayout()
        level_row.addWidget(QLabel("Record:"))
        self.diagnostic_log_level = QComboBox()
        for level in applog.LEVELS:
            self.diagnostic_log_level.addItem(self.LEVEL_LABELS[level], level)
        saved = self.diagnostic_log_level.findData(self.main_app.diagnostic_log_level)
        if saved != -1:
            self.diagnostic_log_level.setCurrentIndex(saved)
        self.diagnostic_log_level.currentIndexChanged.connect(self._on_diagnostic_log_changed)
        level_row.addWidget(self.diagnostic_log_level)
        level_row.addStretch()
        section.addLayout(level_row)

        self._update_level_enabled()
        return section

    def _update_level_enabled(self):
        self.diagnostic_log_level.setEnabled(self.diagnostic_log_checkbox.isChecked())

    def _on_diagnostic_log_changed(self):
        self._update_level_enabled()
        self.main_app.set_diagnostic_log(
            self.diagnostic_log_checkbox.isChecked(),
            level=self.diagnostic_log_level.currentData(),
        )
        self.refresh_counts()

    # --- disclosure ---

    def locations_text(self) -> str:
        return (
            f"Data files:\n{self.main_app.data_store.base_dir}\n\n"
            f"Settings:\n{self.main_app.settings.fileName()}"
        )

    def _open_url(self, url):
        QDesktopServices.openUrl(url)

    def _on_open_folder(self):
        base_dir = self.main_app.data_store.base_dir
        # Created on demand: nothing has been saved yet on a fresh install, and opening
        # Explorer on a path that doesn't exist just fails silently.
        try:
            base_dir.mkdir(parents=True, exist_ok=True)
        except OSError as error:
            log.error("Could not create the data folder: %s", error)
            return
        self._open_url(QUrl.fromLocalFile(str(base_dir)))

    # --- counts ---

    def category_counts(self) -> dict:
        store = self.main_app.data_store
        return {
            "session_history": len(self.main_app.score_tracker.get_history()),
            "saved_sessions": len(session_files.load_saved_sessions(store)),
            "achievements": len(self.main_app.achievement_tracker.unlocked),
            "custom_patterns": len(self.main_app.beat_handler.custom_beat_patterns),
            "custom_phrase_files": len(self.main_app.callout_handler.custom_phrase_files),
            "last_selected_folders": len(store.load("last_selected_folders", [])),
            # Files, not lines: rotated backups count too, and reading them to count lines
            # just to label a checkbox would be silly.
            "diagnostic_log": len(applog.log_file_paths(store.base_dir)),
            "settings": len(self.main_app.settings.allKeys()),
        }

    def refresh_counts(self):
        counts = self.category_counts()
        for key, label, _description in self.CATEGORIES:
            count = counts[key]
            suffix = "nothing stored" if count == 0 else f"{count} stored"
            self.checkboxes[key].setText(f"{label} ({suffix})")

    # --- deleting ---

    def _update_delete_enabled(self):
        self.btn_delete.setEnabled(any(box.isChecked() for box in self.checkboxes.values()))

    def _selected_keys(self) -> list:
        return [key for key, box in self.checkboxes.items() if box.isChecked()]

    def _label_for(self, key: str) -> str:
        return next(label for entry_key, label, _ in self.CATEGORIES if entry_key == key)

    def _confirm_deletion(self, keys) -> bool:
        listed = "\n".join(f"  - {self._label_for(key)}" for key in keys)
        box = QMessageBox(self)
        box.setIcon(QMessageBox.Icon.Warning)
        box.setWindowTitle("Delete this data?")
        box.setText(f"This permanently deletes:\n\n{listed}\n\nThis cannot be undone.")
        box.setStandardButtons(QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No)
        box.setDefaultButton(QMessageBox.StandardButton.No)
        return box.exec() == QMessageBox.StandardButton.Yes

    def clear_categories(self, keys):
        """Deletes the named categories, resetting the live objects as well as the files.

        The in-memory reset is the part that matters: dropping session_history.json while
        ScoreTracker still holds the list would just write it back at the next session end.
        """
        log.info("Clearing user data: %s", ", ".join(keys))
        for key in keys:
            if key == "session_history":
                self.main_app.score_tracker.clear_history()
            elif key == "saved_sessions":
                self.main_app.data_store.delete(session_files.SAVED_SESSIONS_KEY)
            elif key == "achievements":
                # Cleared in memory too, or the next session end would write them back.
                self.main_app.achievement_tracker.clear()
            elif key == "custom_patterns":
                self.main_app.beat_handler.clear_custom_patterns()
            elif key == "custom_phrase_files":
                self.main_app.callout_handler.clear_custom_phrase_files()
            elif key == "last_selected_folders":
                self.main_app.data_store.delete("last_selected_folders")
            elif key == "diagnostic_log":
                applog.delete_log_files(self.main_app.data_store.base_dir)
            elif key == "settings":
                self.main_app.settings.clear()
                self.main_app.settings.sync()

    def _on_delete_clicked(self):
        keys = self._selected_keys()
        if not keys or not self._confirm_deletion(keys):
            return
        self.clear_categories(keys)
        for box in self.checkboxes.values():
            box.setChecked(False)
        self.refresh_counts()
