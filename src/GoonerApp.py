import random
import os
import time
from pathlib import Path
from typing import List

from PyQt6.QtCore import Qt, QTimer, QUrl, QSettings
from PyQt6.QtGui import QPixmap, QMovie, QAction
from PyQt6.QtWidgets import (QMainWindow, QWidget, QVBoxLayout,
                             QLabel, QPushButton, QFileDialog, QStackedWidget, QHBoxLayout, QSplitter)
from PyQt6.QtMultimedia import QMediaPlayer, QAudioOutput
from PyQt6.QtMultimediaWidgets import QVideoWidget

from src.BeatHandler import BeatHandler
from src.SettingsDialog import SettingsDialog


class GoonerApp(QMainWindow):
    def __init__(self):
        super().__init__()

        self.settings = QSettings("GoonerCock", "GoonerApp")

        self.setWindowTitle("Auto Hero Generation")
        self.resize(1920, 1080)

        self.current_movie = None

        central_widget = QWidget()
        self.setCentralWidget(central_widget)
        layout = QVBoxLayout(central_widget)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(0)

        self.main_splitter = QSplitter(Qt.Orientation.Vertical)

        media_container = QWidget()
        media_layout = QVBoxLayout(media_container)
        media_layout.setContentsMargins(0, 0, 0, 0)
        media_layout.setSpacing(0)

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

        media_layout.addWidget(self.media_stack, stretch=4)

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

        self.max_dur = float(self.settings.value("GoonerApp/max_dur", 4.0))
        self.min_dur = float(self.settings.value("GoonerApp/min_dur", 0.5))
        self.video_min_dur = float(self.settings.value("GoonerApp/video_min_dur", 1.5))

        media_layout.addWidget(controls_container)
        self.main_splitter.addWidget(media_container)


        self.beat_handler = BeatHandler(settings=self.settings)
        self.main_splitter.addWidget(self.beat_handler.beat_meter)

        self.video_start_time = 0

        self.btn_settings = QPushButton("Settings")

        layout.addWidget(self.main_splitter)
        self.btn_settings.clicked.connect(self.open_settings)

        self.create_menu_bar()

        self.loudness = 1.0

    def create_menu_bar(self):
        menu_bar = self.menuBar()

        settings_menu = menu_bar.addMenu("Settings")

        settings_action = QAction("Change Settings", self)
        settings_action.setShortcut("Ctrl+S")
        settings_action.triggered.connect(self.open_settings)

        settings_menu.addAction(settings_action)

        exit_action = QAction("Quit", self)
        exit_action.setShortcut("Ctrl+Q")
        exit_action.triggered.connect(self.close)
        settings_menu.addAction(exit_action)

    def video_status_changed(self, status):
        if status == QMediaPlayer.MediaStatus.EndOfMedia:
            elapsed_time = time.time() - self.video_start_time
            print(f"Video has been playing for: {elapsed_time}")
            if elapsed_time < self.video_min_dur:
                self.media_player.play()
                return

            self.show_next()

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
                self.beat_handler.start_beat()
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
            self.video_start_time = time.time()
            self.audio_output.setVolume(self.loudness)

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

    # Neue Methode zur GoonerApp-Klasse hinzufügen
    def open_settings(self):
        settings_dialog = SettingsDialog(parent=self)
        settings_dialog.exec()
