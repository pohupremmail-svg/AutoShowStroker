from PyQt6.QtCore import QTimer
from PyQt6.QtWidgets import (
    QDialog,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QListWidget,
    QListWidgetItem,
    QPushButton,
    QVBoxLayout,
    QWidget,
)

from src import theme
from src.PatternStepWidget import PatternStepWidget

MAX_STEPS = 16
DEFAULT_STEPS = [1, 1, 1, 1]
# Preview duration for a weight-1 (longest) step; shorter steps get base/weight,
# matching BeatHandler's own inverse-duration encoding (see PatternStepWidget).
PREVIEW_BASE_STEP_MS = 400


class PatternEditorDialog(QDialog):
    """Music-producer-style step-sequencer editor for user-defined beat patterns.

    Built-in patterns (BeatHandler.BEAT_PATTERNS_MAP) are read-only and never
    listed here - only entries in beat_handler.custom_beat_patterns are editable.
    """

    def __init__(self, beat_handler, parent=None):
        super().__init__(parent)
        self.beat_handler = beat_handler
        self.setWindowTitle("Manage Custom Patterns")
        self.setModal(True)
        self.resize(640, 360)

        self._editing_name = None  # None while creating a brand new pattern
        self._step_widgets = []
        self._preview_position = 0
        self._preview_timer = QTimer(self)
        self._preview_timer.setSingleShot(True)
        self._preview_timer.timeout.connect(self._preview_tick)

        root = QHBoxLayout(self)

        left = QVBoxLayout()
        left.addWidget(QLabel("Your Patterns"))
        self.pattern_list = QListWidget()
        self.pattern_list.itemSelectionChanged.connect(self._on_selection_changed)
        left.addWidget(self.pattern_list)

        list_buttons = QHBoxLayout()
        self.new_button = QPushButton("New")
        self.new_button.clicked.connect(self._start_new_pattern)
        self.delete_button = QPushButton("Delete")
        self.delete_button.clicked.connect(self._delete_selected_pattern)
        list_buttons.addWidget(self.new_button)
        list_buttons.addWidget(self.delete_button)
        left.addLayout(list_buttons)
        root.addLayout(left, 1)

        right = QVBoxLayout()
        name_row = QHBoxLayout()
        name_row.addWidget(QLabel("Name:"))
        self.name_edit = QLineEdit()
        name_row.addWidget(self.name_edit)
        right.addLayout(name_row)

        self.steps_layout = QHBoxLayout()
        steps_container = QWidget()
        steps_container.setLayout(self.steps_layout)
        right.addWidget(steps_container)

        step_buttons = QHBoxLayout()
        self.add_step_button = QPushButton("+ Step")
        self.add_step_button.clicked.connect(self._add_step)
        self.remove_step_button = QPushButton("- Step")
        self.remove_step_button.clicked.connect(self._remove_step)
        step_buttons.addWidget(self.add_step_button)
        step_buttons.addWidget(self.remove_step_button)
        right.addLayout(step_buttons)

        self.error_label = QLabel("")
        self.error_label.setStyleSheet(f"color: {theme.DENIED};")
        right.addWidget(self.error_label)

        action_row = QHBoxLayout()
        self.preview_button = QPushButton("Preview")
        self.preview_button.clicked.connect(self._toggle_preview)
        self.save_button = QPushButton("Save")
        self.save_button.setObjectName("primary")
        self.save_button.clicked.connect(self._save)
        action_row.addWidget(self.preview_button)
        action_row.addWidget(self.save_button)
        right.addLayout(action_row)

        root.addLayout(right, 2)

        self._refresh_pattern_list()
        self._start_new_pattern()

    # --- list management ---

    def _refresh_pattern_list(self):
        self.pattern_list.blockSignals(True)
        self.pattern_list.clear()
        for name in self.beat_handler.custom_beat_patterns:
            self.pattern_list.addItem(QListWidgetItem(name))
        self.pattern_list.blockSignals(False)

    def _select_pattern_in_list(self, name):
        for i in range(self.pattern_list.count()):
            item = self.pattern_list.item(i)
            if item.text() == name:
                self.pattern_list.blockSignals(True)
                item.setSelected(True)
                self.pattern_list.blockSignals(False)
                break

    def _on_selection_changed(self):
        items = self.pattern_list.selectedItems()
        if not items:
            return
        name = items[0].text()
        self._load_pattern_into_editor(name, self.beat_handler.custom_beat_patterns[name])

    def _start_new_pattern(self):
        self.pattern_list.clearSelection()
        self._load_pattern_into_editor(None, list(DEFAULT_STEPS))

    def _load_pattern_into_editor(self, name, steps):
        self._stop_preview()
        self._editing_name = name
        self.name_edit.setText(name or "")
        self._set_steps(steps)
        self.error_label.setText("")

    # --- step widgets ---

    def _set_steps(self, steps):
        for widget in self._step_widgets:
            self.steps_layout.removeWidget(widget)
            widget.deleteLater()
        self._step_widgets = []
        for value in steps:
            self._append_step_widget(value)

    def _append_step_widget(self, value=1):
        widget = PatternStepWidget(value)
        self.steps_layout.addWidget(widget)
        self._step_widgets.append(widget)
        return widget

    def _add_step(self):
        if len(self._step_widgets) >= MAX_STEPS:
            return
        self._append_step_widget(1)

    def _remove_step(self):
        if len(self._step_widgets) <= 1:
            return
        widget = self._step_widgets.pop()
        self.steps_layout.removeWidget(widget)
        widget.deleteLater()

    def _current_steps(self):
        return [w.get_value() for w in self._step_widgets]

    # --- save / delete ---

    def _save(self):
        name = self.name_edit.text().strip()
        steps = self._current_steps()
        renamed_from = self._editing_name if self._editing_name and self._editing_name != name else None
        try:
            self.beat_handler.add_or_update_custom_pattern(name, steps)
        except ValueError as exc:
            self.error_label.setText(str(exc))
            return
        if renamed_from:
            self.beat_handler.delete_custom_pattern(renamed_from)
        self.error_label.setText("")
        self._editing_name = name
        self._refresh_pattern_list()
        self._select_pattern_in_list(name)

    def _delete_selected_pattern(self):
        items = self.pattern_list.selectedItems()
        if not items:
            return
        name = items[0].text()
        self.beat_handler.delete_custom_pattern(name)
        self._refresh_pattern_list()
        self._start_new_pattern()

    # --- preview ---

    def _toggle_preview(self):
        if self._preview_timer.isActive():
            self._stop_preview()
        else:
            self._start_preview()

    def _start_preview(self):
        if not self._step_widgets:
            return
        self._preview_position = 0
        self.preview_button.setText("Stop")
        self._preview_tick()

    def _stop_preview(self):
        self._preview_timer.stop()
        self.preview_button.setText("Preview")
        for widget in self._step_widgets:
            widget.set_highlighted(False)

    def _preview_tick(self):
        if not self._step_widgets:
            self._stop_preview()
            return
        for widget in self._step_widgets:
            widget.set_highlighted(False)
        widget = self._step_widgets[self._preview_position]
        widget.set_highlighted(True)
        if widget.get_value() > 0:
            self.beat_handler.play_beat_sound()

        # Same inverse-duration relationship as BeatHandler: divide the base step
        # time by this step's weight, so a "4" plays a quarter as long as a "1".
        step_ms = int(PREVIEW_BASE_STEP_MS / abs(widget.get_value()))
        self._preview_position = (self._preview_position + 1) % len(self._step_widgets)
        self._preview_timer.start(step_ms)

    def closeEvent(self, event):
        self._stop_preview()
        super().closeEvent(event)
