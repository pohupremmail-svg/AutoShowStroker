from PyQt6.QtCore import QRectF, Qt
from PyQt6.QtGui import QColor, QPainter, QPixmap
from PyQt6.QtSvg import QSvgRenderer
from PyQt6.QtWidgets import (
    QDialog,
    QFrame,
    QGraphicsDropShadowEffect,
    QGridLayout,
    QHBoxLayout,
    QLabel,
    QProgressBar,
    QPushButton,
    QScrollArea,
    QVBoxLayout,
    QWidget,
)

from src import achievements, applog, theme

log = applog.get_logger(__name__)

MARK_SIZE = 48
LOCKED_COLOR = "#6a6175"
COLUMNS = 2
PIP_EARNED = "●"
PIP_LOCKED = "○"


def tinted_mark(path, color, size=MARK_SIZE) -> QPixmap:
    """One SVG, either state. The marks are drawn as a single stroke with no fill, so
    painting the colour straight through the alpha gives both the lit and the locked version
    from the same file - no second asset, and no chance of the two drifting apart."""
    pixmap = QPixmap(size, size)
    pixmap.fill(Qt.GlobalColor.transparent)
    renderer = QSvgRenderer(str(path))
    painter = QPainter(pixmap)
    painter.setRenderHint(QPainter.RenderHint.Antialiasing)
    if renderer.isValid():
        renderer.render(painter, QRectF(0, 0, size, size))
    painter.setCompositionMode(QPainter.CompositionMode.CompositionMode_SourceIn)
    painter.fillRect(pixmap.rect(), QColor(color))
    painter.end()
    return pixmap


class AchievementCard(QFrame):
    """One tile: a mark, a name, how many of its levels are earned, and what the next wants.

    A tiered track is a single achievement with three stages rather than three achievements
    that happen to look alike, so it gets one tile with three pips. A standalone one is the
    same tile with a single stage.
    """

    def __init__(self, levels, tracker, played, history, secret_title, parent=None):
        super().__init__(parent)
        self.levels = levels
        self.achievement = levels[0]
        self.level_count = len(levels)
        self.earned = [item for item in levels if tracker.is_unlocked(item.id)]
        self._dates = [tracker.unlocked_at(item.id) or "" for item in self.earned]
        self.levels_earned = len(self.earned)
        self.unlocked = bool(self.earned)
        self.glow = None
        self.progress_bar = None
        self.pips = None

        self.next_level = next(
            (item for item in levels if not tracker.is_unlocked(item.id)), None
        )
        # A secret stays hidden only while nothing of it is earned. A secret *track* hides
        # its steps as well until then: the pips would give away that there is more of it,
        # which is half of what was being kept back.
        hidden = self.achievement.secret and not self.unlocked

        # Scoped by object name on purpose: QLabel is a QFrame subclass, so a bare
        # "QFrame { ... }" rule here paints the mark and the text with the card's own
        # background, padding and rounded border too.
        self.setObjectName("achievementCard")
        self.setStyleSheet(
            f"#achievementCard {{ background-color: {theme.SURFACE_DARK}; "
            "border-radius: 10px; padding: 8px; }"
        )
        row = QHBoxLayout(self)
        row.addWidget(self._build_mark())
        row.addLayout(self._build_text(tracker, played, history, secret_title, hidden), 1)

    # --- construction ---

    def _build_mark(self):
        self.mark = QLabel()
        self.mark.setFixedSize(MARK_SIZE, MARK_SIZE)
        self.mark.setPixmap(
            tinted_mark(
                achievements.icon_path(self.achievement),
                theme.ACCENT if self.unlocked else LOCKED_COLOR,
            )
        )
        if self.unlocked:
            # The same glow the record-chase badge and the session timer already wear, so an
            # earned mark reads as lit rather than merely coloured.
            self.glow = QGraphicsDropShadowEffect()
            self.glow.setColor(QColor(theme.ACCENT))
            self.glow.setBlurRadius(24)
            self.glow.setOffset(0, 0)
            self.mark.setGraphicsEffect(self.glow)
        return self.mark

    def _build_text(self, tracker, played, history, secret_title, hidden):
        text = QVBoxLayout()

        heading = QHBoxLayout()
        self.title = QLabel(secret_title if hidden else self._tile_name())
        self.title.setStyleSheet(
            f"color: {theme.ACCENT if self.unlocked else theme.TEXT}; font-weight: bold; "
            "font-size: 14px;"
        )
        heading.addWidget(self.title)
        heading.addStretch()
        if self.level_count > 1 and not hidden:
            self.pips = self._build_pips()
            heading.addWidget(self.pips)
        text.addLayout(heading)

        self.detail = QLabel(self._detail_for(hidden))
        self.detail.setWordWrap(True)
        self.detail.setStyleSheet(f"color: {theme.TEXT}; font-size: 12px;")
        text.addWidget(self.detail)

        self.status = QLabel(self._status_for())
        self.status.setStyleSheet(f"color: {LOCKED_COLOR}; font-size: 11px;")
        self.status.setVisible(bool(self.status.text()))
        text.addWidget(self.status)

        if self.next_level is not None and not hidden:
            self._build_progress(tracker, played, history, text)
        return text

    def _build_pips(self):
        """One dot per level, filled for the ones earned - the whole point of a tile is that
        the track's shape is visible at a glance."""
        pips = QLabel(
            PIP_EARNED * self.levels_earned + PIP_LOCKED * (self.level_count - self.levels_earned)
        )
        pips.setStyleSheet(
            f"color: {theme.ACCENT if self.unlocked else LOCKED_COLOR}; "
            "font-size: 13px; letter-spacing: 3px;"
        )
        return pips

    def _build_progress(self, tracker, played, history, text):
        progress = tracker.progress_for(self.next_level, played, history)
        if progress is None:
            return
        current, target = progress
        self.progress_bar = QProgressBar()
        self.progress_bar.setRange(0, int(target))
        self.progress_bar.setValue(int(current))
        self.progress_bar.setTextVisible(False)
        self.progress_bar.setFixedHeight(6)
        # Qt's default chunk is a flat green that belongs to another application.
        self.progress_bar.setStyleSheet(
            f"QProgressBar {{ background-color: {theme.SURFACE_DARKEST}; "
            "border: none; border-radius: 3px; }"
            f"QProgressBar::chunk {{ background-color: {theme.ACCENT}; border-radius: 3px; }}"
        )
        text.addWidget(self.progress_bar)

    # --- what the tile says ---

    def _tile_name(self) -> str:
        return self.achievement.track or self.achievement.name

    def _detail_for(self, hidden) -> str:
        if hidden:
            return "Not everything announces itself in advance."
        if self.next_level is None:
            return self.levels[-1].description
        return self.next_level.description

    def _status_for(self) -> str:
        """When the highest earned level was earned, and how far up the track that is."""
        if not self.earned:
            return ""
        when = self._latest_date()
        if self.level_count == 1:
            return f"Earned {when}"
        return f"Level {self.levels_earned} of {self.level_count} - earned {when}"

    def _latest_date(self) -> str:
        """The highest level's date, which is the most recent one - levels are only ever
        earned in order, since a higher one implies every lower one."""
        return self._dates[-1] if self._dates else ""

    # Read by tests and by nothing else - the widgets themselves are the interface.
    def title_text(self) -> str:
        return self.title.text()

    def detail_text(self) -> str:
        return self.detail.text()

    def status_text(self) -> str:
        return self.status.text()


