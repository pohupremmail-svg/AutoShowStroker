import json
import time

import pytest
from PyQt6.QtCore import QSettings

from src.ScoreTracker import ScoreTracker


def test_initial_state():
    tracker = ScoreTracker()
    assert tracker.beat_count == 0
    assert tracker.number_of_pauses == 0
    assert tracker.patterns == {}


def test_session_started_resets_counters():
    tracker = ScoreTracker()
    tracker.beat_count = 5
    tracker.skips = 3
    tracker.patterns = {"Standard Beat": 2}

    tracker.session_started()

    assert tracker.beat_count == 0
    assert tracker.skips == 0
    assert tracker.patterns == {}
    assert tracker.session_start_time is not None


def test_beat_pause_resume_tracks_counts():
    tracker = ScoreTracker()
    tracker.beat_paused()
    assert tracker.number_of_pauses == 1
    assert tracker.cur_pause_start_time is not None

    tracker.beat_resumed()
    assert tracker.cur_pause_start_time is None
    assert tracker.total_duration_of_pauses >= 0


def test_media_skipped_and_repeated_increment_counters():
    tracker = ScoreTracker()
    tracker.media_skipped()
    tracker.media_skipped()
    tracker.media_repeated()
    assert tracker.skips == 2
    assert tracker.repeats == 1


def test_beat_changed_tracks_pattern_usage():
    tracker = ScoreTracker()
    tracker.beat_changed(None, "Standard Beat")
    tracker.beat_changed(None, "Standard Beat")
    tracker.beat_changed(None, "Quick Swing")
    assert tracker.number_of_beat_changes == 3
    assert tracker.patterns == {"Standard Beat": 2, "Quick Swing": 1}


def test_find_fav_pattern_returns_most_used():
    tracker = ScoreTracker()
    tracker.beat_changed(None, "A")
    tracker.beat_changed(None, "B")
    tracker.beat_changed(None, "B")
    assert tracker._find_fav_pattern() == "B"


def test_find_fav_pattern_none_when_no_patterns():
    tracker = ScoreTracker()
    assert tracker._find_fav_pattern() is None


def test_session_ended_computes_averages(monkeypatch):
    tracker = ScoreTracker()
    tracker.session_start_time = 100.0
    tracker.beat_count = 20
    tracker.number_of_pauses = 2
    tracker.total_duration_of_pauses = 5.0

    monkeypatch.setattr(time, "time", lambda: 110.0)
    tracker.session_ended()

    assert tracker.total_run_time == pytest.approx(10.0)
    assert tracker.average_pause_duration == pytest.approx(2.5)
    assert tracker.average_beat_speed == pytest.approx(2.0)
    assert tracker.average_beat_speed_active == pytest.approx(20 / 5.0)


def test_session_ended_no_pauses_average_is_none(monkeypatch):
    tracker = ScoreTracker()
    tracker.session_start_time = 0.0
    monkeypatch.setattr(time, "time", lambda: 5.0)

    tracker.session_ended()

    assert tracker.average_pause_duration is None


def test_deliver_infos_contains_expected_data(monkeypatch):
    tracker = ScoreTracker()
    tracker.session_started()
    tracker.beat()
    tracker.beat()
    tracker.beat_changed(None, "Standard Beat")
    tracker.media_skipped()
    tracker.media_repeated()

    monkeypatch.setattr(time, "time", lambda: tracker.session_start_time + 1.0)
    tracker.session_ended()

    info = tracker.deliver_infos()

    assert info["total_num_beat"] == 2
    assert info["skips"] == 1
    assert info["repeats"] == 1
    assert info["most_used_pattern"] == "Standard Beat"


def test_climax_outcome_defaults_to_none():
    tracker = ScoreTracker()
    assert tracker.climax_outcome is None
    assert tracker.deliver_infos()["climax_outcome"] is None


def test_climax_decided_sets_outcome():
    tracker = ScoreTracker()
    tracker.climax_decided("ruined")
    assert tracker.climax_outcome == "ruined"
    assert tracker.deliver_infos()["climax_outcome"] == "ruined"


def test_session_started_resets_climax_outcome():
    tracker = ScoreTracker()
    tracker.climax_outcome = "denied"
    tracker.session_started()
    assert tracker.climax_outcome is None


# --- fakeout counter ---


def test_fake_climax_triggered_increments_fakeout_count():
    tracker = ScoreTracker()
    tracker.fake_climax_triggered()
    tracker.fake_climax_triggered()
    assert tracker.fakeout_count == 2
    assert tracker.deliver_infos()["fakeout_count"] == 2


def test_session_started_resets_fakeout_count():
    tracker = ScoreTracker()
    tracker.fakeout_count = 3
    tracker.session_started()
    assert tracker.fakeout_count == 0


# --- session history persistence ---


