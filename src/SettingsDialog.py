from PyQt6.QtCore import QSettings
from PyQt6.QtWidgets import QDialog, QVBoxLayout, QHBoxLayout, QLabel, QDoubleSpinBox, QPushButton, QGridLayout, \
    QCheckBox


class SettingsDialog(QDialog):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Settings")
        self.setModal(True)

        self.main_app = parent
        self.beat_handler = self.main_app.beat_handler

        self.layout = QVBoxLayout(self)
        self.settings_fields = {}

        self.add_section_header("Slideshow Timing (Pictures/GIFs)")
        self.add_setting("Min. duration (s):", "min_dur", self.main_app, float, 0.1, 60.0, 0.1)
        self.add_setting("Max. duration (s):", "max_dur", self.main_app, float, 0.1, 60.0, 0.1)
        self.add_setting("Video Min. duration (s):", "video_min_dur", self.main_app, float, 0.5, 30.0, 0.1)

        self.add_section_header("Beat Timing (BeatHandler)")
        self.add_setting("Beat Min. frequency (Hz):", "min_beat_freq", self.beat_handler, float, 0.1, 20.0, 0.1)
        self.add_setting("Beat Max. frequency (Hz):", "max_beat_freq", self.beat_handler, float, 0.1, 20.0, 0.1)
        self.add_setting("Beat Min. duration (s):", "min_beat_dur", self.beat_handler, float, 1.0, 120.0, 1.0)
        self.add_setting("Beat Max. duration (s):", "max_beat_dur", self.beat_handler, float, 1.0, 120.0, 1.0)
        self.add_setting("Pause Min. duration (s):", "min_pause_dur", self.beat_handler, int, 1, 180, 1)
        self.add_setting("Pause Max. duration (s):", "max_pause_dur", self.beat_handler, int, 1, 180, 1)
        self.add_setting("Beat change chance", "beat_change_chance", self.beat_handler, float, 0.01, 1, 0.01)
        self.add_setting("Pause chance", "pause_chance", self.beat_handler, float, 0.001, 1, 0.001)

        self.add_section_header("General Settings")
        self.add_setting("Beat Volume", "beat_loudness", self.beat_handler, float, 0.0, 1.0, 0.1)
        self.add_setting("Video Volume", "vid_loudness", self.main_app, float, 0.0, 1.0, 0.1)

        self.add_beat_selection()

        self.button_ok = QPushButton("Save & Close Settings")
        self.button_ok.clicked.connect(self.accept_settings)
        self.layout.addWidget(self.button_ok)

    def add_section_header(self, title):
        header = QLabel(f"--- <b>{title}</b> ---")
        header.setStyleSheet("font-size: 14px; margin-top: 10px;")
        self.layout.addWidget(header)

    def add_setting(self, label_text, var_name, target_object, var_type, min_val, max_val, step):
        h_layout = QHBoxLayout()

        label = QLabel(label_text)
        h_layout.addWidget(label, stretch=1)

        spinbox = QDoubleSpinBox()
        spinbox.setRange(min_val, max_val)
        spinbox.setSingleStep(step)

        initial_value = getattr(target_object, var_name)
        spinbox.setValue(initial_value)

        h_layout.addWidget(spinbox, stretch=2)
        self.layout.addLayout(h_layout)

        self.settings_fields[var_name] = {
            'widget': spinbox,
            'object': target_object,
            'type': var_type
        }

    def accept_settings(self):
        settings = QSettings("GoonerCock", "GoonerApp")

        for var_name, data in self.settings_fields.items():
            new_value = data['widget'].value()

            setattr(data['object'], var_name, new_value)

            key = f"{data['object'].__class__.__name__}/{var_name}"
            settings.setValue(key, new_value)

        self.beat_handler.sound_effect.setVolume(self.settings_fields['beat_loudness']['widget'].value())

        new_selected_patterns = []
        for name, checkbox in self.beat_checkboxes.items():
            if checkbox.isChecked():
                new_selected_patterns.append(name)
        self.beat_handler.selected_beat_patterns = new_selected_patterns

        settings.setValue("BeatHandler/selected_beat_patterns", new_selected_patterns)

        self.beat_handler.recalc_beat()
        self.accept()

    def add_beat_selection(self):
        self.add_section_header("Active Rhythms")

        grid = QGridLayout()
        self.beat_checkboxes = {}

        patterns = self.beat_handler.available_beat_patterns

        row = 0
        col = 0

        for name, pattern_list in patterns.items():
            display_text = f"({name} {pattern_list})"

            checkbox = QCheckBox(display_text)

            if name in self.beat_handler.selected_beat_patterns:
                checkbox.setChecked(True)

            grid.addWidget(checkbox, row, col)
            self.beat_checkboxes[name] = checkbox

            col += 1
            if col > 1:
                col = 0
                row += 1

        self.layout.addLayout(grid)