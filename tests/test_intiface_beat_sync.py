import time

import pytest

from src.BeatHandler import BeatHandler


@pytest.fixture
def beat(qsettings, qtbot):
    beat = BeatHandler(settings=qsettings)
    beat.ramping_active = False
    beat.min_beat_freq = beat.max_beat_freq = 2.0
    beat.min_beat_dur = beat.max_beat_dur = 30.0
    beat.pause_chance = 0
    beat.selected_beat_patterns = ["Standard Beat"]
    yield beat
    beat.stop()


def test_prediction_is_emitted_before_first_beat(beat):
    predictions, beats = [], []
    beat.linear_movement_planned.connect(lambda *args: predictions.append(args))
    beat.beat_event.connect(lambda: beats.append(True))
    now = time.monotonic()
    beat.start_beat()
    assert not beats
    assert len(predictions) == 1
    assert 0.45 <= predictions[0][2] - now <= 0.55


def test_targets_alternate_and_match_visible_up_down(beat):
    predictions, display = [], []
    beat.linear_movement_planned.connect(lambda *args: predictions.append(args))
    beat.beat_meter_update_event.connect(lambda text, kind: display.append(kind))
    beat.start_beat()
    for _ in range(9):
        target = predictions[-1][1]
        display.clear()
        beat.beat()
        if "up" in display or "down" in display:
            assert target == ("up" in display)
    assert all(a[1] != b[1] for a, b in zip(predictions, predictions[1:], strict=False))


def test_silent_steps_extend_duration_to_next_audible_target(beat):
    beat.start_beat()
    beat.current_beat_pattern = [1, -1, -1, 1]
    beat.current_beat_position = 1
    beat._pattern_audible_count = 2
    beat._pattern_inv_sum = 4.0
    beat.beat_meter_timer.start(250)
    move = beat.next_linear_movement()
    assert move is not None
    assert 0.70 <= move[2] - time.monotonic() <= 0.76


def test_prediction_never_crosses_a_segment_boundary_on_a_rest(beat):
    beat.start_beat()
    beat.current_beat_pattern = [1, -1, -1, 1]
    beat.current_beat_position = 1
    beat._pattern_audible_count = 2
    beat._pattern_inv_sum = 4.0
    beat._current_segment_end = time.time() + 0.1
    beat.beat_meter_timer.start(250)
    assert beat.next_linear_movement() is None


def test_pause_and_stopped_engine_have_no_prediction(beat):
    beat.start_beat()
    beat.start_pause()
    assert beat.next_linear_movement() is None
    beat.stop()
    assert beat.next_linear_movement() is None
