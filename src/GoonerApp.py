import random
import os
import time
from pathlib import Path
from typing import List

from PyQt6.QtCore import Qt, QTimer, QUrl, QSettings, pyqtSignal
from PyQt6.QtGui import QPixmap, QMovie, QAction
from PyQt6.QtWidgets import (QMainWindow, QWidget, QVBoxLayout,
                             QLabel, QPushButton, QFileDialog, QStackedWidget, QHBoxLayout, QSplitter, QGridLayout)
from PyQt6.QtMultimedia import QMediaPlayer, QAudioOutput
from PyQt6.QtMultimediaWidgets import QVideoWidget

from src.BeatHandler import BeatHandler
from src.CalloutHandler import CalloutHandler
from src.ScoreTracker import ScoreTracker
from src.SettingsDialog import SettingsDialog
from src.StatisticsDialog import StatisticsDialog


class GoonerApp(QMainWindow):
    session_started_event = pyqtSignal()
    session_ended_event = pyqtSignal()
    media_repeated_event = pyqtSignal()
    media_skipped_event = pyqtSignal()

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

        self.callout_label = QLabel("")
        self.callout_label.setWordWrap(True)
        self.callout_label.setAlignment(Qt.AlignmentFlag.AlignBottom | Qt.AlignmentFlag.AlignCenter)

        self.callout_label.setStyleSheet("""
                    color: #FF69B4;
                    font-size: 24px;
                    padding: 5px;
                    background-color: rgba(0, 0, 0, 0.8);
                    border-radius: 5px;
                """)
        self.callout_label.hide()

        self.overlay_widget = QWidget()
        self.overlay_layout = QGridLayout(self.overlay_widget)
        self.overlay_layout.setContentsMargins(0, 0, 0, 0)
        self.overlay_layout.setSpacing(0)

        self.overlay_layout.addWidget(self.media_stack, 0, 0)

        self.overlay_layout.addWidget(
            self.callout_label,
            0, 0,
            Qt.AlignmentFlag.AlignBottom | Qt.AlignmentFlag.AlignCenter
        )

        media_layout.addWidget(self.overlay_widget, stretch=4)

        self.playlist: List[Path] = []
        self.current_index = 0

        controls_container = QWidget()
        controls_layout = QHBoxLayout(controls_container)

        self.btn_prev = QPushButton("<< Previous")
        self.btn_prev.clicked.connect(self.btn_prev_action)
        self.btn_prev.setEnabled(False)
        self.btn_prev.setShortcut("Left")
        self.btn_prev.setToolTip("Left Arrow Key")

        self.btn_load = QPushButton("Set Gooning Folder and Start.")
        self.btn_load.clicked.connect(self.open_folder)

        self.btn_next = QPushButton("Skip >>")
        self.btn_next.clicked.connect(self.btn_next_action)
        self.btn_next.setEnabled(False)
        self.btn_next.setShortcut("Right")
        self.btn_next.setToolTip("Right Arrow Key")

        self.btn_stop = QPushButton("Stop")
        self.btn_stop.clicked.connect(self.stop)
        self.btn_stop.setEnabled(False)
        self.btn_stop.setShortcut("Ctrl+Space")
        self.btn_stop.setToolTip("Ctrl+Space")

        controls_layout.addWidget(self.btn_prev)
        controls_layout.addWidget(self.btn_load)
        controls_layout.addWidget(self.btn_stop)
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

        self.vid_loudness = 1.0

        self.is_running = False

        self.callout_handler = CalloutHandler(self.settings)

        self.score_tracker = ScoreTracker()

        self._setup_signal_handler()

    def display_new_tease(self, tease: str):
        self.callout_label.setText(tease)
        self.callout_label.show()

    def hide_last_tease(self):
        self.callout_label.hide()
        self.callout_label.setText("")

    def _setup_signal_handler(self):
        self.beat_handler.register_beat_pause_events(self.score_tracker.beat_paused, self.score_tracker.beat_resumed)
        self.beat_handler.register_beat_pause_events(self.callout_handler.pause_started,
                                                     self.callout_handler.pause_ended)

        self.beat_handler.register_beat_event(self.score_tracker.beat)

        self.beat_handler.register_beat_change_event(self.score_tracker.beat_changed)
        self.beat_handler.register_beat_change_event(self.callout_handler.beat_change_general)

        self.register_start_event(self.score_tracker.session_started)
        self.register_start_event(self.callout_handler.session_started)

        self.register_end_event(self.score_tracker.session_ended)

        self.register_media_skip_event(self.callout_handler.media_skipped)

        self.register_media_repeat_event(self.callout_handler.media_repeated)

        self.callout_handler.register_new_tease_event(self.display_new_tease, self.hide_last_tease)

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

    def btn_next_action(self):
        self.show_next()
        self.media_skipped_event.emit()

    def btn_prev_action(self):
        self.show_prev()
        self.media_repeated_event.emit()

    def video_status_changed(self, status):
        if status == QMediaPlayer.MediaStatus.EndOfMedia:
            elapsed_time = time.time() - self.video_start_time
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
                self.start()
            else:
                self.image_label.setText("Keine Dateien gefunden.")
                self.stop()

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
            self.audio_output.setVolume(self.vid_loudness)

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

    def stop(self):
        if self.is_running:
            self.auto_play_timer.stop()
            self.beat_handler.stop()
            self.btn_load.setText("Set Gooning Folder and Start.")
            self.is_running = False
            self.btn_next.setEnabled(False)
            self.btn_prev.setEnabled(False)
            self.btn_stop.setEnabled(False)
            self.session_ended_event.emit()
            self.show_statistics()

    def start(self):
        if not self.is_running:
            self.btn_next.setEnabled(True)
            self.btn_prev.setEnabled(True)
            self.btn_stop.setEnabled(True)
            self.beat_handler.start_beat()
            self.is_running = True
            self.btn_load.setText("Change Gooning Folder.")
            self.session_started_event.emit()
        self.load_current_index()
        self.recalc_autoplay_timer()

    def register_start_event(self, handler):
        self.session_started_event.connect(handler)

    def register_end_event(self, handler):
        self.session_ended_event.connect(handler)

    def register_media_skip_event(self, handler):
        self.media_skipped_event.connect(handler)

    def register_media_repeat_event(self, handler):
        self.media_repeated_event.connect(handler)

    def show_statistics(self):
        dialog = StatisticsDialog(self.score_tracker.deliver_infos(), parent=self)
        dialog.exec()
