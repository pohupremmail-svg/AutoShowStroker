from PyQt6.QtCore import Qt
from PyQt6.QtWidgets import QAbstractItemView, QDialog, QLabel, QTableWidget, QTableWidgetItem, QVBoxLayout

from src import theme


class StatisticsDialog(QDialog):
    def __init__(self, stats_data: dict, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Session Statistics")
        self.setModal(True)

        main_layout = QVBoxLayout(self)

        title_text = self._title_for_outcome(stats_data.get("climax_outcome"))
        self.title_label = QLabel(title_text)
        self.title_label.setStyleSheet(
            f"font-size: 24px; font-weight: bold; margin-bottom: 10px; color: {theme.ACCENT};"
        )

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

        self._populate_table(stats_data)

        self.conclusion_label = QLabel()
        self.conclusion_label.setWordWrap(True)
        self.conclusion_label.setStyleSheet(
            f"font-size: 14px; margin-bottom: 15px; color: {theme.TEXT}; font-style: italic;"
        )

        main_layout.addWidget(self.title_label)
        main_layout.addWidget(self.conclusion_label)
        main_layout.addWidget(self.stats_table)
        self._gen_conc_text(stats_data)

    def _title_for_outcome(self, outcome):
        if outcome == "denied":
            return "Session over.\nNot today - no cumming for you."
        if outcome == "ruined":
            return "Congratulations on your session.\nEnjoy your ruined orgasm."
        return "Congratulations to your successful session.\nI hope you came a lot!"  # "real" or None

    def _format_time(self, seconds: float) -> str:
        if seconds is None:
            return "N/A"
        total_seconds = int(round(seconds))
        if total_seconds < 60:
            return f"{total_seconds}s"
        minutes = total_seconds // 60
        secs = total_seconds % 60
        if secs == 0:
            return f"{minutes} Min"
        return f"{minutes} Min {secs}s"

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
