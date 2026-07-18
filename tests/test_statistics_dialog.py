import pytest
from PyQt6.QtWidgets import QLabel

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
    "climax_outcome": None,
    "fakeout_count": 3,
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
    assert dialog.stats_table.rowCount() == 14


def test_fakeout_row_shows_value(dialog):
    for row in range(dialog.stats_table.rowCount()):
        if dialog.stats_table.item(row, 0).text() == "Fakeouts survived":
            assert dialog.stats_table.item(row, 1).text() == "3"
            break
    else:
        pytest.fail("Fakeouts survived row not found")


def test_missing_fakeout_count_shows_na(qtbot):
    partial = dict(FULL_STATS)
    del partial["fakeout_count"]
    d = StatisticsDialog(partial)
    qtbot.addWidget(d)

    for row in range(d.stats_table.rowCount()):
        if d.stats_table.item(row, 0).text() == "Fakeouts survived":
            assert d.stats_table.item(row, 1).text() == "N/A"
            break
    else:
        pytest.fail("Fakeouts survived row not found")


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


def test_title_for_outcome_real(dialog):
    assert dialog._title_for_outcome("real") == "Congratulations to your successful session.\nI hope you came a lot!"


def test_title_for_outcome_ruined(dialog):
    assert dialog._title_for_outcome("ruined") == "Congratulations on your session.\nEnjoy your ruined orgasm."


def test_title_for_outcome_denied(dialog):
    assert dialog._title_for_outcome("denied") == "Session over.\nNot today - no cumming for you."


def test_title_for_outcome_none_defaults_to_classic_message(dialog):
    assert dialog._title_for_outcome(None) == "Congratulations to your successful session.\nI hope you came a lot!"


def test_dialog_title_reflects_denied_outcome(qtbot):
    stats = dict(FULL_STATS)
    stats["climax_outcome"] = "denied"
    d = StatisticsDialog(stats)
    qtbot.addWidget(d)

    assert d.title_label.text() == "Session over.\nNot today - no cumming for you."


def test_climax_outcome_row_shows_na_when_none(dialog):
    for row in range(dialog.stats_table.rowCount()):
        if dialog.stats_table.item(row, 0).text() == "Climax outcome":
            assert dialog.stats_table.item(row, 1).text() == "N/A"
            break
    else:
        pytest.fail("Climax outcome row not found")


def test_climax_outcome_row_shows_outcome_value(qtbot):
    stats = dict(FULL_STATS)
    stats["climax_outcome"] = "ruined"
    d = StatisticsDialog(stats)
    qtbot.addWidget(d)

    for row in range(d.stats_table.rowCount()):
        if d.stats_table.item(row, 0).text() == "Climax outcome":
            assert d.stats_table.item(row, 1).text() == "ruined"
            break
    else:
        pytest.fail("Climax outcome row not found")


def test_adjust_table_height_matches_computed_size(dialog):
    header_height = dialog.stats_table.horizontalHeader().height()
    row_heights = sum(dialog.stats_table.rowHeight(i) for i in range(dialog.stats_table.rowCount()))
    frame_margin = dialog.stats_table.frameWidth() * 2
    expected = header_height + row_heights + frame_margin

    assert dialog.stats_table.minimumHeight() == expected
    assert dialog.stats_table.maximumHeight() == expected


# --- new personal record cards ---


def test_no_record_cards_when_new_records_is_none(qtbot):
    d = StatisticsDialog(dict(FULL_STATS), new_records=None)
    qtbot.addWidget(d)
    assert d.record_cards == []


def test_no_record_cards_when_new_records_is_empty(qtbot):
    d = StatisticsDialog(dict(FULL_STATS), new_records={})
    qtbot.addWidget(d)
    assert d.record_cards == []


def test_one_card_per_new_record(qtbot):
    d = StatisticsDialog(dict(FULL_STATS), new_records={"total_dur_sec": 100.0, "fakeout_count": 1})
    qtbot.addWidget(d)
    assert len(d.record_cards) == 2


def test_record_card_shows_metric_label_new_value_and_previous_best(qtbot):
    stats = dict(FULL_STATS)  # total_dur_sec = 125.0 -> "2 Min 5s"
    d = StatisticsDialog(stats, new_records={"total_dur_sec": 100.0})  # previous best 100.0 -> "1 Min 40s"
    qtbot.addWidget(d)

    card_text = " ".join(label.text() for label in d.record_cards[0].findChildren(QLabel))
    assert "Total Duration" in card_text
    assert "2 Min 5s" in card_text
    assert "1 Min 40s" in card_text


def test_record_card_formats_count_metrics_as_plain_numbers(qtbot):
    stats = dict(FULL_STATS)  # fakeout_count = 3
    d = StatisticsDialog(stats, new_records={"fakeout_count": 1})
    qtbot.addWidget(d)

    card_text = " ".join(label.text() for label in d.record_cards[0].findChildren(QLabel))
    assert "Fakeouts Survived" in card_text
    assert "3" in card_text
    assert "1" in card_text
