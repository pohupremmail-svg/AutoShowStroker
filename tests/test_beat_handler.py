import pytest
from PyQt6.QtCore import QSettings

from src.BeatHandler import BeatHandler


@pytest.fixture
def handler(qtbot):
    h = BeatHandler()
    qtbot.addWidget(h.beat_meter)
    yield h
    h.stop()


def test_default_selected_patterns_is_all_patterns(handler):
    assert set(handler.selected_beat_patterns) == set(BeatHandler.BEAT_PATTERNS_MAP.keys())


def test_settings_override_defaults(qtbot, tmp_path):
    ini = tmp_path / "settings.ini"
    settings = QSettings(str(ini), QSettings.Format.IniFormat)
    settings.setValue("BeatHandler/min_beat_freq", 2.0)
    settings.setValue("BeatHandler/max_beat_freq", 2.0)
    settings.setValue("BeatHandler/selected_beat_patterns", ["Standard Beat"])
    settings.setValue("BeatHandler/ramping_active", False)
    settings.setValue("BeatHandler/min_ramp_duration", 111.0)
    settings.setValue("BeatHandler/max_ramp_duration", 222.0)
    settings.setValue("BeatHandler/ramp_window_width", 0.25)

    handler = BeatHandler(settings=settings)
    qtbot.addWidget(handler.beat_meter)

    assert handler.min_beat_freq == 2.0
    assert handler.max_beat_freq == 2.0
    assert handler.selected_beat_patterns == ["Standard Beat"]
    assert handler.ramping_active is False
    assert handler.min_ramp_duration == 111.0
    assert handler.max_ramp_duration == 222.0
    assert handler.ramp_window_width == 0.25
    handler.stop()


def test_recalc_beat_only_picks_from_selected_patterns(handler):
    handler.selected_beat_patterns = ["Standard Beat"]
    handler.recalc_beat()
    assert handler.current_beat_pattern == BeatHandler.BEAT_PATTERNS_MAP["Standard Beat"]


def test_recalc_beat_frequency_within_bounds(handler):
    handler.min_beat_freq = 1.0
    handler.max_beat_freq = 1.0
    handler.recalc_beat()
    assert handler.cur_freq == 1.0


def test_recalc_beat_emits_beat_change_event(handler, qtbot):
    with qtbot.waitSignal(handler.beat_change_event, timeout=1000) as blocker:
        handler.recalc_beat()
    freq, _pattern_str = blocker.args
    assert freq == handler.cur_freq


def test_start_pause_duration_within_bounds(handler):
    handler.min_pause_dur = 5
    handler.max_pause_dur = 5
    handler.start_pause()
    assert handler.cur_pause_dur == 5


def test_start_pause_emits_paused_event(handler, qtbot):
    with qtbot.waitSignal(handler.beat_paused_event, timeout=1000):
        handler.start_pause()


def test_pause_loop_counts_down_without_resuming(handler):
    handler.cur_pause_dur = 3
    handler.pause_loop()
    assert handler.cur_pause_dur == 2


def test_pause_loop_resumes_and_resets_frequency(handler, qtbot):
    handler.cur_pause_dur = 1
    with qtbot.waitSignal(handler.beat_resumed_event, timeout=1000):
        handler.pause_loop()
    assert handler.cur_freq != 0  # reset_beat_timer immediately recalculates a new beat


def test_toggle_blink_alternates_state(handler):
    handler.is_red = False
    handler.toggle_blink()
    assert handler.is_red is True
    assert handler.beat_meter.text() == "DOWN"
    handler.toggle_blink()
    assert handler.is_red is False
    assert handler.beat_meter.text() == "UP"


def test_stop_resets_beat_meter_text(handler):
    handler.beat_meter.setText("something")
    handler.stop()
    assert handler.beat_meter.text() == "Strokemeter appears here."


# --- difficulty ramping ---


def test_current_freq_range_full_corridor_when_ramping_inactive(handler):
    handler.min_beat_freq = 1.0
    handler.max_beat_freq = 5.0
    handler.ramping_active = False
    assert handler._current_freq_range() == (1.0, 5.0)


