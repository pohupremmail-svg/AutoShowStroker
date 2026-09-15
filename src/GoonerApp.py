import random
import time
from pathlib import Path

from PyQt6.QtCore import QSettings, Qt, QTimer, QUrl, pyqtSignal
from PyQt6.QtGui import QAction, QColor, QDesktopServices, QIcon, QMovie
from PyQt6.QtMultimedia import QAudioOutput, QMediaPlayer
from PyQt6.QtMultimediaWidgets import QVideoWidget
from PyQt6.QtWidgets import (
    QDialog,
    QGraphicsDropShadowEffect,
    QGridLayout,
    QHBoxLayout,
    QLabel,
    QMainWindow,
    QMessageBox,
    QPushButton,
    QSplitter,
    QStackedWidget,
    QVBoxLayout,
    QWidget,
)

from src import applog, changelog, media_kinds, theme
from src.BeatHandler import BeatHandler
from src.BeatTrackWidget import BeatTrackWidget
from src.CalloutHandler import CalloutHandler
from src.ClimaxHandler import ClimaxHandler
from src.HelpDialog import HelpDialog
from src.IntifaceController import IntifaceController
from src.MediaFolderPickerDialog import MediaFolderPickerDialog
from src.PrivacyDataDialog import PrivacyDataDialog
from src.ScoreTracker import ScoreTracker
from src.SessionRecorder import SessionRecorder
from src.SettingsDialog import SettingsDialog
from src.StatisticsDialog import StatisticsDialog
from src.UpdateChecker import UpdateChecker
from src.user_data import UserDataStore
from src.utils import format_clock, get_current_version, get_project_root, load_scaled_pixmap
from src.WhatsNewDialog import WhatsNewDialog

# How long the "denied" banner stays up before the session is ended for the user.
DENIED_STOP_DELAY_MS = 5000
# Delay before skipping past media that will never finish playing - long enough that an
# entirely unplayable playlist cycles visibly instead of spinning.
MEDIA_ERROR_ADVANCE_MS = 1000

log = applog.get_logger(__name__)


