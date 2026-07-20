import random
import time
from pathlib import Path

from PyQt6.QtCore import QSettings, Qt, QTimer, QUrl, pyqtSignal
from PyQt6.QtGui import QAction, QColor, QDesktopServices, QIcon, QMovie, QPixmap
from PyQt6.QtMultimedia import QAudioOutput, QMediaPlayer
from PyQt6.QtMultimediaWidgets import QVideoWidget
from PyQt6.QtWidgets import (
    QDialog,
    QGraphicsDropShadowEffect,
    QGridLayout,
    QHBoxLayout,
    QLabel,
    QMainWindow,
    QPushButton,
    QSizePolicy,
    QSplitter,
    QStackedWidget,
    QVBoxLayout,
    QWidget,
)

from src import changelog, media_kinds, theme
from src.BeatHandler import BeatHandler
from src.CalloutHandler import CalloutHandler
from src.ClimaxHandler import ClimaxHandler
from src.HelpDialog import HelpDialog
from src.LongTermStatisticsDialog import LongTermStatisticsDialog
from src.MediaFolderPickerDialog import MediaFolderPickerDialog
from src.ScoreTracker import ScoreTracker
from src.SettingsDialog import SettingsDialog
from src.StatisticsDialog import StatisticsDialog
from src.utils import get_current_version, get_project_root
from src.WhatsNewDialog import WhatsNewDialog


