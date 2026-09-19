from PyQt6.QtCore import Qt
from PyQt6.QtWidgets import (
    QAbstractItemView,
    QDialog,
    QFrame,
    QLabel,
    QPushButton,
    QTableWidget,
    QTableWidgetItem,
    QVBoxLayout,
)

from src import theme, utils
from src.ScoreTracker import ScoreTracker


class StatisticsDialog(QDialog):
    def __init__(self, stats_data: dict, new_records: dict | None = None, timeline=None,
                 save_session=None, new_achievements=None, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Session Statistics")
        self.setModal(True)

        main_layout = QVBoxLayout(self)

        title_text = self._title_for_outcome(stats_data.get("climax_outcome"))
        self.title_label = QLabel(title_text)
        self.title_label.setStyleSheet(
            f"font-size: 24px; font-weight: bold; margin-bottom: 10px; color: {theme.ACCENT};"
        )

        self.conclusion_label = QLabel()
        self.conclusion_label.setWordWrap(True)
        self.conclusion_label.setStyleSheet(
            f"font-size: 14px; margin-bottom: 15px; color: {theme.TEXT}; font-style: italic;"
        )

        self.record_cards = self._build_record_cards(stats_data, new_records or {})
        self.achievement_cards = [
            self._build_achievement_card(item) for item in (new_achievements or [])
        ]

        self.stats_table = QTableWidget()
        self.stats_table.setColumnCount(2)
        self.stats_table.setHorizontalHeaderLabels(["metric", "value"])
        self.stats_table.setEditTriggers(QAbstractItemView.EditTrigger.NoEditTriggers)
        self.stats_table.setSelectionMode(QAbstractItemView.SelectionMode.NoSelection)
        self.stats_table.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        self.stats_table.verticalHeader().setVisible(False)
        self.stats_table.setShowGrid(False)
        self.stats_table.setAlternatingRowColors(True)
        self.stats_table.horizontalHeader().setStretchLastSection(True)

        main_layout.addWidget(self.title_label)
        main_layout.addWidget(self.conclusion_label)
        for card in self.record_cards:
            main_layout.addWidget(card)
        for card in self.achievement_cards:
            main_layout.addWidget(card)
        main_layout.addWidget(self.stats_table)

        # Added before _populate_table() below, which freezes the dialog size - a button
        # appended afterwards would sit outside it and never be seen.
        self._save_session = save_session
        self.explorer_button = self._build_explorer_button(timeline)
        if self.explorer_button is not None:
            main_layout.addWidget(self.explorer_button)

        # Populated last, once every widget above is already in the layout - _populate_table
        # locks the dialog's size to its current content (adjustSize + setFixedSize), so
        # anything added afterward would never actually become visible.
        self._populate_table(stats_data)
        self._gen_conc_text(stats_data)

    def _build_explorer_button(self, timeline):
        """The way into the Session Explorer, or None when there is nothing to explore.

        A session stopped before the beat ever started has no segments, and an explorer
        opening on an empty timeline is worse than no button at all.
        """
        if not timeline or not timeline.get("segments"):
            return None
        button = QPushButton("Session Explorer")
        button.setToolTip("Scroll back through this session - every rhythm, and what was on screen during it")
        button.clicked.connect(lambda: self._open_explorer(timeline))
        return button

    def _open_explorer(self, timeline):
        # Imported here rather than at module scope: the explorer pulls in the video
        # thumbnail machinery, and most sessions close this dialog without opening it.
        from src.SessionExplorerDialog import SessionExplorerDialog

        dialog = SessionExplorerDialog(timeline, save_session=self._save_session, parent=self)
        dialog.exec()
        dialog.deleteLater()

    def _build_record_cards(self, stats_data: dict, new_records: dict) -> list:
        return [
            self._build_record_card(metric, stats_data.get(metric), previous_best)
            for metric, previous_best in new_records.items()
        ]

    def _build_record_card(self, metric: str, new_value, previous_value) -> QFrame:
        card = QFrame()
        card.setStyleSheet(f"QFrame {{ background-color: {theme.ACCENT}; border-radius: 8px; padding: 6px; }}")
        layout = QVBoxLayout(card)

        label = ScoreTracker.PR_METRIC_LABELS.get(metric, metric)
        title = QLabel(f"\U0001f3c6 New Personal Record: {label}")
        title.setStyleSheet(f"color: {theme.BACKGROUND}; font-weight: bold; font-size: 14px;")

        value_label = QLabel(self._format_metric_value(metric, new_value))
        value_label.setStyleSheet(f"color: {theme.BACKGROUND}; font-size: 18px; font-weight: bold;")

        previous_label = QLabel(f"Previous best: {self._format_metric_value(metric, previous_value)}")
        previous_label.setStyleSheet(f"color: {theme.BACKGROUND}; font-size: 11px;")

        layout.addWidget(title)
        layout.addWidget(value_label)
        layout.addWidget(previous_label)
        return card

    def _build_achievement_card(self, achievement) -> QFrame:
        """Same shape as a personal-record card, one shade quieter.

        A record is you beating yourself; an achievement is a thing the app was holding out
        on you. Both belong in the recap, but the record stays the louder of the two.
        """
        card = QFrame()
        card.setStyleSheet(
            f"QFrame {{ background-color: {theme.SURFACE_DARK}; border-radius: 8px; "
            f"padding: 6px; border: 2px solid {theme.ACCENT}; }}"
        )
        layout = QVBoxLayout(card)

        title = QLabel(f"✦ Achievement unlocked: {achievement.name}")
        title.setStyleSheet(f"color: {theme.ACCENT}; font-weight: bold; font-size: 14px;")

        description = QLabel(achievement.description or "Some things you only find by doing.")
        description.setWordWrap(True)
        description.setStyleSheet(f"color: {theme.TEXT}; font-size: 11px;")

        layout.addWidget(title)
        layout.addWidget(description)
        return card

    def _format_metric_value(self, metric: str, value) -> str:
        return ScoreTracker.format_metric_value(metric, value)

    def _title_for_outcome(self, outcome):
        if outcome == "denied":
            return "Session over.\nNot today - no cumming for you."
        if outcome == "ruined":
            return "Congratulations on your session.\nEnjoy your ruined orgasm."
        return "Congratulations to your successful session.\nI hope you came a lot!"  # "real" or None

    def _format_time(self, seconds: float) -> str:
        return utils.format_duration(seconds)

    def _gen_conc_text(self, stats_data: dict):
        active_time = stats_data['total_dur_sec'] - stats_data['pause_dur_sec']
        skips = stats_data.get('skips', 0)
        repeats = stats_data.get('repeats', 0)
        avg_speed = stats_data.get('average_beat_speed_active', 0)

        formatted_total = self._format_time(stats_data['total_dur_sec'])
        formatted_active = self._format_time(active_time)

        text = (
            f"You survived a total of {formatted_total}! "
            f"During this session, you spent {formatted_active} actively stroking "
            f"with an average speed of {avg_speed:.2f} beats per second.\n"
            f"You skipped {skips} media files and repeated {repeats} of them. "
            # A session too short to ever recalculate the beat has no favourite - that
            # rendered as the literal string 'None' while every other nullable stat here
            # already goes through an "N/A".
            f"Your favorite rhythm pattern was '{stats_data['most_used_pattern'] or 'N/A'}'."
        )
        self.conclusion_label.setText(text)

    def _populate_table(self, stats_data: dict):
        display_order = [
            ("Total duration", lambda x: self._format_time(x['total_dur_sec'])),
            ("Active Time", lambda x: self._format_time(x['total_dur_sec'] - x['pause_dur_sec'])),
            ("Pause duration", lambda x: self._format_time(x['pause_dur_sec'])),
            ("Total number of pauses", lambda x: f"{x['total_num_pauses']}"),
            ("Total number of beats", lambda x: f"{x['total_num_beat']}"),
            ("Total number of beat changes", lambda x: f"{x['total_num_beat_change']}"),
            ("Average pause duration", lambda x: self._format_time(x['average_pause_dur_sec'])),
            ("Average beat speed (1/sec)", lambda x: f"{x['average_beat_speed']:.2f}"),
            ("Average beat speed during active time (1/sec)", lambda x: f"{x['average_beat_speed_active']:.2f}"),
            ("Favourite pattern", lambda x: f"{x['most_used_pattern'] or 'N/A'}"),
            ("Skips", lambda x: f"{x['skips']}"),
            ("Repeats", lambda x: f"{x['repeats']}"),
            ("Fakeouts survived", lambda x: f"{x['fakeout_count']}"),
            ("Climax outcome", lambda x: f"{x['climax_outcome'] or 'N/A'}"),
        ]

        self.stats_table.setRowCount(len(display_order))

        for row, (label, formatter) in enumerate(display_order):
            self.stats_table.setItem(row, 0, QTableWidgetItem(label))
            try:
                value_str = formatter(stats_data)
            except KeyError:
                value_str = "N/A"

            item = QTableWidgetItem(value_str)
            item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
            self.stats_table.setItem(row, 1, item)

        self.stats_table.resizeColumnsToContents()
        self._adjust_table_height()
        self.adjustSize()
        self.setFixedSize(self.size())

    def _adjust_table_height(self):
        header_height = self.stats_table.horizontalHeader().height()
        row_heights = sum(self.stats_table.rowHeight(i) for i in range(self.stats_table.rowCount()))
        frame_margin = self.stats_table.frameWidth() * 2
        total_height = header_height + row_heights + frame_margin
        self.stats_table.setFixedHeight(total_height)
