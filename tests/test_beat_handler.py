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

    handler = BeatHandler(settings=settings)
    qtbot.addWidget(handler.beat_meter)

    assert handler.min_beat_freq == 2.0
    assert handler.max_beat_freq == 2.0
    assert handler.selected_beat_patterns == ["Standard Beat"]
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