def test_current_freq_range_full_corridor_before_start_beat_called(handler):
    handler.min_beat_freq = 1.0
    handler.max_beat_freq = 5.0
    handler.ramping_active = True
    assert handler.ramp_target_duration == 0.0
    assert handler._current_freq_range() == (1.0, 5.0)


def test_current_freq_range_collapses_when_corridor_has_zero_width(handler):
    handler.min_beat_freq = 3.0
    handler.max_beat_freq = 3.0
    handler.ramping_active = True
    handler.min_ramp_duration = 10.0
    handler.max_ramp_duration = 10.0
    handler.start_beat()
    assert handler._current_freq_range() == (3.0, 3.0)


def test_current_freq_range_sits_at_bottom_at_ramp_start(handler, monkeypatch):
    handler.min_beat_freq = 1.0
    handler.max_beat_freq = 5.0
    handler.ramping_active = True
    handler.ramp_window_width = 0.4
    handler.min_ramp_duration = 100.0
    handler.max_ramp_duration = 100.0

    monkeypatch.setattr("src.BeatHandler.time.time", lambda: 1000.0)
    handler.start_beat()

    window_min, window_max = handler._current_freq_range()
    assert window_min == pytest.approx(1.0)
    assert window_max == pytest.approx(2.6)


def test_current_freq_range_slides_up_partway_through_ramp(handler, monkeypatch):
    handler.min_beat_freq = 1.0
    handler.max_beat_freq = 5.0
    handler.ramping_active = True
    handler.ramp_window_width = 0.4
    handler.min_ramp_duration = 100.0
    handler.max_ramp_duration = 100.0

    monkeypatch.setattr("src.BeatHandler.time.time", lambda: 1000.0)
    handler.start_beat()

    monkeypatch.setattr("src.BeatHandler.time.time", lambda: 1050.0)  # halfway (progress=0.5)
    window_min, window_max = handler._current_freq_range()
    corridor = 4.0
    width = 0.4 * corridor
    expected_min = 1.0 + 0.5 * (corridor - width)
    assert window_min == pytest.approx(expected_min)
    assert window_max == pytest.approx(expected_min + width)


def test_current_freq_range_caps_at_top_once_ramp_target_elapsed(handler, monkeypatch):
    handler.min_beat_freq = 1.0
    handler.max_beat_freq = 5.0
    handler.ramping_active = True
    handler.ramp_window_width = 0.4
    handler.min_ramp_duration = 100.0
    handler.max_ramp_duration = 100.0

    monkeypatch.setattr("src.BeatHandler.time.time", lambda: 1000.0)
    handler.start_beat()

    monkeypatch.setattr("src.BeatHandler.time.time", lambda: 5000.0)  # way past target
    window_min, window_max = handler._current_freq_range()
    assert window_max == pytest.approx(5.0)
    assert window_min == pytest.approx(3.4)


def test_start_beat_draws_ramp_target_duration_from_configured_range(handler):
    handler.min_ramp_duration = 42.0
    handler.max_ramp_duration = 42.0
    handler.start_beat()
    assert handler.ramp_target_duration == 42.0


# --- BPM normalization (cur_freq == real audible beats/sec, independent of pattern shape) ---


def test_reset_beat_timer_interval_matches_freq_for_standard_beat(handler):
    handler.selected_beat_patterns = ["Standard Beat"]  # [1]
    handler.min_beat_freq = 2.0
    handler.max_beat_freq = 2.0
    handler.recalc_beat()
    handler.reset_beat_timer()
    assert handler.beat_meter_timer.interval() == int(1000 / 2.0)


def test_reset_beat_timer_interval_normalizes_audible_rate_for_sparse_pattern(handler):
    handler.selected_beat_patterns = ["Slow Pulse"]  # [1, 1, -1, -1, -1, -1, -1, -1]
    handler.min_beat_freq = 2.0
    handler.max_beat_freq = 2.0
    handler.recalc_beat()
    handler.reset_beat_timer()

    audible_count = 2
    inv_sum = 8  # all eight steps have abs value 1
    base_step_sec = audible_count / (2.0 * inv_sum)
    expected_ms = int(base_step_sec * 1000 / 1)  # position 0 has value 1
    assert handler.beat_meter_timer.interval() == expected_ms
