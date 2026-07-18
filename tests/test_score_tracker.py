import time

import pytest

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
