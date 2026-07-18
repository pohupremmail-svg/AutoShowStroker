import pyqtgraph as pg
from PyQt6.QtWidgets import QComboBox, QDialog, QHBoxLayout, QLabel, QPushButton, QVBoxLayout

from src import theme
from src.ScoreTracker import ScoreTracker

# pyqtgraph defaults to a white background, which would clash hard with the app's dark neon
# theme - set this once, globally, before any PlotWidget is created.
pg.setConfigOption("background", theme.SURFACE_DARK)
pg.setConfigOption("foreground", theme.TEXT)


class LongTermStatisticsDialog(QDialog):
    """Read-only, on-demand view of session history across the app's lifetime.

    Separate from the end-of-session StatisticsDialog on purpose - this one is only ever
    shown via the Statistics menu, never automatically.
    """

    def __init__(self, history: list, all_time_bests: dict, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Long-term Statistics")
        self.setModal(True)
        # Roughly a third of a 1920x1080 (Full HD) screen by area, so the chart has real
        # room to breathe on first open - still just an initial size, freely resizable.
        self.resize(960, 720)

        self.history = history
        self.all_time_bests = all_time_bests

        layout = QVBoxLayout(self)

        title = QLabel("Your Long-term Statistics")
        title.setStyleSheet(f"font-size: 22px; font-weight: bold; color: {theme.ACCENT}; margin-bottom: 8px;")
        layout.addWidget(title)

        self.sessions_label = QLabel(f"Sessions played: {len(history)}")
        self.sessions_label.setStyleSheet(f"color: {theme.TEXT}; font-size: 13px;")
        layout.addWidget(self.sessions_label)

        self.best_labels = {}
        for metric in ScoreTracker.PR_METRICS:
            label = QLabel(self._format_best_line(metric))
            label.setStyleSheet(f"color: {theme.TEXT}; font-size: 13px;")
            self.best_labels[metric] = label
            layout.addWidget(label)

        selector_row = QHBoxLayout()
        selector_row.addWidget(QLabel("Chart metric:"))
        self.metric_selector = QComboBox()
        for metric in ScoreTracker.PR_METRICS:
            self.metric_selector.addItem(ScoreTracker.PR_METRIC_LABELS[metric], metric)
        self.metric_selector.currentIndexChanged.connect(self._on_metric_changed)
        selector_row.addWidget(self.metric_selector)
        selector_row.addStretch()
        layout.addLayout(selector_row)

        self.plot_widget = pg.PlotWidget()
        layout.addWidget(self.plot_widget)
        self._plot_metric(ScoreTracker.PR_METRICS[0])

        self.button = QPushButton("Close")
        self.button.setObjectName("primary")
        self.button.clicked.connect(self.accept)
        layout.addWidget(self.button)

    def _format_best_line(self, metric: str) -> str:
        label = ScoreTracker.PR_METRIC_LABELS[metric]
        value = self.all_time_bests.get(metric)
        formatted = "N/A" if value is None else ScoreTracker.format_metric_value(metric, value)
        return f"Best {label}: {formatted}"

    def _on_metric_changed(self, index: int):
        metric = self.metric_selector.itemData(index)
        self._plot_metric(metric)

    def _plot_metric(self, metric: str):
        values = [entry.get(metric, 0) for entry in self.history]
        x = list(range(1, len(values) + 1))
        self.plot_widget.clear()
        self.plot_widget.plot(x, values, pen=pg.mkPen(theme.ACCENT, width=2), symbol="o", symbolBrush=theme.ACCENT)
        self.plot_widget.setLabel("bottom", "Session #")
        self.plot_widget.setLabel(
            "left", ScoreTracker.PR_METRIC_LABELS[metric], units=ScoreTracker.PR_METRIC_UNITS[metric]
        )
