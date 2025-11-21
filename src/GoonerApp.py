import random
import os
import time
from pathlib import Path
from typing import List

from PyQt6.QtCore import Qt, QTimer, QUrl
from PyQt6.QtGui import QPixmap, QMovie
from PyQt6.QtWidgets import (QMainWindow, QWidget, QVBoxLayout,
                             QLabel, QPushButton, QFileDialog, QStackedWidget, QHBoxLayout)
from PyQt6.QtMultimedia import QMediaPlayer, QAudioOutput, QSoundEffect
from PyQt6.QtMultimediaWidgets import QVideoWidget


class GoonerApp(QMainWindow):
    def __init__(self):
        super().__init__()

        self.loudness = 1.0
        self.setWindowTitle("Auto Hero Generation")
        self.resize(1920, 1080)

        self.current_movie = None

        central_widget = QWidget()
        self.setCentralWidget(central_widget)
        layout = QVBoxLayout(central_widget)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(0)

        self.media_stack = QStackedWidget()

        self.image_label = QLabel("No Gooning files selected yet.")
        self.image_label.setAlignment(Qt.AlignmentFlag.AlignCenter)

        self.image_label.setMinimumSize(1, 1)
        self.image_label.setStyleSheet("background-color: #222; color: #FFF; font-size: 20px;")
        self.media_stack.addWidget(self.image_label)

        self.video_widget = QVideoWidget()
        self.media_player = QMediaPlayer()
        self.audio_output = QAudioOutput()
        self.media_player.setAudioOutput(self.audio_output)
        self.media_player.setVideoOutput(self.video_widget)
        self.media_stack.addWidget(self.video_widget)

        self.media_player.mediaStatusChanged.connect(self.video_status_changed)
        self.sound_effect = None

        layout.addWidget(self.media_stack, stretch=4)

        self.playlist: List[Path] = []
        self.current_index = 0

        controls_container = QWidget()
        controls_layout = QHBoxLayout(controls_container)

        self.btn_prev = QPushButton("<< Previous")
        self.btn_prev.clicked.connect(self.show_prev)
        self.btn_prev.setEnabled(False)
        self.btn_prev.setShortcut("Left")
        self.btn_prev.setToolTip("Left Arrow Key")

        self.btn_load = QPushButton("Add Gooning Folder.")
        self.btn_load.clicked.connect(self.open_folder)

        self.btn_next = QPushButton("Skip >>")
        self.btn_next.clicked.connect(self.show_next)
        self.btn_next.setEnabled(False)
        self.btn_next.setShortcut("Right")
        self.btn_next.setToolTip("Right Arrow Key")

        controls_layout.addWidget(self.btn_prev)
        controls_layout.addWidget(self.btn_load)
        controls_layout.addWidget(self.btn_next)

        self.auto_play_timer = QTimer()
        self.auto_play_timer.timeout.connect(self.next_img_timer)
        self.max_dur = 4
        self.min_dur = 0.5

        self.beat_meter_timer = QTimer()
        self.beat_meter_timer.timeout.connect(self.beat)
        self.max_beat_dur = 4
        self.min_beat_dur = 0.5
        self.max_beat_freq = 5
        self.min_beat_freq = 0.5
        self.cur_freq = 0
        self.target_beat_dur = 0
        self.cur_beat_start_time = 0
        self.min_pause_dur = 5
        self.max_pause_dur = 20


        layout.addWidget(controls_container)

        self.beat_meter = QLabel("Strokemeter appears here.")
        self.beat_meter.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(self.beat_meter, stretch=1)

        self.footer_style_base = "font-weight: bold; font-size: 24px;"
        self.beat_meter.setStyleSheet(f"background-color: grey; color: white; {self.footer_style_base}")

        self.beat_meter_pause_timer = QTimer()
        self.beat_meter_pause_timer.timeout.connect(self.pause_loop)
        self.cur_pause_dur = None

        self.is_red = False

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
            self.recalc_beat_timer()
            return
        self.beat_meter_pause_timer.start(1000)
        self.beat_meter.setText(f"Pause: {self.cur_pause_dur} seconds left.")

    def video_status_changed(self, status):
        if status == QMediaPlayer.MediaStatus.EndOfMedia:
            self.show_next()

    def beat(self):
        self.toggle_blink()
        self.recalc_beat_timer()
        self.play_wave_file(".\\res\\mixkit-cool-interface-click-tone-2568.wav")


    def next_img_timer(self):
        self.show_next()

    def recalc_autoplay_timer(self):
        self.auto_play_timer.start(int(random.uniform(self.min_dur, self.max_dur) * 1000))

    def finde_unterstützte_dateien(self, verzeichnis_pfad: str) -> List[Path]:
        pfad = Path(verzeichnis_pfad)
        unterstützte_endungen = ['.mp4', '.gif', '.jpeg', '.jpg', '.png']
        gefundene_dateien = []

        for datei in pfad.rglob('*'):
            if datei.is_file() and datei.suffix.lower() in unterstützte_endungen:
                gefundene_dateien.append(datei)
        return gefundene_dateien

    def open_folder(self):
        folder = QFileDialog.getExistingDirectory(self, "Medien-Ordner wählen")
        if folder:
            files = self.finde_unterstützte_dateien(folder)
            if files:
                random.shuffle(files)
                self.playlist = files
                self.current_index = 0
                self.btn_next.setEnabled(True)
                self.btn_prev.setEnabled(True)
                self.load_current_index()

                self.recalc_autoplay_timer()
                self.recalc_beat_timer()
                self.btn_load.setText("Change Gooning Folder.")
            else:
                self.image_label.setText("Keine Dateien gefunden.")
                self.auto_play_timer.stop()

    def show_next(self):
        if not self.playlist: return
        self.current_index = (self.current_index + 1) % len(self.playlist)
        self.load_current_index()

    def show_prev(self):
        if not self.playlist: return
        self.current_index = (self.current_index - 1) % len(self.playlist)
        self.load_current_index()

    def load_current_index(self):
        if not self.playlist: return
        file_path = str(self.playlist[self.current_index])
        self.load_media(file_path)

    def load_media(self, file_path):
        ext = os.path.splitext(file_path)[1].lower()

        self.media_player.stop()
        if self.current_movie:
            self.current_movie.stop()
            self.image_label.setMovie(None)
            self.current_movie = None

        if ext in ['.mp4', '.avi', '.mov', '.mkv']:
            self.auto_play_timer.stop()

            self.media_stack.setCurrentWidget(self.video_widget)
            self.media_player.setSource(QUrl.fromLocalFile(file_path))
            self.media_player.play()
            self.audio_output.setVolume(0.7)

        elif ext == '.gif':
            self.media_stack.setCurrentWidget(self.image_label)

            movie = QMovie(file_path)
            movie.jumpToFrame(0)

            available_size = self.image_label.size()
            if available_size.isValid():
                original_size = movie.currentImage().size()
                scaled_size = original_size.scaled(available_size, Qt.AspectRatioMode.KeepAspectRatio)
                movie.setScaledSize(scaled_size)

            self.image_label.setMovie(movie)
            movie.start()

            self.current_movie = movie
            self.recalc_autoplay_timer()

        elif ext in ['.png', '.jpg', '.jpeg', '.bmp']:
            self.media_stack.setCurrentWidget(self.image_label)
            pixmap = QPixmap(file_path)
            scaled_pixmap = pixmap.scaled(self.image_label.size(),
                                          Qt.AspectRatioMode.KeepAspectRatio,
                                          Qt.TransformationMode.SmoothTransformation)
            self.image_label.setPixmap(scaled_pixmap)
            self.recalc_autoplay_timer()


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

    def play_wave_file(self, file_path):
        if not self.sound_effect:
            self.sound_effect = QSoundEffect()
            self.sound_effect.setSource(QUrl.fromLocalFile(file_path))
            self.sound_effect.setVolume(self.loudness)
        self.sound_effect.play()

    def recalc_beat_timer(self):
        if self.cur_freq == 0:
            self.recalc_beat()
        if self.target_beat_dur < time.time() - self.cur_beat_start_time and random.uniform(0, 1) < 0.1:
            if random.uniform(0, 1) < 0.005:
                self.start_pause()
                return
            self.recalc_beat()
        beat_time_ms = int((1/self.cur_freq)*1000)
        self.beat_meter_timer.start(beat_time_ms)

    def recalc_beat(self):
        self.cur_freq = random.uniform(self.min_beat_freq, self.max_beat_freq)
        self.cur_beat_start_time = time.time()
        self.target_beat_dur = random.uniform(self.min_beat_dur, self.max_beat_dur)

