import os
import random
import sys
import time
from pathlib import Path

from PyQt6.QtCore import QMutex, QObject, Qt, QTimer, QUrl, pyqtSignal
from PyQt6.QtMultimedia import QSoundEffect
from PyQt6.QtWidgets import QLabel


def get_resource_path(relative_path):
    """ Liefert den absoluten Pfad zur Ressource, passend für Entwicklung und PyInstaller-EXE """
    if hasattr(sys, '_MEIPASS'):
        return os.path.join(sys._MEIPASS, relative_path)
    return os.path.abspath(relative_path)


class BeatHandler(QObject):
    BEAT_PATTERNS_MAP = {
        # --- Standard & Simple ---
        "Standard Beat": [1],

        # --- Grooves & Swings ---
        "Quick Swing": [1, 2, 2, -1, -1],
        "Simple Bounce": [1, 1, -1, 1, 1, -1],
        "Double Tap": [2, 2, -2, 2, 2, -2],
        "Syncopated 4/4": [1, -1, 1, -1, 1, 1, 1],

        # --- Long Pauses & Gaps ---
        "Slow Pulse": [1, 1, -1, -1, -1, -1, -1, -1],
        "Held Breath": [3, -1, -1, -1, -1, -1, 3],
        "Double Tap Pause": [2, 2, -1, -1, -1, -1],

        # --- Complex & Off-Beat ---
        "Delayed Swing": [1, -2, 2, -2, 1, -2, 2, -2],
        "Triple Quick Tap": [1, 4, 4, -2, -2],
        "Missing Third": [1, 1, -3, 1],

        # --- Accelerating & Decelerating ---
        "Build Up": [1, 2, 3, 4, -4, -4],
        "Slow Down": [4, 3, 2, 1, -2],
        "Speed Change": [1, 1, 1, 1, 2, 2, 2, 2],
        "Suspense Build": [2, -4, -3, -2, -1, 3],
    }

    beat_paused_event = pyqtSignal()
    beat_resumed_event = pyqtSignal()
    beat_change_event = pyqtSignal(float, str)
    beat_event = pyqtSignal()

    def __init__(self, beat_file=None, settings=None):
        super().__init__()
        self.beat_changed_counter = 5
        self.just_changed_beat = False
        self.beat_meter_pause_timer = QTimer()
        self.beat_meter_pause_timer.timeout.connect(self.pause_loop)
        self.cur_pause_dur = None
        self.is_red = False

        self.beat_meter = QLabel("Strokemeter appears here.")
        self.beat_meter.setAlignment(Qt.AlignmentFlag.AlignCenter)

        self.footer_style_base = "font-weight: bold; font-size: 24px;"
        self.beat_meter.setStyleSheet(f"background-color: grey; color: white; {self.footer_style_base}")

        self.settings = settings  # QSettings Instanz speichern

        # --- Standardwerte definieren ---
        self.max_beat_dur = 45.0
        self.min_beat_dur = 15
        self.max_beat_freq = 5.0
        self.min_beat_freq = 0.5
        self.min_pause_dur = 5
        self.max_pause_dur = 20
        self.pause_chance = 0.05
        self.beat_change_chance = 0.1

        self.ramping_active = True
        self.min_ramp_duration = 600.0
        self.max_ramp_duration = 1800.0
        self.ramp_window_width = 0.4

        self.session_start_time = 0.0
        self.ramp_target_duration = 0.0

        # --- Laden, falls QSettings existieren ---
        if self.settings:
            self.max_beat_dur = float(self.settings.value("BeatHandler/max_beat_dur", self.max_beat_dur))
            self.min_beat_dur = float(self.settings.value("BeatHandler/min_beat_dur", self.min_beat_dur))
            self.max_beat_freq = float(self.settings.value("BeatHandler/max_beat_freq", self.max_beat_freq))
            self.min_beat_freq = float(self.settings.value("BeatHandler/min_beat_freq", self.min_beat_freq))
            self.min_pause_dur = int(float(self.settings.value("BeatHandler/min_pause_dur", self.min_pause_dur)))
            self.max_pause_dur = int(float(self.settings.value("BeatHandler/max_pause_dur", self.max_pause_dur)))
            self.pause_chance = float(self.settings.value("BeatHandler/pause_chance", self.pause_chance))
            self.beat_change_chance = float(
                self.settings.value("BeatHandler/beat_change_chance", self.beat_change_chance)
            )
            self.ramping_active = bool(
                self.settings.value("BeatHandler/ramping_active", self.ramping_active, type=bool)
            )
            self.min_ramp_duration = float(
                self.settings.value("BeatHandler/min_ramp_duration", self.min_ramp_duration)
            )
            self.max_ramp_duration = float(
                self.settings.value("BeatHandler/max_ramp_duration", self.max_ramp_duration)
            )
            self.ramp_window_width = float(
                self.settings.value("BeatHandler/ramp_window_width", self.ramp_window_width)
            )
            loaded_patterns = self.settings.value("BeatHandler/selected_beat_patterns")
            if loaded_patterns:
                # Das geladene Muster ist eine Liste von Strings (Namen)
                self.selected_beat_patterns = loaded_patterns
            else:
                # Standard: Alle Muster aktiv
                self.selected_beat_patterns = list(self.BEAT_PATTERNS_MAP.keys())

        else:
            self.selected_beat_patterns = list(self.BEAT_PATTERNS_MAP.keys())

        self.beat_meter_timer = QTimer()
        self.beat_meter_timer.timeout.connect(self.beat)
        self.cur_freq = 0
        self.target_beat_dur = 0
        self.cur_beat_start_time = 0

        self.sound_effect = None
        self.beat_loudness = 1.0

        if beat_file is None:
            beat_file = Path(get_resource_path("res/mixkit-cool-interface-click-tone-2568.wav"))
        self.init_beat_sound(str(beat_file.absolute()))

        self.current_beat_pattern = None
        self.current_beat_position = 0
        self.available_beat_patterns = self.BEAT_PATTERNS_MAP
        self.beat_pattern_mutex = QMutex()
        self._pattern_audible_count = 1
        self._pattern_inv_sum = 1.0


    def start_beat(self):
        self.session_start_time = time.time()
        self.ramp_target_duration = random.uniform(self.min_ramp_duration, self.max_ramp_duration)
        self.reset_beat_timer()


    def reset_beat_timer(self):
        if self.cur_freq == 0:  # Choose a frequency. None has been selected yet. This references 1-1-1-1 beats
            self.recalc_beat()
        if self.target_beat_dur < time.time() - self.cur_beat_start_time:
            if random.uniform(0, 1) < self.beat_change_chance:
                # The current beat reached its target duration. Check if a new one should be selected.
                if random.uniform(0, 1) < self.pause_chance:
                    # Pauses can happen at this time.
                    self.start_pause()
                    return
                self.recalc_beat()
        self.beat_pattern_mutex.lock()
        if self._pattern_audible_count > 0:
            base_step_sec = self._pattern_audible_count / (self.cur_freq * self._pattern_inv_sum)
        else:
            base_step_sec = 1 / self.cur_freq  # defensive fallback, no current pattern lacks a beat
        beat_time_ms = int(base_step_sec * 1000 / abs(self.current_beat_pattern[self.current_beat_position]))
        self.current_beat_position = (self.current_beat_position + 1) % len(self.current_beat_pattern)
        self.beat_pattern_mutex.unlock()
        self.beat_meter_timer.start(beat_time_ms)

    def _ramp_progress(self):
        if self.ramp_target_duration <= 0:
            return None  # ramping not initialized (e.g. recalc_beat() called before start_beat())
        elapsed = time.time() - self.session_start_time
        return min(1.0, max(0.0, elapsed / self.ramp_target_duration))

    def _current_freq_range(self):
        corridor = self.max_beat_freq - self.min_beat_freq
        progress = self._ramp_progress()
        if not self.ramping_active or progress is None or corridor <= 0:
            return self.min_beat_freq, self.max_beat_freq
        width = corridor * self.ramp_window_width
        window_min = self.min_beat_freq + progress * (corridor - width)
        return window_min, window_min + width

    def recalc_beat(self):
        window_min, window_max = self._current_freq_range()
        self.cur_freq = random.uniform(window_min, window_max)
        self.cur_beat_start_time = time.time()
        self.target_beat_dur = random.uniform(self.min_beat_dur, self.max_beat_dur)
        self.beat_pattern_mutex.lock()
        self.current_beat_position = 0
        if not isinstance(self.selected_beat_patterns, list):
            self.selected_beat_patterns = list(self.selected_beat_patterns)
        self.current_beat_pattern = self.available_beat_patterns[random.choice(self.selected_beat_patterns)]
        self._pattern_audible_count = sum(1 for v in self.current_beat_pattern if v > 0)
        self._pattern_inv_sum = sum(1 / abs(v) for v in self.current_beat_pattern)
        self.beat_pattern_mutex.unlock()

        # Mark a new beat or speed with a different color for one beat:
        self.beat_meter.setText(f"New Beat! {self.current_beat_pattern}")
        self.beat_meter.setStyleSheet(f"background-color: blue; color: white; {self.footer_style_base}")
        self.just_changed_beat = True
        self.beat_changed_counter = 5
        self.beat_change_event.emit(self.cur_freq, str(self.current_beat_pattern))

    def init_beat_sound(self, file_path):
        self.sound_effect = QSoundEffect()
        self.sound_effect.setSource(QUrl.fromLocalFile(file_path))
        self.sound_effect.setVolume(self.beat_loudness)

    def play_beat_sound(self):
        if not self.sound_effect:
            return
        self.sound_effect.play()

    def toggle_blink(self):
        if self.is_red:
            color = "grey"
            text_col = "white"
            self.beat_meter.setText("UP")
        else:
            color = "red"
            text_col = "yellow"
            self.beat_meter.setText("DOWN")


        self.beat_meter.setStyleSheet(f"background-color: {color}; color: {text_col}; {self.footer_style_base}")
        self.is_red = not self.is_red

    def beat(self):
        self.beat_pattern_mutex.lock()
        play_beat = self.current_beat_pattern[self.current_beat_position] > 0
        self.beat_pattern_mutex.unlock()

        if play_beat:
            self.play_beat_sound()
            if not self.just_changed_beat:
                self.toggle_blink()
            else:
                self.beat_changed_counter -=1
                if self.beat_changed_counter == 0:
                    self.just_changed_beat = False
            self.beat_event.emit()
        self.reset_beat_timer()


    def start_pause(self):
        self.beat_meter_timer.stop()
        self.cur_pause_dur = random.randint(self.min_pause_dur, self.max_pause_dur)
        self.beat_meter_pause_timer.start(1000)
        self.beat_meter.setText(f"Pause: {self.cur_pause_dur} seconds left.")
        self.beat_meter.setStyleSheet(f"background-color: green; color: white; {self.footer_style_base}")
        self.beat_paused_event.emit()
        return


    def pause_loop(self):
        self.cur_pause_dur -= 1
        if self.cur_pause_dur <= 0:
            self.beat_resumed_event.emit()
            self.cur_freq = 0
            self.reset_beat_timer()
            self.beat_meter_pause_timer.stop()
            return
        self.beat_meter_pause_timer.start(1000)
        self.beat_meter.setText(f"Pause: {self.cur_pause_dur} seconds left.")

    def stop(self):
        self.beat_meter_timer.stop()
        self.beat_meter_pause_timer.stop()
        self.beat_meter.setStyleSheet(f"background-color: grey; color: white; {self.footer_style_base}")
        self.beat_meter.setText("Strokemeter appears here.")

    def register_beat_pause_events(self, pause_start_event, pause_resume_event):
        self.beat_paused_event.connect(pause_start_event)
        self.beat_resumed_event.connect(pause_resume_event)

    def register_beat_event(self, handler):
        self.beat_event.connect(handler)

    def register_beat_change_event(self, handler):
        self.beat_change_event.connect(handler)
