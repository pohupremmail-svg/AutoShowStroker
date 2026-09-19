from pathlib import Path

from PyQt6.QtCore import Qt
from PyQt6.QtWidgets import (
    QDialog,
    QFileDialog,
    QHBoxLayout,
    QLabel,
    QListWidget,
    QListWidgetItem,
    QMessageBox,
    QPushButton,
    QVBoxLayout,
)

from src import applog, session_files, theme

log = applog.get_logger(__name__)

FILE_FILTER = "GoonerApp session (*.json)"


class SavedSessionsDialog(QDialog):
    """The shelf of saved sessions: play one again, hand one on, throw one away.

    Replaying is the point of the whole thing - the same segments, the same climax, the
    same pacing, against the same files or against your own. The manager itself only
    decides *which* of those two, and the rule is simple: if the recorded files are still
    where they were, use them; if they are not, offer the user's own library rather than
    playing a session of empty frames.

    Takes the main app for its data store and its replay entry point, and reaches for
    nothing else on it - which is what lets it be tested without a window.
    """

    # The whole reason exporting asks every time. Deliberately concrete: "includes paths"
    # means nothing to someone who has not thought about what a path contains.
    PATHS_WARNING = (
        "A session exported with paths tells whoever opens it your Windows user name, how "
        "your collection is laid out in folders, and the file name of everything it showed.\n\n"
        "Without them it still replays every beat, the climax and the pacing - against "
        "whatever collection the other person has."
    )

    def __init__(self, main_app, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Saved Sessions")
        self.setModal(True)
        self.resize(640, 460)

        self.main_app = main_app
        self._sessions = []

        layout = QVBoxLayout(self)

        title = QLabel("Saved Sessions")
        title.setStyleSheet(f"font-size: 20px; font-weight: bold; color: {theme.ACCENT};")
        layout.addWidget(title)

        self.intro_label = QLabel(
            "A saved session plays again exactly as it went: the same rhythms, the same "
            "pauses, the same climax, the same pacing. The callouts stay random, so two "
            "runs of the same session are still comparable.<br><br>"
            "One marked <b>Stopped early</b> was saved before it ever climaxed - usually "
            "because you did not last that long. It replays as recorded and then carries "
            "on as an ordinary session, so this time you can finish it."
        )
        self.intro_label.setWordWrap(True)
        self.intro_label.setStyleSheet(f"color: {theme.TEXT};")
        layout.addWidget(self.intro_label)

        self.session_list = QListWidget()
        self.session_list.currentRowChanged.connect(self._update_buttons)
        self.session_list.itemDoubleClicked.connect(self._on_replay_clicked)
        layout.addWidget(self.session_list, 1)

        self.empty_label = QLabel(
            "Nothing saved yet. Save a session from the Session Explorer at the end of one."
        )
        self.empty_label.setWordWrap(True)
        self.empty_label.setStyleSheet(f"color: {theme.TEXT};")
        layout.addWidget(self.empty_label)

        layout.addLayout(self._build_buttons())

        self.refresh()

    def _build_buttons(self):
        row = QHBoxLayout()

        self.replay_button = QPushButton("Replay")
        self.replay_button.setObjectName("primary")
        self.replay_button.clicked.connect(self._on_replay_clicked)

        self.export_button = QPushButton("Export...")
        self.export_button.setToolTip("Write this session to a file you can pass on")
        self.export_button.clicked.connect(self._on_export_clicked)

        self.import_button = QPushButton("Import...")
        self.import_button.setToolTip("Put a session file somebody sent you on the shelf")
        self.import_button.clicked.connect(self._on_import_clicked)

        self.delete_button = QPushButton("Delete")
        self.delete_button.clicked.connect(self._on_delete_clicked)

        self.close_button = QPushButton("Close")
        self.close_button.clicked.connect(self.accept)

        row.addWidget(self.replay_button)
        row.addWidget(self.export_button)
        row.addWidget(self.import_button)
        row.addWidget(self.delete_button)
        row.addStretch()
        row.addWidget(self.close_button)
        return row

    # --- the list ---

    def refresh(self):
        """Rebuilds the list from what the store actually holds.

        Read every time rather than cached: Privacy & Data can delete the lot while this
        dialog is open, and a list still offering to replay them would be a lie.
        """
        self._sessions = session_files.load_saved_sessions(self.main_app.data_store)
        self.session_list.clear()
        # Newest first: the one you want is almost always the one you just played.
        for index in reversed(range(len(self._sessions))):
            item = QListWidgetItem(session_files.describe(self._sessions[index]))
            item.setData(Qt.ItemDataRole.UserRole, index)
            self.session_list.addItem(item)
        self.empty_label.setVisible(not self._sessions)
        self._update_buttons()

    def _update_buttons(self, *_args):
        has_selection = self._selected_index() is not None
        self.replay_button.setEnabled(has_selection)
        self.export_button.setEnabled(has_selection)
        self.delete_button.setEnabled(has_selection)

    def _selected_index(self):
        """The index into the stored list, which is not the row - the list is reversed."""
        item = self.session_list.currentItem()
        if item is None:
            return None
        return item.data(Qt.ItemDataRole.UserRole)

    def _selected_session(self):
        index = self._selected_index()
        return None if index is None else self._sessions[index]

    # --- replaying ---

    def _on_replay_clicked(self, *_args):
        session = self._selected_session()
        if session is None:
            return

        ignore_paths = self._decide_on_paths(session)
        if ignore_paths is None:
            return

        if not self.main_app.replay_session(session, ignore_paths=ignore_paths):
            QMessageBox.warning(
                self,
                "Nothing to play it with",
                "This session has to be replayed against your own collection, and none is "
                "loaded. Load a folder first, then replay it.",
            )
            return
        self.accept()

    def _decide_on_paths(self, session):
        """True for own-library mode, False for the recorded files, None to call it off."""
        recorded = session_files.recorded_paths(session)
        if not recorded:
            return True  # a stripped session - there is nothing to go looking for

        missing = session_files.missing_paths(session)
        if not missing:
            return False
        if self._ask_about_missing_files(len(missing), len(recorded)):
            return True
        return None

    def _ask_about_missing_files(self, missing, total) -> bool:
        answer = QMessageBox.question(
            self,
            "Some of the files are gone",
            f"{missing} of {total} files this session showed are no longer where they were.\n\n"
            "Replay it against your own loaded collection instead? The rhythms, the climax "
            "and the pacing stay exactly the same - only the pictures are yours.",
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.Cancel,
            QMessageBox.StandardButton.Yes,
        )
        return answer == QMessageBox.StandardButton.Yes

    # --- exporting ---

    def _on_export_clicked(self):
        session = self._selected_session()
        if session is None:
            return

        # Asked before the file picker so cancelling here costs nothing, and asked every
        # time on purpose: the right answer depends on who is getting the file, not on
        # what the user chose last month.
        choice = self._ask_about_paths()
        if choice is None:
            return

        target = self._choose_export_file()
        if target is None:
            return

        payload = session if choice == "with" else session_files.strip_paths(session)
        if not session_files.write_session_file(target, payload):
            QMessageBox.warning(
                self,
                "Could not export",
                "That session could not be written. Try somewhere else.",
            )
            return
        log.info("Session exported (with paths: %s)", choice == "with")
        QMessageBox.information(
            self,
            "Session exported",
            "Exported. Whoever opens it can put it on their own shelf with Import.",
        )

    def _ask_about_paths(self):
        """"with", "without", or None if the user backed out."""
        box = QMessageBox(self)
        box.setIcon(QMessageBox.Icon.Question)
        box.setWindowTitle("Include the media paths?")
        box.setText("Should this export carry the paths of the media it showed?")
        box.setInformativeText(self.PATHS_WARNING)
        without = box.addButton("Leave them out", QMessageBox.ButtonRole.AcceptRole)
        with_paths = box.addButton("Include them", QMessageBox.ButtonRole.DestructiveRole)
        box.addButton(QMessageBox.StandardButton.Cancel)
        # Leaving them out is the default: the safe answer should be the one a distracted
        # user gets by pressing return.
        box.setDefaultButton(without)
        box.exec()

        clicked = box.clickedButton()
        if clicked is without:
            return "without"
        if clicked is with_paths:
            return "with"
        return None

    def _choose_export_file(self):
        path, _filter = QFileDialog.getSaveFileName(
            self, "Export Session", "session.json", FILE_FILTER
        )
        return Path(path) if path else None

    # --- importing ---

    def _on_import_clicked(self):
        source = self._choose_import_file()
        if source is None:
            return
        try:
            session = session_files.read_session_file(source)
        except session_files.UnsupportedSessionFile as error:
            QMessageBox.warning(self, "Could not import", str(error))
            return

        if not session_files.store_session(self.main_app.data_store, session):
            QMessageBox.warning(
                self, "Could not import", "That session could not be saved to your shelf."
            )
            return
        log.info("Session imported")
        self.refresh()

    def _choose_import_file(self):
        path, _filter = QFileDialog.getOpenFileName(self, "Import Session", "", FILE_FILTER)
        return Path(path) if path else None

    # --- deleting ---

    def _on_delete_clicked(self):
        index = self._selected_index()
        if index is None or not self._confirm_deletion():
            return
        session_files.delete_saved_session(self.main_app.data_store, index)
        self.refresh()

    def _confirm_deletion(self) -> bool:
        answer = QMessageBox.question(
            self,
            "Delete this session?",
            "This removes the saved session for good. Exported copies are not touched.",
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
            QMessageBox.StandardButton.No,
        )
        return answer == QMessageBox.StandardButton.Yes