def test_history_empty_by_default():
    tracker = ScoreTracker()
    assert tracker.get_history() == []


def test_session_ended_appends_a_history_entry(monkeypatch):
    tracker = ScoreTracker()
    tracker.session_start_time = 0.0
    tracker.beat_count = 10
    monkeypatch.setattr(time, "time", lambda: 10.0)

    tracker.session_ended()

    history = tracker.get_history()
    assert len(history) == 1
    assert history[0]["total_dur_sec"] == pytest.approx(10.0)
    assert history[0]["total_num_beat"] == 10
    assert "ended_at" in history[0]


def test_session_ended_persists_history_to_settings(tmp_path, monkeypatch):
    ini = tmp_path / "settings.ini"
    settings = QSettings(str(ini), QSettings.Format.IniFormat)
    tracker = ScoreTracker(settings=settings)
    tracker.session_start_time = 0.0
    monkeypatch.setattr(time, "time", lambda: 5.0)

    tracker.session_ended()

    saved = json.loads(settings.value("ScoreTracker/session_history"))
    assert len(saved) == 1
    assert saved[0]["total_dur_sec"] == pytest.approx(5.0)


def test_history_loaded_from_settings(tmp_path):
    ini = tmp_path / "settings.ini"
    settings = QSettings(str(ini), QSettings.Format.IniFormat)
    settings.setValue(
        "ScoreTracker/session_history",
        json.dumps(
            [
                {
                    "ended_at": "2026-01-01 00:00",
                    "total_dur_sec": 42.0,
                    "total_num_beat": 1,
                    "average_beat_speed_active": 1.0,
                    "fakeout_count": 0,
                }
            ]
        ),
    )

    tracker = ScoreTracker(settings=settings)

    assert len(tracker.get_history()) == 1
    assert tracker.get_history()[0]["total_dur_sec"] == 42.0


def test_history_capped_at_max_entries(monkeypatch):
    tracker = ScoreTracker()
    tracker.history = [
        {
            "ended_at": f"entry-{i}",
            "total_dur_sec": 1.0,
            "total_num_beat": 1,
            "average_beat_speed_active": 1.0,
            "fakeout_count": 0,
        }
        for i in range(ScoreTracker.MAX_HISTORY_ENTRIES)
    ]
    tracker.session_start_time = 0.0
    monkeypatch.setattr(time, "time", lambda: 1.0)

    tracker.session_ended()

    history = tracker.get_history()
    assert len(history) == ScoreTracker.MAX_HISTORY_ENTRIES
    assert history[0]["ended_at"] == "entry-1"  # oldest (entry-0) dropped


# --- personal-record detection ---


def test_first_ever_session_sets_no_new_records(monkeypatch):
    tracker = ScoreTracker()
    tracker.session_start_time = 0.0
    tracker.beat_count = 50
    monkeypatch.setattr(time, "time", lambda: 100.0)

    tracker.session_ended()

    assert tracker.last_session_new_records == {}


def test_second_session_beating_one_metric_flags_only_that_metric(monkeypatch):
    tracker = ScoreTracker()

    tracker.session_start_time = 0.0
    tracker.beat_count = 5
    monkeypatch.setattr(time, "time", lambda: 10.0)
    tracker.session_ended()

    tracker.session_started()
    tracker.session_start_time = 100.0
    tracker.beat_count = 3
    monkeypatch.setattr(time, "time", lambda: 120.0)  # 20s, beats the 10s record; fewer beats doesn't
    tracker.session_ended()

    assert set(tracker.last_session_new_records.keys()) == {"total_dur_sec"}
    assert tracker.last_session_new_records["total_dur_sec"] == pytest.approx(10.0)


def test_tying_previous_best_is_not_a_new_record(monkeypatch):
    tracker = ScoreTracker()
    tracker.session_start_time = 0.0
    monkeypatch.setattr(time, "time", lambda: 10.0)
    tracker.session_ended()

    tracker.session_started()
    tracker.session_start_time = 100.0
    monkeypatch.setattr(time, "time", lambda: 110.0)  # same 10s duration again
    tracker.session_ended()

    assert "total_dur_sec" not in tracker.last_session_new_records


def test_get_all_time_bests_reflects_max_across_history(monkeypatch):
    tracker = ScoreTracker()
    tracker.session_start_time = 0.0
    tracker.beat_count = 5
    monkeypatch.setattr(time, "time", lambda: 10.0)
    tracker.session_ended()

    tracker.session_started()
    tracker.session_start_time = 100.0
    tracker.beat_count = 50
    monkeypatch.setattr(time, "time", lambda: 105.0)
    tracker.session_ended()

    bests = tracker.get_all_time_bests()
    assert bests["total_dur_sec"] == pytest.approx(10.0)
    assert bests["total_num_beat"] == 50


# --- shared metric-value formatting ---


