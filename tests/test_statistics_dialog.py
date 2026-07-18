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


def test_gen_conc_text_formats_summary(dialog):
    text = dialog.conclusion_label.text()

    assert "2 Min 5s" in text  # total_dur_sec formatted
    assert "1 Min 55s" in text  # active_time = total_dur_sec - pause_dur_sec, formatted
    assert "0.43 beats per second" in text  # average_beat_speed_active, 2 decimals
    assert "skipped 1 media files and repeated 0 of them" in text
    assert "'Standard Beat'" in text


def test_gen_conc_text_defaults_missing_skips_and_repeats_to_zero(qtbot):
    partial = dict(FULL_STATS)
    del partial["skips"]
    del partial["repeats"]
    d = StatisticsDialog(partial)
    qtbot.addWidget(d)

    assert "skipped 0 media files and repeated 0 of them" in d.conclusion_label.text()


def test_adjust_table_height_matches_computed_size(dialog):
    header_height = dialog.stats_table.horizontalHeader().height()
    row_heights = sum(dialog.stats_table.rowHeight(i) for i in range(dialog.stats_table.rowCount()))
    frame_margin = dialog.stats_table.frameWidth() * 2
    expected = header_height + row_heights + frame_margin

    assert dialog.stats_table.minimumHeight() == expected
    assert dialog.stats_table.maximumHeight() == expected
