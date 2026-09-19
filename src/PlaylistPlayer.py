"""What is on screen, and what replaces it.

A QStackedWidget with two pages - a QLabel for images and animated GIFs, a VideoDisplay for
clips - plus the timer that decides how long the current one stays up. It is the stack
rather than a wrapper around one, so it drops into the layout exactly where the bare stack
used to sit.

It knows two things about the session it serves and no more: whether one is running, and
whether it is a replay. Both arrive through plain calls from GoonerApp rather than being
read back off it, which is what lets the whole thing be driven from a test without a window.

The replay case is the reason `script` is here at all. A saved session records where each
picture was cut, and that pacing is as much a part of it as the beats are - it is most of
what "the same session" means when the pictures are someone else's.

Privacy: a media path is emitted on `media_shown` for the in-memory session recorder and is
never logged. See applog's module docstring for why counts go in the log instead.
"""
import random
import time

from PyQt6.QtCore import Qt, QTimer, QUrl, pyqtSignal
from PyQt6.QtGui import QMovie
from PyQt6.QtMultimedia import QAudioOutput, QMediaPlayer
from PyQt6.QtWidgets import QLabel, QStackedWidget

from src import applog, media_kinds, theme
from src.utils import load_scaled_pixmap
from src.VideoDisplay import VideoDisplay

log = applog.get_logger(__name__)


class PlaylistPlayer(QStackedWidget):
    # The timing settings were always stored under GoonerApp/, and they stay there: the
    # settings dialog builds its fields from this attribute, so naming the old group keeps
    # every existing installation's values without a migration.
    SETTINGS_GROUP = "GoonerApp"

    # Delay before skipping past media that will never finish playing - long enough that an
    # entirely unplayable playlist cycles visibly instead of spinning.
    MEDIA_ERROR_ADVANCE_MS = 1000

    # Read back by the settings dialog's "Reset to defaults", which looks them up on the
    # object a field is bound to - see SettingsDialog.add_reset_button.
    DEFAULTS = {
        "min_dur": 0.5,
        "max_dur": 4.0,
        "video_min_dur": 1.5,
        "vid_loudness": 1.0,
    }

    media_shown = pyqtSignal(str)

    def __init__(self, settings, parent=None):
        super().__init__(parent)
        self.settings = settings
        for name, fallback in self.DEFAULTS.items():
            setattr(self, name, float(
                settings.value(f"{self.SETTINGS_GROUP}/{name}", fallback)
            ))

        self.playlist = []
        self.current_index = 0
        # Set while replaying a saved session. None for a live one.
        self.script = None
        self._active = False
        self.current_movie = None
        self.video_start_time = 0

        self.image_label = QLabel("No Gooning files selected yet.")
        self.image_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.image_label.setMinimumSize(1, 1)
        self.image_label.setStyleSheet(
            f"background-color: {theme.SURFACE_DARK}; color: {theme.TEXT}; font-size: 20px; border-radius: 12px;"
        )
        self.addWidget(self.image_label)

        self.video_widget = VideoDisplay()
        self.media_player = QMediaPlayer()
        self.audio_output = QAudioOutput()
        self.media_player.setAudioOutput(self.audio_output)
        self.media_player.setVideoOutput(self.video_widget.video_item)
        self.addWidget(self.video_widget)

        self.media_player.mediaStatusChanged.connect(self.video_status_changed)
        self.media_player.errorOccurred.connect(self._on_media_error)

        self.auto_play_timer = QTimer(self)
        self.auto_play_timer.timeout.connect(self.show_next)

    # --- session lifecycle ------------------------------------------------------------------

    def session_started(self):
        self._active = True

    def session_ended(self):
        """Everything stops. Playback was once left running here, and the clip kept going
        (with sound) behind the modal statistics dialog, its EndOfMedia then restarting the
        whole slideshow with no session, no beat and the controls greyed out."""
        self._active = False
        self.auto_play_timer.stop()
        self.media_player.stop()
        if self.current_movie:
            self.current_movie.stop()

    def set_playlist(self, files):
        """Takes a fresh, already-shuffled playlist and rewinds to its start."""
        self.playlist = list(files)
        self.current_index = 0

    def set_muted(self, muted: bool):
        """Silences the clip that is playing. The rhythm is muted separately."""
        self.audio_output.setMuted(muted)

    def show_message(self, text):
        """Puts a line where the picture goes - there is nothing to play."""
        self.setCurrentWidget(self.image_label)
        self.image_label.setText(text)

    # --- moving through the playlist -----------------------------------------------------------

    def show_next(self):
        if not self.playlist:
            return
        self.current_index = (self.current_index + 1) % len(self.playlist)
        self.load_current()

    def show_prev(self):
        if not self.playlist:
            return
        self.current_index = (self.current_index - 1) % len(self.playlist)
        self.load_current()

    def load_current(self):
        scripted = self.script.next_media_path() if self.script is not None else None
        if scripted is not None:
            self._show_media_path(scripted)
            return
        if not self.playlist:
            return
        self._show_media_path(str(self.playlist[self.current_index]))

    def _show_media_path(self, file_path):
        # Never logged, only recorded in memory - a media path is exactly what the privacy
        # rules keep out of the log and the data directory.
        self.media_shown.emit(file_path)
        self.load_media(file_path)

    def recalc_autoplay_timer(self):
        """How long the medium now on screen stays up.

        A replay takes the recorded gap - the pacing is as much a part of the saved session
        as the beats are, and it is most of what "the same session" means when the pictures
        are someone else's. Once the script runs out the settings take over again.
        """
        if self.script is not None:
            scripted = self.script.next_media_gap()
            if scripted is not None:
                self.auto_play_timer.start(int(scripted * 1000))
                return
        self.auto_play_timer.start(int(random.uniform(self.min_dur, self.max_dur) * 1000))

    def load_media(self, file_path):
        kind = media_kinds.media_kind(file_path)

        self.media_player.stop()
        if self.current_movie:
            self.current_movie.stop()
            self.image_label.setMovie(None)
            self.current_movie = None

        if kind == "video":
            # Live, a clip runs to EndOfMedia and is not on the autoplay timer at all. In a
            # replay the recorded gap wins instead: the saved session says where this clip
            # was actually cut, and that is the thing being replayed.
            if self.script is None:
                self.auto_play_timer.stop()
            else:
                self.recalc_autoplay_timer()

            self.setCurrentWidget(self.video_widget)
            self.media_player.setSource(QUrl.fromLocalFile(file_path))
            self.media_player.play()
            self.video_start_time = time.time()
            self.audio_output.setVolume(self.vid_loudness)

        elif kind == "gif":
            self.setCurrentWidget(self.image_label)

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
            self.setCurrentWidget(self.image_label)
            self.image_label.setPixmap(load_scaled_pixmap(file_path, self.image_label.size()))
            self.recalc_autoplay_timer()

    # --- video that will not play ---------------------------------------------------------------

    def video_status_changed(self, status):
        if not self._active:
            # A status can still land just after the session ended - advancing here would
            # restart the slideshow with no session behind it.
            return

        if status == QMediaPlayer.MediaStatus.InvalidMedia:
            self._recover_from_stuck_video("the backend can't decode it")
            return

        if status == QMediaPlayer.MediaStatus.EndOfMedia:
            if time.time() - self.video_start_time < self.video_min_dur:
                self.media_player.play()
                return
            self.show_next()

    def _on_media_error(self, error, error_string=""):
        if not self._active:
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
        self.auto_play_timer.start(self.MEDIA_ERROR_ADVANCE_MS)
