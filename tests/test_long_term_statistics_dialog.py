import pytest

from src.LongTermStatisticsDialog import LongTermStatisticsDialog
from src.ScoreTracker import ScoreTracker

HISTORY = [
    {
        "ended_at": "2026-01-01 10:00",
        "total_dur_sec": 60.0,
        "total_num_beat": 10,
        "average_beat_speed_active": 0.5,
        "fakeout_count": 1,
    },
    {
        "ended_at": "2026-01-02 10:00",
        "total_dur_sec": 120.0,
        "total_num_beat": 30,
        "average_beat_speed_active": 0.8,
        "fakeout_count": 0,
    },
]
ALL_TIME_BESTS = {
    "total_dur_sec": 120.0,
    "total_num_beat": 30,
    "average_beat_speed_active": 0.8,
    "fakeout_count": 1,
}


@pytest.fixture
def dialog(qtbot):
    d = LongTermStatisticsDialog(list(HISTORY), dict(ALL_TIME_BESTS))
    qtbot.addWidget(d)
    return d


def test_sessions_played_count(dialog):
    assert "2" in dialog.sessions_label.text()


def test_best_labels_show_formatted_all_time_bests(dialog):
    assert "2 Min" in dialog.best_labels["total_dur_sec"].text()
    assert "30" in dialog.best_labels["total_num_beat"].text()
    assert "0.80 beats/sec" in dialog.best_labels["average_beat_speed_active"].text()
    assert "1" in dialog.best_labels["fakeout_count"].text()


def test_best_label_shows_na_when_metric_missing(qtbot):
    d = LongTermStatisticsDialog([], {})
    qtbot.addWidget(d)
    assert "N/A" in d.best_labels["total_dur_sec"].text()


def test_metric_selector_lists_all_pr_metrics(dialog):
    labels = [dialog.metric_selector.itemText(i) for i in range(dialog.metric_selector.count())]
    assert labels == [ScoreTracker.PR_METRIC_LABELS[m] for m in ScoreTracker.PR_METRICS]


def test_default_chart_metric_is_total_duration(dialog):
    assert dialog.metric_selector.currentData() == "total_dur_sec"

    data_items = dialog.plot_widget.listDataItems()
    assert len(data_items) == 1
    _x, y = data_items[0].getData()
    assert list(y) == [60.0, 120.0]


def test_changing_metric_selector_replots_chart(dialog):
    index = dialog.metric_selector.findData("total_num_beat")
    dialog.metric_selector.setCurrentIndex(index)

    data_items = dialog.plot_widget.listDataItems()
    assert len(data_items) == 1
    _x, y = data_items[0].getData()
    assert list(y) == [10, 30]


def test_empty_history_plots_without_crashing(qtbot):
    d = LongTermStatisticsDialog([], {})
    qtbot.addWidget(d)

    data_items = d.plot_widget.listDataItems()
    assert len(data_items) == 1
    _x, y = data_items[0].getData()
    assert y is None or len(y) == 0


def test_close_button_accepts_dialog(dialog):
    assert dialog.button.text() == "Close"
    dialog.button.click()
    assert dialog.result() == LongTermStatisticsDialog.DialogCode.Accepted
