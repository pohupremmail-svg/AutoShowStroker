
from PyQt6.QtWidgets import (
    QCheckBox,
    QComboBox,
    QDialog,
    QDoubleSpinBox,
    QGridLayout,
    QHBoxLayout,
    QLabel,
    QMessageBox,
    QPushButton,
    QScrollArea,
    QTabWidget,
    QVBoxLayout,
    QWidget,
)

from src import applog, theme
from src.CustomPhraseFilesDialog import CustomPhraseFilesDialog
from src.IntifaceSettingsWidget import IntifaceSettingsWidget
from src.PatternEditorDialog import PatternEditorDialog

log = applog.get_logger(__name__)


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
        self.show_startup_splash_checkbox = QCheckBox("Show startup splash animation")
        self.show_startup_splash_checkbox.setChecked(self.main_app.show_startup_splash)
        self._current_layout.addWidget(self.show_startup_splash_checkbox)
        self.show_record_chase_checkbox = QCheckBox("Show live personal-record chase")
        self.show_record_chase_checkbox.setChecked(self.main_app.show_record_chase)
        self._current_layout.addWidget(self.show_record_chase_checkbox)
        self.show_session_timer_checkbox = QCheckBox("Show session timer")
        self.show_session_timer_checkbox.setChecked(self.main_app.show_session_timer)
        self._current_layout.addWidget(self.show_session_timer_checkbox)
        self.playback_reset_button = self.add_reset_button(
            ["min_dur", "max_dur", "video_min_dur", "beat_loudness", "vid_loudness"],
            checkbox_defaults=[
                (self.show_startup_splash_checkbox, self.main_app.DEFAULTS["show_startup_splash"]),
                (self.show_record_chase_checkbox, self.main_app.DEFAULTS["show_record_chase"]),
                (self.show_session_timer_checkbox, self.main_app.DEFAULTS["show_session_timer"]),
            ],
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
        self.add_setting("Pause chance (per beat change)", "pause_chance", self.beat_handler, float, 0.001, 1, 0.001)

        self.add_section_header("Edge Relief")
        self.edge_relief_active_checkbox = QCheckBox("\"I reached my Edge\" button active")
        self.edge_relief_active_checkbox.setToolTip(
            "Press E during a session for a pause now and a gentler rhythm behind it. The "
            "climax waits out the break rather than being paid for with it."
        )
        self.edge_relief_active_checkbox.setChecked(self.beat_handler.edge_relief_active)
        self._current_layout.addWidget(self.edge_relief_active_checkbox)
        self.add_setting("Edge pause duration (s):", "edge_pause_dur", self.beat_handler, int, 5, 300, 5)
        self.add_setting("Edge cooldown (s):", "edge_cooldown_sec", self.beat_handler, int, 0, 900, 10)

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
                "min_pause_dur", "max_pause_dur", "pause_chance",
                "edge_pause_dur", "edge_cooldown_sec",
                "min_ramp_duration", "max_ramp_duration", "ramp_window_width",
            ],
            checkbox_defaults=[
                (self.ramping_active_checkbox, self.beat_handler.DEFAULTS["ramping_active"]),
                (self.edge_relief_active_checkbox, self.beat_handler.DEFAULTS["edge_relief_active"]),
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
            "Climax earliest (s into session):", "min_climax_after", self.climax_handler, float, 10.0, 7200.0, 10.0
        )
        self.add_setting(
            "Climax latest (s into session):", "max_climax_after", self.climax_handler, float, 10.0, 7200.0, 10.0
        )
        self.climax_only_after_ramp_checkbox = QCheckBox("Climax only after ramping finishes")
        self.climax_only_after_ramp_checkbox.setToolTip(
            "Holds the climax back until the difficulty ramp has topped out, however early "
            "the window above allows it. Has no effect while difficulty ramping is off."
        )
        self.climax_only_after_ramp_checkbox.setChecked(self.climax_handler.climax_only_after_ramp)
        self._current_layout.addWidget(self.climax_only_after_ramp_checkbox)

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
        self.ask_for_outcome_checkbox = QCheckBox("Ask what actually happened")
        self.ask_for_outcome_checkbox.setToolTip(
            "Offers I Came / I Ruined It / I Stopped at every climax cue, and asks once when "
            "you stop a session yourself - otherwise the app only ever records what it told "
            "you to do."
        )
        self.ask_for_outcome_checkbox.setChecked(self.main_app.ask_for_outcome)
        self._current_layout.addWidget(self.ask_for_outcome_checkbox)

        self.climax_reset_button = self.add_reset_button(
            [
                "min_climax_after", "max_climax_after",
                "ruined_orgasm_chance", "denied_orgasm_chance",
                "fake_climax_chance", "min_fake_climax_delay", "max_fake_climax_delay",
            ],
            checkbox_defaults=[
                (self.climax_active_checkbox, self.climax_handler.DEFAULTS["climax_active"]),
                (
                    self.climax_only_after_ramp_checkbox,
                    self.climax_handler.DEFAULTS["climax_only_after_ramp"],
                ),
                (self.ruined_orgasm_active_checkbox, self.climax_handler.DEFAULTS["ruined_orgasm_active"]),
                (self.denied_orgasm_active_checkbox, self.climax_handler.DEFAULTS["denied_orgasm_active"]),
                (self.fake_climax_active_checkbox, self.climax_handler.DEFAULTS["fake_climax_active"]),
                (self.ask_for_outcome_checkbox, self.main_app.DEFAULTS["ask_for_outcome"]),
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
            extra_reset=self._reset_callout_lang_and_tones,
        )
        self._current_layout.addStretch()

        device_layout = self._new_tab("Device")
        self.intiface_tab = IntifaceSettingsWidget(self.main_app, self)
        device_layout.addWidget(self.intiface_tab)

        self.layout.addWidget(self.tabs)

        # && - a single & is a mnemonic prefix and gets swallowed, leaving "Save  Close".
        self.button_ok = QPushButton("Save && Close Settings")
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

    def _reset_callout_lang_and_tones(self):
        default_lang = self.callout_handler.DEFAULTS["lang"]
        index = self.callout_selected_lang.findText(default_lang)
        if index != -1:
            self.callout_selected_lang.setCurrentIndex(index)

        default_tones = self.callout_handler.DEFAULTS["selected_tones"]
        for tone, checkbox in self.tone_checkboxes.items():
            checkbox.setChecked(tone in default_tones)

    def add_section_header(self, title):
        header = QLabel(f"--- <b>{title}</b> ---")
        header.setStyleSheet(f"font-size: 14px; margin-top: 10px; color: {theme.ACCENT}; font-weight: bold;")
        self._current_layout.addWidget(header)

    @staticmethod
    def _decimals_for(step) -> int:
        """Enough decimal places to represent `step` exactly, with Qt's default of 2 as the
        floor so the ordinary 0.1/0.01 fields keep looking the way they always have."""
        text = f"{float(step):.10f}".rstrip("0")
        fractional = text.split(".")[1] if "." in text else ""
        return max(2, len(fractional))

    def add_setting(self, label_text, var_name, target_object, var_type, min_val, max_val, step):
        h_layout = QHBoxLayout()

        label = QLabel(label_text)
        h_layout.addWidget(label, stretch=1)

        spinbox = QDoubleSpinBox()
        # Decimals before range/step: QDoubleSpinBox defaults to 2, and setValue() rounds to
        # that. "Pause chance" steps by 0.001, so every arrow click was rounded straight back
        # to where it started and the declared 0.001 minimum was unreachable.
        spinbox.setDecimals(self._decimals_for(step))
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

    # Spinbox pairs where the first must not exceed the second, with the label used in the
    # rejection message. Only min_pause_dur/max_pause_dur actually crashes (random.randint),
    # but every one of these produces an inverted, meaningless range if saved that way.
    MIN_MAX_PAIRS = (
        ("min_dur", "max_dur", "Slideshow duration"),
        ("min_beat_freq", "max_beat_freq", "Beat frequency"),
        ("min_beat_dur", "max_beat_dur", "Beat duration"),
        ("min_pause_dur", "max_pause_dur", "Pause duration"),
        ("min_ramp_duration", "max_ramp_duration", "Ramp duration"),
        ("min_climax_after", "max_climax_after", "Climax time"),
        ("min_fake_climax_delay", "max_fake_climax_delay", "Fake climax reveal delay"),
    )

    def _validation_error(self):
        """First reason these settings can't be saved, or None if they're fine."""
        device_error = self.intiface_tab.validation_error()
        if device_error:
            return device_error
        if not any(checkbox.isChecked() for checkbox in self.beat_checkboxes.values()):
            return "At least one rhythm has to stay active under 'Active Rhythms'."
        for min_name, max_name, label in self.MIN_MAX_PAIRS:
            if self.settings_fields[min_name]['widget'].value() > self.settings_fields[max_name]['widget'].value():
                return f"{label}: the minimum must not be higher than the maximum."
        return None

    def _show_validation_error(self, message):
        box = QMessageBox(self)
        box.setIcon(QMessageBox.Icon.Warning)
        box.setWindowTitle("Can't save these settings")
        box.setText(message)
        box.exec()

    def accept_settings(self):
        # Checked before anything is written: accept_settings applies values straight to the
        # live handlers, so a half-applied invalid set would take effect even after a refusal.
        error = self._validation_error()
        if error:
            log.info("Settings rejected: %s", error)
            self._show_validation_error(error)
            return

        settings = self.main_app.settings

        for var_name, data in self.settings_fields.items():
            new_value = data['widget'].value()

            if data['type'] is int:
                setattr(data['object'], var_name, int(new_value))
            else:
                setattr(data['object'], var_name, new_value)


            key = f"{data['object'].SETTINGS_GROUP}/{var_name}"
            settings.setValue(key, new_value)

        self.beat_handler.sound_effect.setVolume(self.settings_fields['beat_loudness']['widget'].value())

        settings.setValue("GoonerApp/show_startup_splash", self.show_startup_splash_checkbox.isChecked())
        self.main_app.show_startup_splash = self.show_startup_splash_checkbox.isChecked()

        settings.setValue("GoonerApp/show_record_chase", self.show_record_chase_checkbox.isChecked())
        self.main_app.show_record_chase = self.show_record_chase_checkbox.isChecked()
        self.main_app._update_record_chase()

        settings.setValue("GoonerApp/show_session_timer", self.show_session_timer_checkbox.isChecked())
        self.main_app.show_session_timer = self.show_session_timer_checkbox.isChecked()
        settings.setValue("GoonerApp/ask_for_outcome", self.ask_for_outcome_checkbox.isChecked())
        self.main_app.ask_for_outcome = self.ask_for_outcome_checkbox.isChecked()
        self.main_app._update_session_timer()

        new_selected_patterns = []
        for name, checkbox in self.beat_checkboxes.items():
            if checkbox.isChecked():
                new_selected_patterns.append(name)
        self.beat_handler.selected_beat_patterns = new_selected_patterns

        settings.setValue("BeatHandler/selected_beat_patterns", new_selected_patterns)

        settings.setValue("BeatHandler/ramping_active", self.ramping_active_checkbox.isChecked())
        self.beat_handler.ramping_active = self.ramping_active_checkbox.isChecked()

        settings.setValue("BeatHandler/edge_relief_active", self.edge_relief_active_checkbox.isChecked())
        self.beat_handler.edge_relief_active = self.edge_relief_active_checkbox.isChecked()
        # Applied at once rather than at the next session: a button that is still there but
        # switched off would do nothing when pressed.
        self.main_app._reset_edge_button()

        settings.setValue("ClimaxHandler/climax_active", self.climax_active_checkbox.isChecked())
        self.climax_handler.climax_active = self.climax_active_checkbox.isChecked()

        settings.setValue(
            "ClimaxHandler/climax_only_after_ramp", self.climax_only_after_ramp_checkbox.isChecked()
        )
        self.climax_handler.climax_only_after_ramp = self.climax_only_after_ramp_checkbox.isChecked()

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
        self.callout_handler.set_tones(self._ticked_tones())
        settings.setValue("CalloutHandler/selected_tones", self.callout_handler.selected_tones)

        # The running segment plays out on the old values - cutting the beat the user is
        # currently following out from under them to prove the save worked would be worse
        # than waiting. Everything still queued is rebuilt from the new ones.
        if self.main_app.is_running:
            self.climax_handler.settings_changed()
            self.beat_handler.replan_from_next_segment()
        self.intiface_tab.apply_settings()
        log.info("Settings saved (%d active rhythms)", len(new_selected_patterns))
        self.accept()

    def add_beat_selection(self):
        self.add_section_header("Active Rhythms")

        self.beat_grid_layout = QGridLayout()
        self.beat_checkboxes = {}
        self._populate_beat_grid()
        self._current_layout.addLayout(self.beat_grid_layout)

        self.manage_patterns_button = QPushButton("Manage Custom Patterns...")
        self.manage_patterns_button.clicked.connect(self._open_pattern_editor)
        self._current_layout.addWidget(self.manage_patterns_button)

    def _populate_beat_grid(self):
        while self.beat_grid_layout.count():
            item = self.beat_grid_layout.takeAt(0)
            widget = item.widget()
            if widget:
                widget.deleteLater()
        self.beat_checkboxes = {}

        patterns = self.beat_handler.available_beat_patterns

        row = 0
        col = 0

        for name, pattern_list in patterns.items():
            display_text = f"({name} {pattern_list})"

            checkbox = QCheckBox(display_text)

            if name in self.beat_handler.selected_beat_patterns:
                checkbox.setChecked(True)

            self.beat_grid_layout.addWidget(checkbox, row, col)
            self.beat_checkboxes[name] = checkbox

            col += 1
            if col > 1:
                col = 0
                row += 1

    def refresh_beat_selection(self):
        self._populate_beat_grid()

    def _open_pattern_editor(self):
        PatternEditorDialog(self.beat_handler, parent=self).exec()
        self.refresh_beat_selection()

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

        self._add_tone_selection()

        self.add_setting(
            "Chance for callouts to happen during events", "talking_chance", self.callout_handler, float, 0, 1, 0.01
        )

        self.manage_phrase_files_button = QPushButton("Manage Custom Phrase Files...")
        self.manage_phrase_files_button.clicked.connect(self._open_phrase_files_dialog)
        self._current_layout.addWidget(self.manage_phrase_files_button)

    def _add_tone_selection(self):
        """Tone is the second axis next to language, and unlike language it is a mix -
        hence checkboxes, laid out like the Active Rhythms grid rather than a combo box."""
        self.add_section_header("Tone")

        hint = QLabel("Pick one or more. Ticking several mixes them evenly.")
        hint.setStyleSheet(f"color: {theme.TEXT}; font-size: 11px;")
        self._current_layout.addWidget(hint)

        tone_grid = QGridLayout()
        self.tone_checkboxes = {}
        for index, tone in enumerate(self.callout_handler.available_tones):
            checkbox = QCheckBox(self.callout_handler.tone_label(tone))
            checkbox.setChecked(tone in self.callout_handler.selected_tones)
            tone_grid.addWidget(checkbox, index // 2, index % 2)
            self.tone_checkboxes[tone] = checkbox
        self._current_layout.addLayout(tone_grid)

        self.tone_hint = QLabel("")
        self.tone_hint.setWordWrap(True)
        self.tone_hint.setStyleSheet(f"color: {theme.DISABLED_TEXT}; font-size: 11px;")
        self._current_layout.addWidget(self.tone_hint)

        # A language may ship only some tones. CalloutHandler already skips what the
        # current language lacks, but silently - the box would stay ticked while a
        # different tone speaks.
        self.callout_selected_lang.currentTextChanged.connect(self._mark_unavailable_tones)
        self._mark_unavailable_tones(self.callout_selected_lang.currentText())

    def _mark_unavailable_tones(self, lang: str):
        """Greys out the tones the selected language has no file for.

        Disabled rather than relabelled: the greyed-out state already carries the meaning,
        where a parenthetical on half the rows is just noise. A disabled QCheckBox keeps
        its check state and still answers isChecked(), so a ticked tone stays in the saved
        mix - the user may well switch back to the language that has it.
        """
        available = self.callout_handler.tones_for(lang)
        unavailable = []
        for tone, checkbox in self.tone_checkboxes.items():
            usable = tone in available
            checkbox.setEnabled(usable)
            checkbox.setToolTip("" if usable else f"No phrases for this tone in {lang} yet.")
            if not usable:
                unavailable.append(self.callout_handler.tone_label(tone))
        # Greyed out alone only says "no". Saying why once under the grid beats a
        # parenthetical on every second row.
        self.tone_hint.setText(
            "" if not unavailable else f"Greyed out: no {lang} phrases yet for {', '.join(unavailable)}."
        )

    def _ticked_tones(self) -> list[str]:
        """Falls back to the default tone rather than saving an empty mix: CalloutHandler
        would fall back at draw time anyway, and this way the dialog doesn't reopen
        claiming no tone is active while one plainly is."""
        ticked = [tone for tone, checkbox in self.tone_checkboxes.items() if checkbox.isChecked()]
        if ticked:
            return ticked
        fallback = [self.callout_handler.DEFAULT_TONE]
        for tone, checkbox in self.tone_checkboxes.items():
            checkbox.setChecked(tone in fallback)
        return fallback

    def _open_phrase_files_dialog(self):
        CustomPhraseFilesDialog(self.callout_handler, parent=self).exec()