class GoonerApp(QMainWindow):
    DISCORD_INVITE_URL = "https://discord.gg/qqkcxvq37Z"

    session_started_event = pyqtSignal()
    session_ended_event = pyqtSignal()
    media_repeated_event = pyqtSignal()
    media_skipped_event = pyqtSignal()

    # Keep in sync with the literal defaults set in __init__ below - single source of truth
    # for the SettingsDialog "Reset to defaults" button.
    DEFAULTS = {
        "min_dur": 0.5,
        "max_dur": 4.0,
        "video_min_dur": 1.5,
        "vid_loudness": 1.0,
        "show_startup_splash": True,
    }

    def __init__(self, settings: QSettings | None = None):
        super().__init__()

        self.settings = settings if settings is not None else QSettings("GoonerCock", "GoonerApp")

        self.setWindowTitle("Auto Hero Generation")

        self.current_movie = None

        project_root = get_project_root()  # Siehe Funktion oben

        icon_path = project_root / 'res' / 'icons' / 'favicon.ico'

        str_icon_path = str(icon_path.resolve())

        if icon_path.exists():
            self.setWindowIcon(QIcon(str_icon_path))
        else:
            print(f"Fehler: Icon nicht gefunden unter: {str_icon_path}")

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
        self.image_label.setStyleSheet(
            f"background-color: {theme.SURFACE_DARK}; color: {theme.TEXT}; font-size: 20px; border-radius: 12px;"
        )
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

        self.callout_label.setStyleSheet(f"""
                    color: {theme.ACCENT};
                    font-size: 24px;
                    padding: 8px;
                    background-color: rgba(45, 29, 58, 0.9);
                    border-radius: 10px;
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

        self.playlist: list[Path] = []
        self.current_index = 0

        self.controls_container = QWidget()
        controls_layout = QHBoxLayout(self.controls_container)

        self.btn_prev = QPushButton("<< Previous")
        self.btn_prev.clicked.connect(self.btn_prev_action)
        self.btn_prev.setEnabled(False)
        self.btn_prev.setShortcut("Left")
        self.btn_prev.setToolTip("Left Arrow Key")

        self.btn_load = QPushButton("Set Gooning Folder and Start.")
        self.btn_load.setObjectName("primary")
        self.btn_load.clicked.connect(self.open_folder)
        self.btn_load.setShortcut("Ctrl+O")
        self.btn_load.setToolTip("Ctrl+O")
        btn_load_glow = QGraphicsDropShadowEffect()
        btn_load_glow.setColor(QColor(theme.ACCENT))
        btn_load_glow.setBlurRadius(50)
        btn_load_glow.setOffset(0, 0)
        self.btn_load.setGraphicsEffect(btn_load_glow)

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

        self.btn_mute = QPushButton("Mute")
        self.btn_mute.setCheckable(True)
        self.btn_mute.clicked.connect(self.set_muted)
        self.btn_mute.setToolTip("M")

        # Space triggers Panic (see keyPressEvent) - QPushButton intercepts Space/Enter for
        # whichever button currently has keyboard focus before it ever reaches keyPressEvent,
        # so Panic would silently fail to fire while any of these had focus. NoFocus keeps them
        # mouse/shortcut-clickable but out of the keyboard-focus chain entirely.
        for button in (self.btn_prev, self.btn_load, self.btn_next, self.btn_stop, self.btn_mute):
            button.setFocusPolicy(Qt.FocusPolicy.NoFocus)

        controls_layout.addWidget(self.btn_prev)
        controls_layout.addWidget(self.btn_load)
        controls_layout.addWidget(self.btn_stop)
        controls_layout.addWidget(self.btn_next)
        controls_layout.addWidget(self.btn_mute)

        self.auto_play_timer = QTimer()
        self.auto_play_timer.timeout.connect(self.next_img_timer)

        self.max_dur = float(self.settings.value("GoonerApp/max_dur", 4.0))
        self.min_dur = float(self.settings.value("GoonerApp/min_dur", 0.5))
        self.video_min_dur = float(self.settings.value("GoonerApp/video_min_dur", 1.5))
        self.show_startup_splash = bool(
            self.settings.value("GoonerApp/show_startup_splash", self.DEFAULTS["show_startup_splash"], type=bool)
        )

        media_layout.addWidget(self.controls_container)
        self.main_splitter.addWidget(media_container)

        self.beat_handler = BeatHandler(settings=self.settings)

        self.climax_status_label = QLabel("")
        self.climax_status_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.climax_status_label.setStyleSheet(self._climax_label_style("transparent"))
        self.climax_status_label.hide()
        climax_glow = QGraphicsDropShadowEffect()
        climax_glow.setColor(QColor(theme.ACCENT))
        climax_glow.setBlurRadius(30)
        climax_glow.setOffset(0, 0)
        self.climax_status_label.setGraphicsEffect(climax_glow)

        self.climax_blink_timer = QTimer()
        self.climax_blink_timer.timeout.connect(self._toggle_climax_blink)
        self._climax_blink_on = False
        self._climax_status_text = ""
        self._climax_status_colors = ("transparent", "transparent")

        self.beat_meter = QLabel("Strokemeter appears here.")
        self.beat_meter.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.beat_meter.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)
        background, color = self.BEAT_METER_COLORS["idle"]
        self.beat_meter.setStyleSheet(self._beat_meter_style(background, color))
        self.beat_handler.register_beat_meter_update_event(self._update_beat_meter)

        # Fixed total height so the media area above never wobbles when the climax label
        # appears/disappears - only the split *within* this container changes (beat_meter
        # expands to fill it via stretch when the label is hidden, shrinks when it's shown).
        self.footer_container = QWidget()
        self.footer_container.setFixedHeight(110)
        self.footer_layout = QVBoxLayout(self.footer_container)
        self.footer_layout.setContentsMargins(0, 0, 0, 0)
        self.footer_layout.setSpacing(0)
        self.footer_layout.addWidget(self.climax_status_label, stretch=0)
        self.footer_layout.addWidget(self.beat_meter, stretch=1)
        self.main_splitter.addWidget(self.footer_container)

        self.video_start_time = 0

        self.btn_settings = QPushButton("Settings")

        self.main_splitter.setSizes([950, 50, 50])

        layout.addWidget(self.main_splitter)
        self.btn_settings.clicked.connect(self.open_settings)

        self.create_menu_bar()

        self.vid_loudness = 1.0

        self.is_running = False
        self._was_maximized_before_fullscreen = False
        self.is_muted = False

        self.callout_handler = CalloutHandler(self.settings)

        self.score_tracker = ScoreTracker(settings=self.settings)

        self.climax_handler = ClimaxHandler(self.beat_handler, self.callout_handler, settings=self.settings)

        self._setup_signal_handler()

    def keyPressEvent(self, event):
        if event.key() == Qt.Key.Key_F or event.key() == Qt.Key.Key_F11:
            self._toggle_fullscreen()
        elif event.key() == Qt.Key.Key_Escape:
            self._leave_fullscreen()
        elif event.key() == Qt.Key.Key_Space:
            self.panic()
        elif event.key() == Qt.Key.Key_M:
            self.toggle_mute()
        else:
            super().keyPressEvent(event)

    def _toggle_fullscreen(self):
        if self.isFullScreen():
            self._leave_fullscreen()
        else:
            self._enter_fullscreen()

    def _enter_fullscreen(self):
        if not self.isFullScreen():
            self._was_maximized_before_fullscreen = self.isMaximized()
            self.controls_container.hide()
            self.showFullScreen()

    def _leave_fullscreen(self):
        if self.isFullScreen():
            self.controls_container.show()
            if self._was_maximized_before_fullscreen:
                self.showMaximized()
            else:
                self.showNormal()

    def panic(self):
        """Instant hide-and-silence: minimizes the window and mutes audio in one keypress.
        Deliberately does not stop/pause the session (see Ctrl+Space) or auto-unmute on
        restore - the user decides when sound comes back, same as toggling Mute normally."""
        self.set_muted(True)
        self.showMinimized()

    def set_muted(self, muted: bool):
        self.is_muted = muted
        self.audio_output.setMuted(muted)
        self.beat_handler.set_muted(muted)
        self.btn_mute.setChecked(muted)
        self.btn_mute.setText("Unmute" if muted else "Mute")

    def toggle_mute(self):
        self.set_muted(not self.is_muted)

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

        self.beat_handler.register_beat_change_event(self.climax_handler.on_beat_change)
        self.climax_handler.register_outcome_event(self.score_tracker.climax_decided)
        self.climax_handler.register_outcome_event(self._on_climax_outcome)
        self.climax_handler.register_status_event(self._update_climax_status_label)
        self.climax_handler.register_fake_climax_event(self.score_tracker.fake_climax_triggered)

        self.register_start_event(self.score_tracker.session_started)
        self.register_start_event(self.callout_handler.session_started)
        self.register_start_event(self.climax_handler.session_started)

        self.register_end_event(self.score_tracker.session_ended)

        self.register_media_skip_event(self.score_tracker.media_skipped)
        self.register_media_skip_event(self.callout_handler.media_skipped)

        self.register_media_repeat_event(self.score_tracker.media_repeated)
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

        help_menu = menu_bar.addMenu("Help")

        whats_new_action = QAction("What's New", self)
        whats_new_action.triggered.connect(self.show_whats_new_dialog)
        help_menu.addAction(whats_new_action)

        guide_action = QAction("Guide", self)
        guide_action.setShortcut("F1")
        guide_action.triggered.connect(self.show_help_dialog)
        help_menu.addAction(guide_action)

        stats_menu = menu_bar.addMenu("Statistics")

        long_term_stats_action = QAction("Long-term Statistics", self)
        long_term_stats_action.triggered.connect(self.show_long_term_statistics)
        stats_menu.addAction(long_term_stats_action)

        socials_menu = menu_bar.addMenu("Socials")

        discord_action = QAction("Join Discord", self)
        discord_action.triggered.connect(self.open_discord_invite)
        socials_menu.addAction(discord_action)

    def maybe_show_whats_new_on_startup(self):
        current_version = get_current_version()
        last_seen_version = str(self.settings.value("GoonerApp/last_seen_version", ""))
        entries = changelog.entries_since(last_seen_version, current_version)
        if entries:
            dialog = WhatsNewDialog(entries, parent=self)
            dialog.exec()
        self.settings.setValue("GoonerApp/last_seen_version", current_version)

    def show_whats_new_dialog(self):
        dialog = WhatsNewDialog(changelog.CHANGELOG, parent=self)
        dialog.exec()

    def show_help_dialog(self):
        dialog = HelpDialog(parent=self)
        dialog.exec()

    def open_discord_invite(self):
        QDesktopServices.openUrl(QUrl(self.DISCORD_INVITE_URL))

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

    def finde_unterstützte_dateien(self, verzeichnis_pfad: str) -> list[Path]:
        return media_kinds.find_supported_files(verzeichnis_pfad)

    def open_folder(self):
        dialog = MediaFolderPickerDialog(parent=self)
        if dialog.exec() == QDialog.DialogCode.Accepted:
            self._update_climax_status_label("neutral")
            files = dialog.selected_files
            if files:
                random.shuffle(files)
                self.playlist = files
                self.current_index = 0
                self.start()
            else:
                self.image_label.setText("Keine Dateien gefunden.")
                self.stop()

    def show_next(self):
        if not self.playlist:
            return
        self.current_index = (self.current_index + 1) % len(self.playlist)
        self.load_current_index()

    def show_prev(self):
        if not self.playlist:
            return
        self.current_index = (self.current_index - 1) % len(self.playlist)
        self.load_current_index()

    def load_current_index(self):
        if not self.playlist:
            return
        file_path = str(self.playlist[self.current_index])
        self.load_media(file_path)

    def load_media(self, file_path):
        kind = media_kinds.media_kind(file_path)

        self.media_player.stop()
        if self.current_movie:
            self.current_movie.stop()
            self.image_label.setMovie(None)
            self.current_movie = None

        if kind == "video":
            self.auto_play_timer.stop()

            self.media_stack.setCurrentWidget(self.video_widget)
            self.media_player.setSource(QUrl.fromLocalFile(file_path))
            self.media_player.play()
            self.video_start_time = time.time()
            self.audio_output.setVolume(self.vid_loudness)

        elif kind == "gif":
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

        elif kind == "image":
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
            self._freeze_climax_blink()
            self.session_ended_event.emit()
            self.show_statistics()

    def start(self):
        if not self.is_running:
            self.session_started_event.emit()
            self.btn_next.setEnabled(True)
            self.btn_prev.setEnabled(True)
            self.btn_stop.setEnabled(True)
            self.beat_handler.start_beat()
            self.is_running = True
            self.btn_load.setText("Change Gooning Folder.")
        self.load_current_index()
        self.recalc_autoplay_timer()

    def _on_climax_outcome(self, outcome):
        if outcome == "denied":
            QTimer.singleShot(5000, self.stop)

    CLIMAX_STATUS_COLORS = {
        "cum": (theme.ACCENT, theme.ACCENT_HOVER),
        "ruined": (theme.RUINED, theme.RUINED_DIM),
        "denied": (theme.DENIED, theme.DENIED_DIM),
    }

    def _climax_label_style(self, background):
        return (
            f"font-size: 28px; font-weight: bold; padding: 10px; color: white; "
            f"background-color: {background}; border-radius: 12px;"
        )

    def _update_climax_status_label(self, status):
        if status not in self.CLIMAX_STATUS_COLORS:
            self.climax_blink_timer.stop()
            self._climax_status_text = ""
            self.climax_status_label.setText("")
            self.climax_status_label.setStyleSheet(self._climax_label_style("transparent"))
            self.climax_status_label.hide()
            return
        self._climax_status_text = status.upper()
        self._climax_status_colors = self.CLIMAX_STATUS_COLORS[status]
        self._climax_blink_on = True
        self.climax_status_label.setText(self._climax_status_text)
        self.climax_status_label.setStyleSheet(self._climax_label_style(self._climax_status_colors[0]))
        self.climax_status_label.show()
        self.climax_blink_timer.start(100)

    def _toggle_climax_blink(self):
        self._climax_blink_on = not self._climax_blink_on
        color = self._climax_status_colors[0 if self._climax_blink_on else 1]
        self.climax_status_label.setStyleSheet(self._climax_label_style(color))

    def _freeze_climax_blink(self):
        """Stops the blink but keeps the banner visible with its last outcome - used on
        Stop, where the result should stay readable rather than disappear or flash forever."""
        self.climax_blink_timer.stop()
        if self._climax_status_text:
            self.climax_status_label.setStyleSheet(self._climax_label_style(self._climax_status_colors[0]))

    BEAT_METER_COLORS = {
        "idle": (theme.SECONDARY, theme.TEXT),
        "up": (theme.SECONDARY, theme.TEXT),
        "down": (theme.ACCENT, theme.BACKGROUND),
        "new_beat": (theme.ACCENT, theme.BACKGROUND),
        "pause": (theme.PAUSE, theme.TEXT),
    }

    def _beat_meter_style(self, background, color):
        return (
            f"background-color: {background}; color: {color}; "
            "font-weight: bold; font-size: 24px; border-radius: 8px;"
        )

    def _update_beat_meter(self, text, kind):
        self.beat_meter.setText(text)
        background, color = self.BEAT_METER_COLORS[kind]
        self.beat_meter.setStyleSheet(self._beat_meter_style(background, color))

    def register_start_event(self, handler):
        self.session_started_event.connect(handler)

    def register_end_event(self, handler):
        self.session_ended_event.connect(handler)

    def register_media_skip_event(self, handler):
        self.media_skipped_event.connect(handler)

    def register_media_repeat_event(self, handler):
        self.media_repeated_event.connect(handler)

    def show_statistics(self):
        dialog = StatisticsDialog(
            self.score_tracker.deliver_infos(),
            new_records=self.score_tracker.last_session_new_records,
            parent=self,
        )
        dialog.exec()

    def show_long_term_statistics(self):
        dialog = LongTermStatisticsDialog(
            self.score_tracker.get_history(),
            self.score_tracker.get_all_time_bests(),
            parent=self,
        )
        dialog.exec()