class AchievementsDialog(QDialog):
    """Everything there is to earn, earned or not.

    The point of showing the locked ones at all: until now an achievement could only be
    seen by getting it, which makes them discoveries rather than goals. Progress is read
    against the most recent session, because a per-session rule has to be measured against
    a session and the last one played is the only honest choice.
    """

    SECRET_TITLE = "???"

    def __init__(self, tracker, history, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Achievements")
        self.setModal(True)
        self.resize(820, 600)

        self.tracker = tracker
        self.cards = []

        layout = QVBoxLayout(self)

        title = QLabel("Achievements")
        title.setStyleSheet(f"font-size: 20px; font-weight: bold; color: {theme.ACCENT};")
        layout.addWidget(title)

        earned = sum(1 for item in tracker.catalogue if tracker.is_unlocked(item.id))
        self.summary_label = QLabel(f"{earned} of {len(tracker.catalogue)} earned")
        self.summary_label.setStyleSheet(f"color: {theme.TEXT};")
        layout.addWidget(self.summary_label)

        layout.addWidget(self._build_grid(history), 1)

        close_row = QHBoxLayout()
        close_row.addStretch()
        self.close_button = QPushButton("Close")
        self.close_button.setObjectName("primary")
        self.close_button.clicked.connect(self.accept)
        close_row.addWidget(self.close_button)
        layout.addLayout(close_row)

    def _build_grid(self, history):
        latest = history[-1] if history else {}
        container = QWidget()
        grid = QGridLayout(container)
        grid.setContentsMargins(0, 0, 0, 0)

        for index, levels in enumerate(achievements.grouped(self.tracker.catalogue)):
            card = AchievementCard(
                levels, self.tracker, latest, list(history), self.SECRET_TITLE
            )
            self.cards.append(card)
            grid.addWidget(card, index // COLUMNS, index % COLUMNS)
        grid.setRowStretch(grid.rowCount(), 1)

        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setWidget(container)
        return scroll

    def card_for(self, achievement_id):
        """The tile an achievement lives on - a whole track shares one."""
        return next(
            (
                card
                for card in self.cards
                if any(item.id == achievement_id for item in card.levels)
            ),
            None,
        )
