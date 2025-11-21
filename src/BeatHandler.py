import random
import time

from PyQt6.QtCore import Qt, QTimer, QUrl, QMutex
from PyQt6.QtMultimedia import QSoundEffect
from PyQt6.QtWidgets import QLabel


class BeatHandler:
    BEAT_PATTERNS_MAP = {
        # --- Standard & Simple ---
        "Standard Beat": [1],

        # --- Grooves & Swings ---
        "Quick Swing": [1, 2, 2, -1, -1],
        "Simple Bounce": [1, 1, -1, 1, 1, -1],
        "Double Tap": [2, 2, -2, 2, 2, -2],
        "Syncopated 4/4": [1, -1, 1, -1, 1, 1, 1],

        # --- Long Pauses & Gaps ---
        "Long Rest": [1, -2, -2, -2],
        "Double Tap Pause": [2, 2, -1, -1, -1, -1],

        # --- Complex & Off-Beat ---
        "Delayed Swing": [1, -2, 2, -2, 1, -2, 2, -2],
        "Triple Quick Tap": [1, 4, 4, -2, -2],
        "Missing Third": [1, 1, -3, 1],

        # --- Accelerating & Decelerating ---
        "Build Up": [1, 2, 3, 4, -4, -4],
        "Slow Down": [4, 3, 2, 1, -2],
        "Speed Change": [1, 1, 1, 1, 2, 2, 2, 2],
    }

    def __init__(self, beat_file=".\\res\\mixkit-cool-interface-click-tone-2568.wav"):
        self.beat_meter_pause_timer = QTimer()
        self.beat_meter_pause_timer.timeout.connect(self.pause_loop)
        self.cur_pause_dur = None
        self.is_red = False

        self.beat_meter = QLabel("Strokemeter appears here.")
        self.beat_meter.setAlignment(Qt.AlignmentFlag.AlignCenter)

        self.footer_style_base = "font-weight: bold; font-size: 24px;"
        self.beat_meter.setStyleSheet(f"background-color: grey; color: white; {self.footer_style_base}")

        self.beat_meter_timer = QTimer()
        self.beat_meter_timer.timeout.connect(self.beat)
        self.max_beat_dur = 50
        self.min_beat_dur = 15
        self.max_beat_freq = 5
        self.min_beat_freq = 0.5
        self.cur_freq = 0
        self.target_beat_dur = 0
        self.cur_beat_start_time = 0
        self.min_pause_dur = 5
        self.max_pause_dur = 20

        self.sound_effect = None
        self.loudness = 1.0

        self.init_beat_sound(beat_file)

        self.current_beat_pattern = None
        self.current_beat_position = 0
        self.available_beat_patterns = self.BEAT_PATTERNS_MAP
        self.selected_beat_patterns = self.available_beat_patterns.keys()
        self.beat_pattern_mutex = QMutex()


    def start_beat(self):
        self.reset_beat_timer()


    def reset_beat_timer(self):
        if self.cur_freq == 0:  # Choose a frequency. None has been selected yet. This references 1-1-1-1 beats
            self.recalc_beat()
        if self.target_beat_dur < time.time() - self.cur_beat_start_time:
            if random.uniform(0, 1) < 0.1:
                # The current beat reached its target duration. Check if a new one should be selected.
                if random.uniform(0, 1) < 0.005:
                    # Pauses can happen at this time.
                    self.start_pause()
                    return
                self.recalc_beat()
        self.beat_pattern_mutex.lock()
        beat_time_ms = int((1/self.cur_freq)*1000/abs(self.current_beat_pattern[self.current_beat_position]))
        self.current_beat_position = (self.current_beat_position + 1) % len(self.current_beat_pattern)
        self.beat_pattern_mutex.unlock()
        self.beat_meter_timer.start(beat_time_ms)

    def recalc_beat(self):
        self.cur_freq = random.uniform(self.min_beat_freq, self.max_beat_freq)
        self.cur_beat_start_time = time.time()
        self.target_beat_dur = random.uniform(self.min_beat_dur, self.max_beat_dur)
        self.beat_pattern_mutex.lock()
        self.current_beat_position = 0
        self.current_beat_pattern = self.available_beat_patterns[random.choice(self.selected_beat_patterns)]
        self.beat_pattern_mutex.unlock()

    def init_beat_sound(self, file_path):
        self.sound_effect = QSoundEffect()
        self.sound_effect.setSource(QUrl.fromLocalFile(file_path))
        self.sound_effect.setVolume(self.loudness)

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
            self.toggle_blink()
        self.reset_beat_timer()


    def start_pause(self):
        self.beat_meter_timer.stop()
        self.cur_pause_dur = random.randint(self.min_pause_dur, self.max_pause_dur)
        self.beat_meter_pause_timer.start(1000)
        self.beat_meter.setText(f"Pause: {self.cur_pause_dur} seconds left.")
        self.beat_meter.setStyleSheet(f"background-color: green; color: white; {self.footer_style_base}")
        return


    def pause_loop(self):
        self.cur_pause_dur -= 1
        if self.cur_pause_dur <= 0:
            self.cur_freq = 0
            self.reset_beat_timer()
            return
        self.beat_meter_pause_timer.start(1000)
        self.beat_meter.setText(f"Pause: {self.cur_pause_dur} seconds left.")