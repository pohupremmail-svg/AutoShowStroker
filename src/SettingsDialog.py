
from PyQt6.QtWidgets import (
    QCheckBox,
    QComboBox,
    QDialog,
    QDoubleSpinBox,
    QGridLayout,
    QHBoxLayout,
    QLabel,
    QPushButton,
    QScrollArea,
    QTabWidget,
    QVBoxLayout,
    QWidget,
)

from src import theme


class SettingsDialog(QDialog):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Settings")
        self.setModal(True)

        self.main_app = parent
        self.beat_handler = self.main_app.beat_handler
        self.callout_handler = self.main_app.callout_handler
        self.climax_handler = self.main_app.climax_handler

        self.layout = QVBoxLayout(self)
        self.settings_fields = {}
        self.resize(560, 640)

        self.tabs = QTabWidget()

        self._current_layout = self._new_tab("Playback")
        self.add_section_header("Slideshow Timing (Pictures/GIFs)")
        self.add_setting("Min. duration (s):", "min_dur", self.main_app, float, 0.1, 60.0, 0.1)
        self.add_setting("Max. duration (s):", "max_dur", self.main_app, float, 0.1, 60.0, 0.1)
        self.add_setting("Video Min. duration (s):", "video_min_dur", self.main_app, float, 0.5, 30.0, 0.1)
        self.add_section_header("General Settings")
        self.add_setting("Beat Volume", "beat_loudness", self.beat_handler, float, 0.0, 1.0, 0.1)
        self.add_setting("Video Volume", "vid_loudness", self.main_app, float, 0.0, 1.0, 0.1)
        self.playback_reset_button = self.add_reset_button(
            ["min_dur", "max_dur", "video_min_dur", "beat_loudness", "vid_loudness"]
        )
        self._current_layout.addStretch()

        self._current_layout = self._new_tab("Beat && Rhythm")
        self.add_section_header("Beat Timing (BeatHandler)")
        self.add_setting("Beat Min. frequency (Hz):", "min_beat_freq", self.beat_handler, float, 0.1, 20.0, 0.1)
        self.add_setting("Beat Max. frequency (Hz):", "max_beat_freq", self.beat_handler, float, 0.1, 20.0, 0.1)
        self.add_setting("Beat Min. duration (s):", "min_beat_dur", self.beat_handler, float, 1.0, 120.0, 1.0)
        self.add_setting("Beat Max. duration (s):", "max_beat_dur", self.beat_handler, float, 1.0, 120.0, 1.0)
        self.add_setting("Pause Min. duration (s):", "min_pause_dur", self.beat_handler, int, 1, 180, 1)
        self.add_setting("Pause Max. duration (s):", "max_pause_dur", self.beat_handler, int, 1, 180, 1)
        self.add_setting("Beat change chance", "beat_change_chance", self.beat_handler, float, 0.01, 1, 0.01)
        self.add_setting("Pause chance", "pause_chance", self.beat_handler, float, 0.001, 1, 0.001)

        self.add_section_header("Difficulty Ramping")
        self.ramping_active_checkbox = QCheckBox("Difficulty ramping active")
        self.ramping_active_checkbox.setChecked(self.beat_handler.ramping_active)
        self._current_layout.addWidget(self.ramping_active_checkbox)
        self.add_setting(
            "Ramp Min. duration (s):", "min_ramp_duration", self.beat_handler, float, 10.0, 7200.0, 10.0
        )
        self.add_setting(
            "Ramp Max. duration (s):", "max_ramp_duration", self.beat_handler, float, 10.0, 7200.0, 10.0
        )
        self.add_setting(
            "Ramp window width (0-1):", "ramp_window_width", self.beat_handler, float, 0.05, 1.0, 0.05
        )

        self.add_beat_selection()
        self.beat_reset_button = self.add_reset_button(
            [
                "min_beat_freq", "max_beat_freq", "min_beat_dur", "max_beat_dur",
                "min_pause_dur", "max_pause_dur", "beat_change_chance", "pause_chance",
                "min_ramp_duration", "max_ramp_duration", "ramp_window_width",
            ],
            checkbox_defaults=[
                (self.ramping_active_checkbox, self.beat_handler.DEFAULTS["ramping_active"]),
            ],
            extra_reset=lambda: [cb.setChecked(True) for cb in self.beat_checkboxes.values()],
        )
        self._current_layout.addStretch()

        self._current_layout = self._new_tab("Climax")
        self.add_section_header("Climax")
        self.climax_active_checkbox = QCheckBox("Climax prompts active")
        self.climax_active_checkbox.setChecked(self.climax_handler.climax_active)
        self._current_layout.addWidget(self.climax_active_checkbox)
        self.add_setting(
            "Climax chance (per beat change, after ramp)", "climax_chance", self.climax_handler, float, 0.0, 1.0, 0.01
        )

        self.ruined_orgasm_active_checkbox = QCheckBox("Allow ruined orgasm outcome")
        self.ruined_orgasm_active_checkbox.setChecked(self.climax_handler.ruined_orgasm_active)
        self._current_layout.addWidget(self.ruined_orgasm_active_checkbox)
        self.add_setting(
            "Ruined orgasm chance (of this session's climax)",
            "ruined_orgasm_chance", self.climax_handler, float, 0.0, 1.0, 0.01
        )

        self.denied_orgasm_active_checkbox = QCheckBox("Allow full denial outcome")
        self.denied_orgasm_active_checkbox.setChecked(self.climax_handler.denied_orgasm_active)
        self._current_layout.addWidget(self.denied_orgasm_active_checkbox)
        self.add_setting(
            "Denied orgasm chance (of this session's climax)",
            "denied_orgasm_chance", self.climax_handler, float, 0.0, 1.0, 0.01
        )

        self.fake_climax_active_checkbox = QCheckBox("Fake climax cues active")
        self.fake_climax_active_checkbox.setChecked(self.climax_handler.fake_climax_active)
        self._current_layout.addWidget(self.fake_climax_active_checkbox)
        self.add_setting(
            "Fake climax chance (per beat change)", "fake_climax_chance", self.climax_handler, float, 0.0, 1.0, 0.01
        )
        self.add_setting(
            "Fake climax reveal delay Min. (s)", "min_fake_climax_delay", self.climax_handler, float, 1.0, 30.0, 0.5
        )
        self.add_setting(
            "Fake climax reveal delay Max. (s)", "max_fake_climax_delay", self.climax_handler, float, 1.0, 30.0, 0.5
        )
        self.climax_reset_button = self.add_reset_button(
            [
                "climax_chance", "ruined_orgasm_chance", "denied_orgasm_chance",
                "fake_climax_chance", "min_fake_climax_delay", "max_fake_climax_delay",
            ],
            checkbox_defaults=[
                (self.climax_active_checkbox, self.climax_handler.DEFAULTS["climax_active"]),
                (self.ruined_orgasm_active_checkbox, self.climax_handler.DEFAULTS["ruined_orgasm_active"]),
                (self.denied_orgasm_active_checkbox, self.climax_handler.DEFAULTS["denied_orgasm_active"]),
                (self.fake_climax_active_checkbox, self.climax_handler.DEFAULTS["fake_climax_active"]),
            ],
        )
        self._current_layout.addStretch()

        self._current_layout = self._new_tab("Callouts")
        self.add_callout_selection()
        self.callout_reset_button = self.add_reset_button(
            ["talking_chance"],
            checkbox_defaults=[
                (self.callout_active_checkbox, self.callout_handler.DEFAULTS["active_callout"]),
            ],
            extra_reset=self._reset_callout_lang,
        )
        self._current_layout.addStretch()

        self.layout.addWidget(self.tabs)

        self.button_ok = QPushButton("Save & Close Settings")
        self.button_ok.setObjectName("primary")
        self.button_ok.clicked.connect(self.accept_settings)
        self.layout.addWidget(self.button_ok)

    def _new_tab(self, title):
        content = QWidget()
        layout = QVBoxLayout(content)

        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setWidget(content)

        self.tabs.addTab(scroll, title)
        return layout

    def add_reset_button(self, spinbox_names, checkbox_defaults=None, extra_reset=None):
        """Adds a 'Reset to defaults' button to the current tab. Only resets widget values -
        the user still has to press Save & Close Settings to actually apply/persist them,
        same as any other change made in this dialog."""
        checkbox_defaults = checkbox_defaults or []
        button = QPushButton("Reset to defaults")

        def do_reset():
            for var_name in spinbox_names:
                field = self.settings_fields[var_name]
                field['widget'].setValue(field['object'].DEFAULTS[var_name])
            for checkbox, default_value in checkbox_defaults:
                checkbox.setChecked(default_value)
            if extra_reset:
                extra_reset()

        button.clicked.connect(do_reset)
        self._current_layout.addWidget(button)
        return button

    def _reset_callout_lang(self):
        default_lang = self.callout_handler.DEFAULTS["lang"]
        index = self.callout_selected_lang.findText(default_lang)
        if index != -1:
            self.callout_selected_lang.setCurrentIndex(index)

    def add_section_header(self, title):
        header = QLabel(f"--- <b>{title}</b> ---")
        header.setStyleSheet(f"font-size: 14px; margin-top: 10px; color: {theme.ACCENT}; font-weight: bold;")
        self._current_layout.addWidget(header)

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
        self._current_layout.addLayout(h_layout)

        self.settings_fields[var_name] = {
            'widget': spinbox,
            'object': target_object,
            'type': var_type
        }

    def accept_settings(self):
        settings = self.main_app.settings

        for var_name, data in self.settings_fields.items():
            new_value = data['widget'].value()

            if data['type'] is int:
                setattr(data['object'], var_name, int(new_value))
            else:
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

        settings.setValue("BeatHandler/ramping_active", self.ramping_active_checkbox.isChecked())
        self.beat_handler.ramping_active = self.ramping_active_checkbox.isChecked()

        settings.setValue("ClimaxHandler/climax_active", self.climax_active_checkbox.isChecked())
        self.climax_handler.climax_active = self.climax_active_checkbox.isChecked()

        settings.setValue("ClimaxHandler/ruined_orgasm_active", self.ruined_orgasm_active_checkbox.isChecked())
        self.climax_handler.ruined_orgasm_active = self.ruined_orgasm_active_checkbox.isChecked()

        settings.setValue("ClimaxHandler/denied_orgasm_active", self.denied_orgasm_active_checkbox.isChecked())
        self.climax_handler.denied_orgasm_active = self.denied_orgasm_active_checkbox.isChecked()

        settings.setValue("ClimaxHandler/fake_climax_active", self.fake_climax_active_checkbox.isChecked())
        self.climax_handler.fake_climax_active = self.fake_climax_active_checkbox.isChecked()

        settings.setValue("CalloutHandler/active_callout", self.callout_active_checkbox.isChecked())
        self.callout_handler.active_callout = self.callout_active_checkbox.isChecked()
        settings.setValue("CalloutHandler/selected_lang", self.callout_selected_lang.currentText())
        self.callout_handler.set_lang(self.callout_selected_lang.currentText())

        if self.main_app.is_running:
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

        self._current_layout.addLayout(grid)

    def add_callout_selection(self):
        self.add_section_header("Callouts")

        self.callout_active_checkbox = QCheckBox("Callouts active")
        self.callout_active_checkbox.setChecked(self.callout_handler.active_callout)

        self.callout_selected_lang = QComboBox()
        self.callout_selected_lang.addItems(self.callout_handler.available_languages)
        inital_index = self.callout_selected_lang.findText(self.callout_handler.lang)
        if inital_index != -1:
            self.callout_selected_lang.setCurrentIndex(inital_index)

        self._current_layout.addWidget(self.callout_active_checkbox)
        self._current_layout.addWidget(self.callout_selected_lang)

        self.add_setting(
            "Chance for callouts to happen during events", "talking_chance", self.callout_handler, float, 0, 1, 0.01
        )