def test_format_metric_value_formats_duration_as_time():
    assert ScoreTracker.format_metric_value("total_dur_sec", 125.0) == "2 Min 5s"


def test_format_metric_value_formats_speed_with_unit():
    assert ScoreTracker.format_metric_value("average_beat_speed_active", 0.4321) == "0.43 beats/sec"


def test_format_metric_value_formats_counts_as_plain_numbers():
    assert ScoreTracker.format_metric_value("total_num_beat", 42) == "42"
    assert ScoreTracker.format_metric_value("fakeout_count", 3) == "3"


# --- live record-chase ---


def test_live_metrics_empty_before_session_started():
    tracker = ScoreTracker()
    assert tracker.live_metrics() == {}


def test_live_metrics_reflects_running_session(monkeypatch):
    tracker = ScoreTracker()
    tracker.session_started()
    tracker.session_start_time = 0.0
    tracker.beat()
    tracker.beat()
    tracker.fake_climax_triggered()
    monkeypatch.setattr(time, "time", lambda: 10.0)

    live = tracker.live_metrics()

    assert live["total_dur_sec"] == pytest.approx(10.0)
    assert live["total_num_beat"] == 2
    assert live["fakeout_count"] == 1
    assert live["average_beat_speed_active"] == pytest.approx(2 / 10.0)


def test_live_metrics_excludes_completed_pause_duration(monkeypatch):
    tracker = ScoreTracker()
    tracker.session_started()
    tracker.session_start_time = 0.0
    tracker.beat_count = 4
    tracker.total_duration_of_pauses = 6.0
    monkeypatch.setattr(time, "time", lambda: 10.0)

    live = tracker.live_metrics()

    # active_time = 10s elapsed - 6s paused = 4s active
    assert live["average_beat_speed_active"] == pytest.approx(4 / 4.0)


def test_record_chase_status_none_without_session_running():
    tracker = ScoreTracker()
    assert tracker.record_chase_status({"total_num_beat": 100}) is None


def test_record_chase_status_none_without_any_bests(monkeypatch):
    tracker = ScoreTracker()
    tracker.session_started()
    tracker.session_start_time = 0.0
    monkeypatch.setattr(time, "time", lambda: 5.0)

    assert tracker.record_chase_status({}) is None


def test_record_chase_status_none_below_reveal_threshold(monkeypatch):
    tracker = ScoreTracker()
    tracker.session_started()
    tracker.session_start_time = 0.0
    tracker.beat_count = 50  # 50% of the record - below the 0.8 reveal threshold
    monkeypatch.setattr(time, "time", lambda: 1.0)

    assert tracker.record_chase_status({"total_num_beat": 100}) is None


def test_record_chase_status_selects_highest_progress_metric(monkeypatch):
    tracker = ScoreTracker()
    tracker.session_started()
    tracker.session_start_time = 0.0
    tracker.beat_count = 85  # 85% of its record
    monkeypatch.setattr(time, "time", lambda: 1.0)

    best_values = {"total_num_beat": 100, "fakeout_count": 100}  # fakeout_count stays at 0%
    status = tracker.record_chase_status(best_values)

    assert status == ("total_num_beat", 85, 100)


def test_record_chase_status_prioritizes_already_broken_metric(monkeypatch):
    tracker = ScoreTracker()
    tracker.session_started()
    tracker.session_start_time = 0.0
    tracker.beat_count = 90  # 90% of its record - closer in absolute terms
    tracker.fakeout_count = 3  # already exceeds its record of 2
    monkeypatch.setattr(time, "time", lambda: 1.0)

    best_values = {"total_num_beat": 100, "fakeout_count": 2}
    status = tracker.record_chase_status(best_values)

    assert status == ("fakeout_count", 3, 2)


def test_record_chase_status_ignores_zero_or_missing_bests(monkeypatch):
    tracker = ScoreTracker()
    tracker.session_started()
    tracker.session_start_time = 0.0
    tracker.beat_count = 90
    monkeypatch.setattr(time, "time", lambda: 1.0)

    best_values = {"total_num_beat": 0, "fakeout_count": None}
    assert tracker.record_chase_status(best_values) is None


def test_record_chase_status_ignores_average_beat_speed_active(monkeypatch):
    # Instantaneous speed right at session start can spike above the eventual session
    # average (small active_time denominator), so it's excluded from the live chase even
    # when its ratio would otherwise dominate every other metric.
    tracker = ScoreTracker()
    tracker.session_started()
    tracker.session_start_time = 0.0
    tracker.beat_count = 10  # 10 beats in 1s = 10 beats/sec, wildly over a 0.5 beats/sec best
    monkeypatch.setattr(time, "time", lambda: 1.0)

    best_values = {"average_beat_speed_active": 0.5, "total_num_beat": 1000}
    assert tracker.record_chase_status(best_values) is None