class GoonerApp(QMainWindow):
    SETTINGS_GROUP = "GoonerApp"  # see BeatHandler.SETTINGS_GROUP

    DISCORD_INVITE_URL = "https://discord.gg/qqkcxvq37Z"

    session_started_event = pyqtSignal()
    session_ended_event = pyqtSignal()
    media_repeated_event = pyqtSignal()
    media_skipped_event = pyqtSignal()
    media_shown_event = pyqtSignal(str)

    # Single source of truth: __init__ reads these as its fallbacks, and the
    # SettingsDialog "Reset to defaults" buttons read the same dict.
    DEFAULTS = {
        "min_dur": 0.5,
        "max_dur": 4.0,
        "video_min_dur": 1.5,
        "vid_loudness": 1.0,
        "show_startup_splash": True,
        "show_record_chase": True,
        "show_session_timer": True,
        "diagnostic_log": False,
        "diagnostic_log_level": applog.DEFAULT_LEVEL,
    }

    def __init__(self, settings: QSettings | None = None, data_store=None):
        super().__init__()

        self.settings = settings if settings is not None else QSettings("GoonerCock", "GoonerApp")
        self.data_store = data_store if data_store is not None else UserDataStore()
        # Configured before anything else runs, so the very first warnings (a failed
        # migration, a missing callout directory) land in the log rather than being lost.
        self.diagnostic_log = bool(
            self.settings.value("GoonerApp/diagnostic_log", self.DEFAULTS["diagnostic_log"], type=bool)
        )
        self.diagnostic_log_level = str(
            self.settings.value("GoonerApp/diagnostic_log_level", self.DEFAULTS["diagnostic_log_level"])
        )
        applog.configure(self.diagnostic_log, self.data_store.base_dir, self.diagnostic_log_level)
        log.info("GoonerApp %s starting", get_current_version())
        self.data_store.migrate_legacy_location()
        self.data_store.prune_legacy_registry_keys(self.settings)

        self.setWindowTitle("Auto Hero Generation")

        self.current_movie = None

        project_root = get_project_root()

        icon_path = project_root / 'res' / 'icons' / 'favicon.ico'

        str_icon_path = str(icon_path.resolve())

        if icon_path.exists():
            self.setWindowIcon(QIcon(str_icon_path))
        else:
            log.warning("Window icon not found at %s", str_icon_path)

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
        self.media_player.errorOccurred.connect(self._on_media_error)

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

        self.record_chase_label = QLabel("")
        self.record_chase_label.setStyleSheet(f"""
                    color: {theme.ACCENT};
                    font-size: 13px;
                    font-weight: bold;
                    padding: 6px 10px;
                    background-color: rgba(45, 29, 58, 0.85);
                    border-radius: 8px;
                """)
        record_chase_glow = QGraphicsDropShadowEffect()
        record_chase_glow.setColor(QColor(theme.ACCENT))
        record_chase_glow.setBlurRadius(18)
        record_chase_glow.setOffset(0, 0)
        self.record_chase_label.setGraphicsEffect(record_chase_glow)
        self.record_chase_label.hide()

        self.session_timer_label = QLabel("")
        self.session_timer_label.setStyleSheet(f"""
                    color: {theme.ACCENT};
                    font-size: 13px;
                    font-weight: bold;
                    padding: 6px 10px;
                    background-color: rgba(45, 29, 58, 0.85);
                    border-radius: 8px;
                """)
        session_timer_glow = QGraphicsDropShadowEffect()
        session_timer_glow.setColor(QColor(theme.ACCENT))
        session_timer_glow.setBlurRadius(18)
        session_timer_glow.setOffset(0, 0)
        self.session_timer_label.setGraphicsEffect(session_timer_glow)
        self.session_timer_label.hide()

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

        self.overlay_layout.addWidget(
            self.record_chase_label,
            0, 0,
            Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignRight
        )

        self.overlay_layout.addWidget(
            self.session_timer_label,
            0, 0,
            Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignLeft
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

        # Held rather than a fire-and-forget QTimer.singleShot so start() can cancel it -
        # and so a test can check it without monkeypatching QTimer itself.
        self._denied_stop_timer = QTimer(self)
        self._denied_stop_timer.setSingleShot(True)
        self._denied_stop_timer.timeout.connect(self.stop)

        self.session_timer_tick = QTimer()
        self.session_timer_tick.timeout.connect(self._update_session_timer)

        # Fallbacks come from DEFAULTS, not from repeated literals - these three used to
        # carry their own copies of 4.0/0.5/1.5 while the lines right below already read
        # the dict.
        self.max_dur = float(self.settings.value("GoonerApp/max_dur", self.DEFAULTS["max_dur"]))
        self.min_dur = float(self.settings.value("GoonerApp/min_dur", self.DEFAULTS["min_dur"]))
        self.video_min_dur = float(
            self.settings.value("GoonerApp/video_min_dur", self.DEFAULTS["video_min_dur"])
        )
        self.show_startup_splash = bool(
            self.settings.value("GoonerApp/show_startup_splash", self.DEFAULTS["show_startup_splash"], type=bool)
        )
        self.show_record_chase = bool(
            self.settings.value("GoonerApp/show_record_chase", self.DEFAULTS["show_record_chase"], type=bool)
        )
        self.show_session_timer = bool(
            self.settings.value("GoonerApp/show_session_timer", self.DEFAULTS["show_session_timer"], type=bool)
        )

        media_layout.addWidget(self.controls_container)
        self.main_splitter.addWidget(media_container)

        self.beat_handler = BeatHandler(settings=self.settings, data_store=self.data_store)

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

        self.beat_track = BeatTrackWidget(self.beat_handler)
        self.beat_track.set_status("Strokemeter appears here.", "idle")
        self.beat_handler.register_beat_meter_update_event(self._update_beat_track)

        # Fixed total height so the media area above never wobbles when the climax label
        # appears/disappears - only the split *within* this container changes (beat_meter
        # expands to fill it via stretch when the label is hidden, shrinks when it's shown).
        self.footer_container = QWidget()
        self.footer_container.setFixedHeight(110)
        self.footer_layout = QVBoxLayout(self.footer_container)
        self.footer_layout.setContentsMargins(0, 0, 0, 0)
        self.footer_layout.setSpacing(0)
        self.footer_layout.addWidget(self.climax_status_label, stretch=0)
        self.footer_layout.addWidget(self.beat_track, stretch=1)
        self.main_splitter.addWidget(self.footer_container)

        self.video_start_time = 0


        # No setSizes() here: footer_container is setFixedHeight(110) above, so the
        # splitter cannot size it at all and any numbers here would be inert.

        layout.addWidget(self.main_splitter)

        self.create_menu_bar()

        self.vid_loudness = self.DEFAULTS["vid_loudness"]
        if self.settings:
            self.vid_loudness = float(self.settings.value("GoonerApp/vid_loudness", self.vid_loudness))

        self.is_running = False
        self._was_maximized_before_fullscreen = False
        self.is_muted = False

        self.callout_handler = CalloutHandler(self.settings, data_store=self.data_store)

        self.score_tracker = ScoreTracker(settings=self.settings, data_store=self.data_store)
        # Keeps the running session's timeline for the Session Explorer. In memory only -
        # it holds media paths, which never go near the data directory. See SessionRecorder.
        self.session_recorder = SessionRecorder()
        self._session_start_bests = {}

        self.climax_handler = ClimaxHandler(self.beat_handler, self.callout_handler, settings=self.settings)

        self.update_checker = UpdateChecker(get_current_version())

        self.intiface_controller = IntifaceController(self.settings, parent=self)
        self.intiface_controller.shutdown_finished.connect(self.close)

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
        self.intiface_controller.emergency_stop()
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
        self.register_start_event(self.intiface_controller.session_started)
        self.register_end_event(self.intiface_controller.session_ended)
        self.beat_handler.linear_movement_planned.connect(self.intiface_controller.on_movement_planned)
        self.beat_handler.register_beat_pause_events(
            self.intiface_controller.pause, self.intiface_controller.pause_ended,
        )
        self.beat_handler.register_beat_pause_events(self.score_tracker.beat_paused, self.score_tracker.beat_resumed)
        self.beat_handler.register_beat_pause_events(self.callout_handler.pause_started,
                                                     self.callout_handler.pause_ended)

        self.beat_handler.register_beat_event(self.score_tracker.beat)
        self.beat_handler.register_beat_event(self._update_record_chase)
        self.beat_handler.register_beat_event(self.beat_track.flash)

        self.beat_handler.register_beat_change_event(self.score_tracker.beat_changed)
        self.beat_handler.register_beat_change_event(self.callout_handler.beat_change_general)
        self.beat_handler.register_beat_change_event(self.beat_track.pulse_change)

        # The climax reads the session plan instead of rolling dice per beat change: it
        # places itself when the session is planned, pins its fakes to boundaries as they
        # are planned, and fires them when those boundaries arrive.
        self.beat_handler.session_planned_event.connect(self.climax_handler.on_session_planned)
        self.beat_handler.plan_extended_event.connect(self.climax_handler.on_plan_extended)
        self.beat_handler.segment_started_event.connect(self.climax_handler.on_segment_started)

        # What actually played, in order, for the Session Explorer.
        self.beat_handler.segment_started_event.connect(self.session_recorder.segment_started)
        self.media_shown_event.connect(self.session_recorder.media_shown)
        self.climax_handler.register_outcome_event(self.session_recorder.climax_recorded)
        self.climax_handler.register_fake_climax_event(self.session_recorder.fake_climax_recorded)
        self.climax_handler.register_outcome_event(self.score_tracker.climax_decided)
        self.climax_handler.register_outcome_event(self._on_climax_outcome)
        self.climax_handler.register_status_event(self._update_climax_status_label)
        self.climax_handler.register_fake_climax_event(self.score_tracker.fake_climax_triggered)

        self.register_start_event(self.score_tracker.session_started)
        self.register_start_event(self.session_recorder.session_started)
        self.register_start_event(self.callout_handler.session_started)
        self.register_start_event(self.climax_handler.session_started)
        self.register_start_event(self._start_record_chase)
        self.register_start_event(self._start_session_timer)
        self.register_start_event(self.beat_track.start)

        self.register_end_event(self.score_tracker.session_ended)
        self.register_end_event(self._end_record_chase)
        self.register_end_event(self._end_session_timer)
        self.register_end_event(self.beat_track.stop)

        self.register_media_skip_event(self.score_tracker.media_skipped)
        self.register_media_skip_event(self.callout_handler.media_skipped)

        self.register_media_repeat_event(self.score_tracker.media_repeated)
        self.register_media_repeat_event(self.callout_handler.media_repeated)

        self.callout_handler.register_new_tease_event(self.display_new_tease, self.hide_last_tease)

        # Wrapped in lambdas (rather than connecting the bound methods directly) so tests can
        # monkeypatch app._show_*_dialog after construction - PyQt binds a direct connection to
        # the method object at connect() time, which a later monkeypatch.setattr(app, ...)
        # can't retroactively intercept, since the signal already holds the original reference.
        self.update_checker.update_available.connect(
            lambda tag, url: self._show_update_available_dialog(tag, url)
        )
        self.update_checker.up_to_date.connect(lambda: self._show_up_to_date_dialog())
        self.update_checker.check_failed.connect(lambda message: self._show_update_check_failed_dialog(message))

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

        privacy_data_action = QAction("Privacy && Data...", self)
        privacy_data_action.triggered.connect(self.show_privacy_data_dialog)
        help_menu.addAction(privacy_data_action)

        help_menu.addSeparator()
        check_updates_action = QAction("Check for Updates...", self)
        check_updates_action.triggered.connect(self.check_for_updates)
        help_menu.addAction(check_updates_action)

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
            dialog.deleteLater()
        self.settings.setValue("GoonerApp/last_seen_version", current_version)

    def show_whats_new_dialog(self):
        dialog = WhatsNewDialog(changelog.CHANGELOG, parent=self)
        dialog.exec()
        dialog.deleteLater()

    def show_help_dialog(self):
        dialog = HelpDialog(parent=self)
        dialog.exec()
        dialog.deleteLater()

    def set_diagnostic_log(self, enabled: bool, level: str | None = None):
        """Applies and persists the opt-in diagnostic log settings.

        Lives on the window rather than in the dialog because flipping either of these has
        to reconfigure the live logger, not just write a key.
        """
        self.diagnostic_log = bool(enabled)
        if level is not None:
            self.diagnostic_log_level = level
        self.settings.setValue("GoonerApp/diagnostic_log", self.diagnostic_log)
        self.settings.setValue("GoonerApp/diagnostic_log_level", self.diagnostic_log_level)
        applog.configure(self.diagnostic_log, self.data_store.base_dir, self.diagnostic_log_level)
        log.info(
            "Diagnostic log %s at level %s",
            "enabled" if self.diagnostic_log else "disabled",
            self.diagnostic_log_level,
        )

    def show_privacy_data_dialog(self):
        dialog = PrivacyDataDialog(self, parent=self)
        dialog.exec()
        dialog.deleteLater()

    def open_discord_invite(self):
        QDesktopServices.openUrl(QUrl(self.DISCORD_INVITE_URL))

    def check_for_updates(self):
        if self._confirm_update_check():
            self.update_checker.check_now()

    @staticmethod
    def _update_check_consent_text() -> str:
        """Spells out everything that actually goes over the wire.

        This used to say "nothing else is sent" flat out, which wasn't quite true: the
        request carries a User-Agent identifying the app, so GitHub's access logs tie an IP
        to "runs GoonerApp". Small, but a privacy promise is worth nothing unless it's exact.
        """
        return (
            "This will send one request to GitHub.com to check the latest release version.\n\n"
            'It carries your IP address (unavoidable for any web request) and a User-Agent of '
            '"GoonerApp-UpdateChecker", which identifies the app to GitHub. Nothing else is '
            "sent - no folders, no filenames, no statistics, nothing identifying you or your "
            "machine - and this never runs on its own.\n\n"
            "Continue?"
        )

    def _confirm_update_check(self) -> bool:
        # Built via explicit QMessageBox(...) + exec() rather than the static .question()
        # convenience method - the static convenience methods are separate C++ entry points
        # that bypass Python-level QMessageBox.exec entirely, so tests/_no_modal_dialogs
        # can't neuter them and a real modal loop would open during tests.
        box = QMessageBox(self)
        box.setIcon(QMessageBox.Icon.Question)
        box.setWindowTitle("Check for Updates?")
        box.setText(self._update_check_consent_text())
        box.setStandardButtons(QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No)
        box.setDefaultButton(QMessageBox.StandardButton.No)
        box.exec()
        return box.clickedButton() is box.button(QMessageBox.StandardButton.Yes)

    def _show_update_available_dialog(self, latest_tag, release_url):
        box = QMessageBox(self)
        box.setWindowTitle("Update Available")
        box.setText(f"A new version is available: {latest_tag} (you're on v{get_current_version()}).")
        open_button = box.addButton("Open Releases Page", QMessageBox.ButtonRole.ActionRole)
        box.addButton("Close", QMessageBox.ButtonRole.RejectRole)
        box.exec()
        if box.clickedButton() is open_button:
            self._open_external_url(release_url)

    @staticmethod
    def _open_external_url(url_string):
        """Opens a URL only if it is http(s).

        release_url is whatever the GitHub API response said. If that response is ever
        attacker-influenced, a file:// or custom-scheme URL would be handed to the default
        Windows handler on a single click.
        """
        url = QUrl(url_string)
        if url.scheme() not in ("http", "https"):
            log.warning("Refusing to open a non-web URL: %r", url_string)
            return
        QDesktopServices.openUrl(url)

    def _show_up_to_date_dialog(self):
        box = QMessageBox(self)
        box.setIcon(QMessageBox.Icon.Information)
        box.setWindowTitle("Up to Date")
        box.setText(f"You're on the latest version (v{get_current_version()}).")
        box.exec()

    def _show_update_check_failed_dialog(self, message):
        box = QMessageBox(self)
        box.setIcon(QMessageBox.Icon.Warning)
        box.setWindowTitle("Update Check Failed")
        box.setText(f"Couldn't check for updates:\n{message}")
        box.exec()

    def btn_next_action(self):
        self.show_next()
        self.media_skipped_event.emit()

    def btn_prev_action(self):
        self.show_prev()
        self.media_repeated_event.emit()

    def video_status_changed(self, status):
        if not self.is_running:
            # stop() leaves the player alone no more, but a status can still land just
            # after it - advancing here would restart the slideshow with no session.
            return

        if status == QMediaPlayer.MediaStatus.InvalidMedia:
            self._recover_from_stuck_video("the backend can't decode it")
            return

        if status == QMediaPlayer.MediaStatus.EndOfMedia:
            elapsed_time = time.time() - self.video_start_time
            if elapsed_time < self.video_min_dur:
                self.media_player.play()
                return

            self.show_next()

    def _on_media_error(self, error, error_string=""):
        if not self.is_running:
            return
        self._recover_from_stuck_video(error_string or str(error))

    def _recover_from_stuck_video(self, reason):
        """Gets the session off a video that will never finish.

        load_media() stops the autoplay timer for videos and waits on EndOfMedia, which
        never arrives for a file the backend cannot open (an exotic codec, a deleted file,
        an unplugged drive) - the session sat on a black frame until the user pressed an
        arrow key. Advancing on a timer rather than calling show_next() directly bounds the
        damage to one file a second if the whole playlist turns out to be unplayable.
        """
        log.warning("Skipping unplayable media: %s", reason)
        self.auto_play_timer.start(MEDIA_ERROR_ADVANCE_MS)

    def next_img_timer(self):
        self.show_next()

    def recalc_autoplay_timer(self):
        self.auto_play_timer.start(int(random.uniform(self.min_dur, self.max_dur) * 1000))

    def open_folder(self):
        # deleteLater() on every dialog below: none of them are kept in an attribute, so
        # without it the C++ object survives as a child of the window and one instance
        # accumulates per open. Harmless for most, but this one retains the full recursive
        # file list of every selected folder.
        dialog = MediaFolderPickerDialog(parent=self)
        accepted = dialog.exec() == QDialog.DialogCode.Accepted
        files = list(dialog.selected_files)  # read before releasing the dialog
        dialog.deleteLater()
        if accepted:
            self._update_climax_status_label("neutral")
            # Counts only - never the folder paths. See applog's module docstring.
            log.info("Playlist loaded: %d files", len(files))
            if files:
                random.shuffle(files)
                self.playlist = files
                self.current_index = 0
                self.start()
            else:
                self.image_label.setText("No supported files found.")
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
        # Never logged, only recorded in memory - a media path is exactly what the privacy
        # rules keep out of the log and the data directory.
        self.media_shown_event.emit(file_path)
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
            self.image_label.setPixmap(load_scaled_pixmap(file_path, self.image_label.size()))
            self.recalc_autoplay_timer()

    def open_settings(self):
        settings_dialog = SettingsDialog(parent=self)
        settings_dialog.exec()
        settings_dialog.deleteLater()

    def stop(self):
        self.intiface_controller.emergency_stop()
        if self.is_running:
            self._end_session(show_statistics=True)

    def closeEvent(self, event):
        """Records a session still in progress before the window goes away.

        Quitting mid-session used to drop it entirely - no history entry, no personal
        records, as if it never happened. stop() isn't reusable here because it ends in a
        modal recap, which is the last thing someone who just hit the X wants to see.
        """
        if self.is_running:
            self._end_session(show_statistics=False)
        self.intiface_controller.shutdown()
        if self.intiface_controller.has_worker:
            # Keep the event loop responsive while the worker delivers Stop and closes.
            # shutdown_finished calls close() again once its thread has exited.
            event.ignore()
            return
        super().closeEvent(event)

    def _end_session(self, show_statistics: bool):
        self.intiface_controller.emergency_stop()
        self.auto_play_timer.stop()
        # Playback was left running: the video kept playing (with sound) behind the
        # modal statistics dialog, and its EndOfMedia then restarted the whole
        # slideshow with no session, no beat and the controls greyed out.
        self.media_player.stop()
        if self.current_movie:
            self.current_movie.stop()
        self._denied_stop_timer.stop()
        self.beat_handler.stop()
        self.btn_load.setText("Set Gooning Folder and Start.")
        self.is_running = False
        self.btn_next.setEnabled(False)
        self.btn_prev.setEnabled(False)
        self.btn_stop.setEnabled(False)
        self._freeze_climax_blink()
        log.info(
            "Session ended after %s (statistics shown: %s)",
            format_clock(self.score_tracker.live_metrics().get("total_dur_sec", 0)),
            show_statistics,
        )
        self.session_recorder.session_ended()
        self.session_ended_event.emit()
        if show_statistics:
            self.show_statistics()

    def start(self):
        if not self.is_running:
            # A denied outcome from the previous session may still have a stop pending -
            # 5 seconds is comfortably enough to stop, close the stats and start again,
            # and it would then kill the fresh session instead.
            self._denied_stop_timer.stop()
            # Set before the signal: handlers reacting to "a session started" should see a
            # running session. _start_session_timer/_start_record_chase both now check it,
            # and would have hidden their overlays the moment they were meant to appear.
            self.is_running = True
            log.info("Session started")
            self.session_started_event.emit()
            self.btn_next.setEnabled(True)
            self.btn_prev.setEnabled(True)
            self.btn_stop.setEnabled(True)
            self.beat_handler.start_beat()
            self.btn_load.setText("Change Gooning Folder.")
        self.load_current_index()
        self.recalc_autoplay_timer()

    def resume_intiface_sync(self):
        self.intiface_controller.resume_sync()
        movement = self.beat_handler.next_linear_movement()
        if movement is not None:
            self.intiface_controller.on_movement_planned(*movement)

    def _on_climax_outcome(self, outcome):
        log.info("Climax outcome: %s", outcome)
        if outcome == "denied":
            self._denied_stop_timer.start(DENIED_STOP_DELAY_MS)

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

    def _update_beat_track(self, text, kind):
        self.beat_track.set_status(text, kind)

    def _start_record_chase(self):
        self._session_start_bests = self.score_tracker.get_all_time_bests()
        self._update_record_chase()

    def _end_record_chase(self):
        self.record_chase_label.hide()

    def _update_record_chase(self):
        # SettingsDialog calls this on every save, including outside a session, where
        # live_metrics() still reports the *previous* session's numbers.
        if not self.is_running or not self.show_record_chase:
            self.record_chase_label.hide()
            return
        status = self.score_tracker.record_chase_status(self._session_start_bests)
        if status is None:
            self.record_chase_label.hide()
            return
        metric, current, best = status
        label = ScoreTracker.PR_METRIC_LABELS[metric]
        current_text = ScoreTracker.format_metric_value(metric, current)
        if current >= best:
            text = f"\U0001f3c6 New {label} Record! {current_text}"
        else:
            best_text = ScoreTracker.format_metric_value(metric, best)
            text = f"\U0001f3c6 Closing in on your {label} record: {current_text} / {best_text}"
        self.record_chase_label.setText(text)
        self.record_chase_label.show()

    def _start_session_timer(self):
        self.session_timer_tick.start(1000)
        self._update_session_timer()

    def _end_session_timer(self):
        self.session_timer_tick.stop()
        self.session_timer_label.hide()

    def _update_session_timer(self):
        # Same reasoning as _update_record_chase: without this, saving settings after a
        # session put a frozen clock back on screen, counting from the old start time.
        if not self.is_running or not self.show_session_timer:
            self.session_timer_label.hide()
            return
        elapsed = self.score_tracker.live_metrics().get("total_dur_sec", 0)
        self.session_timer_label.setText(f"⏱ {format_clock(elapsed)}")
        self.session_timer_label.show()

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
            timeline=self.session_recorder.timeline(),
            parent=self,
        )
        dialog.exec()
        dialog.deleteLater()

    def show_long_term_statistics(self):
        # Imported here, not at module scope: LongTermStatisticsDialog pulls in pyqtgraph and
        # numpy, ~0.5s warm and ~1.6s cold (the realistic case for a --onefile build, which
        # extracts to a temp dir on every run). That was roughly half of cold startup, spent
        # on a chart library for a screen most sessions never open.
        from src.LongTermStatisticsDialog import LongTermStatisticsDialog

        dialog = LongTermStatisticsDialog(
            self.score_tracker.get_history(),
            self.score_tracker.get_all_time_bests(),
            parent=self,
        )
        dialog.exec()
        dialog.deleteLater()
