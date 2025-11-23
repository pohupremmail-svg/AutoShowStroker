from PyQt6.QtWidgets import QDialog, QVBoxLayout, QLabel, QTableWidget, QTableWidgetItem, QAbstractItemView
from PyQt6.QtCore import Qt


class StatisticsDialog(QDialog):
    def __init__(self, stats_data: dict, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Session Statistics")
        self.setModal(True)

        main_layout = QVBoxLayout(self)

        title = QLabel("Congratulations to your successful session.\nI hope you came a lot!")
        title.setStyleSheet("font-size: 24px; font-weight: bold; margin-bottom: 10px;")

        self.stats_table = QTableWidget()
        self.stats_table.setColumnCount(2)
        self.stats_table.setHorizontalHeaderLabels(["metric", "value"])
        self.stats_table.setEditTriggers(QAbstractItemView.EditTrigger.NoEditTriggers)

        self._populate_table(stats_data)

        self.conclusion_label = QLabel()
        self.conclusion_label.setWordWrap(True)
        self.conclusion_label.setStyleSheet("font-size: 14px; margin-bottom: 15px; color: #555;")

        main_layout.addWidget(title)
        main_layout.addWidget(self.conclusion_label)
        main_layout.addWidget(self.stats_table)
        self._gen_conc_text(stats_data)

    def _gen_conc_text(self, stats_data: dict):
        active_time = stats_data['total_dur_sec'] - stats_data['pause_dur_sec']
        skips = None  # TODO
        average_speed = None # TODO

    def _populate_table(self, stats_data: dict):
        display_order = [
            ("Total duration (min)", lambda x: f"{x['total_dur_sec'] / 60:.2f}"),
            ("Active Time (min)", lambda x: f"{(x['total_dur_sec'] - x['pause_dur_sec']) / 60:.2f}"),
            ("Pause duration (sec)", lambda x: f"{x['pause_dur_sec']:.1f}"),
            ("Total number of pauses", lambda x: f"{x['total_num_pauses']}"),
            ("Total number of beats", lambda x: f"{x['total_num_beat']}"),
            ("Total number of beat changes", lambda x: f"{x['total_num_beat_change']}"),
            ("Average pause duration (sec)", lambda x: f"{x['average_pause_dur_sec']:.2f}" if x['average_pause_dur_sec'] else "N/A"),
            ("Average beat speed (1/sec)", lambda x: f"{x['average_beat_speed']}"),
            ("Average beat speed during active time (1/sec)", lambda x: f"{x['average_beat_speed_active']}"),
            ("Favourite pattern", lambda x: f"{x['most_used_pattern']}"),
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