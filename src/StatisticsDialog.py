from PyQt6.QtCore import Qt
from PyQt6.QtWidgets import (
    QAbstractItemView,
    QDialog,
    QFrame,
    QLabel,
    QTableWidget,
    QTableWidgetItem,
    QVBoxLayout,
)

from src import theme, utils
from src.ScoreTracker import ScoreTracker


class StatisticsDialog(QDialog):
    def __init__(self, stats_data: dict, new_records: dict | None = None, parent=None):
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
        main_layout.addWidget(self.stats_table)

        # Populated last, once every widget above is already in the layout - _populate_table
        # locks the dialog's size to its current content (adjustSize + setFixedSize), so
        # anything added afterward would never actually become visible.
        self._populate_table(stats_data)
        self._gen_conc_text(stats_data)

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
            f"Your favorite rhythm pattern was '{stats_data['most_used_pattern']}'."
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
            ("Favourite pattern", lambda x: f"{x['most_used_pattern']}"),
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
