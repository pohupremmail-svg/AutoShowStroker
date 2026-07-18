import pytest

from src.StatisticsDialog import StatisticsDialog

FULL_STATS = {
    "total_dur_sec": 125.0,
    "pause_dur_sec": 10.0,
    "average_pause_dur_sec": 5.0,
    "total_num_pauses": 2,
    "total_num_beat": 50,
    "total_num_beat_change": 5,
    "average_beat_speed": 0.4,
    "average_beat_speed_active": 0.43,
    "most_used_pattern": "Standard Beat",
    "skips": 1,
    "repeats": 0,
}


@pytest.fixture
def dialog(qtbot):
    d = StatisticsDialog(dict(FULL_STATS))
    qtbot.addWidget(d)
    return d


@pytest.mark.parametrize(
    ("seconds", "expected"),
    [
        (None, "N/A"),
        (0, "0s"),
        (45, "45s"),
        (60, "1 Min"),
        (125, "2 Min 5s"),
    ],
)
def test_format_time(dialog, seconds, expected):
    assert dialog._format_time(seconds) == expected


def test_populate_table_has_a_row_per_metric(dialog):
    assert dialog.stats_table.rowCount() == 12


def test_missing_optional_key_shows_na(qtbot):
    partial = dict(FULL_STATS)
    del partial["skips"]
    d = StatisticsDialog(partial)
    qtbot.addWidget(d)

    for row in range(d.stats_table.rowCount()):
        if d.stats_table.item(row, 0).text() == "Skips":
            assert d.stats_table.item(row, 1).text() == "N/A"
            break
    else:
        pytest.fail("Skips row not found")
