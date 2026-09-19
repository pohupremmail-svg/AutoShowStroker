import json
import time

import pytest
from PyQt6.QtCore import QSettings

from src.BeatHandler import BeatHandler, Segment
from src.user_data import UserDataStore


@pytest.fixture
def handler(qtbot):
    h = BeatHandler()
    yield h
    h.stop()


def test_default_selected_patterns_is_all_patterns(handler):
    assert set(handler.selected_beat_patterns) == set(BeatHandler.BEAT_PATTERNS_MAP.keys())


def test_defaults_dict_matches_init_defaults(handler):
    for var_name, default_value in BeatHandler.DEFAULTS.items():
        assert getattr(handler, var_name) == default_value


def test_settings_override_defaults(tmp_path):
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

    assert handler.min_beat_freq == 2.0
    assert handler.max_beat_freq == 2.0
    assert handler.selected_beat_patterns == ["Standard Beat"]
    assert handler.ramping_active is False
    assert handler.min_ramp_duration == 111.0
    assert handler.max_ramp_duration == 222.0
    assert handler.ramp_window_width == 0.25
    handler.stop()


def test_set_muted_mutes_and_unmutes_sound_effect(handler):
    handler.set_muted(True)
    assert handler.is_muted is True
    assert handler.sound_effect.muted is True

    handler.set_muted(False)
    assert handler.is_muted is False
    assert handler.sound_effect.muted is False


def test_beat_segment_only_picks_from_selected_patterns(handler):
    handler.selected_beat_patterns = ["Standard Beat"]
    handler.start_beat()
    assert handler.current_beat_pattern == BeatHandler.BEAT_PATTERNS_MAP["Standard Beat"]


def test_beat_segment_frequency_within_bounds(handler):
    handler.min_beat_freq = 1.0
    handler.max_beat_freq = 1.0
    handler.start_beat()
    assert handler.cur_freq == 1.0


def test_beat_segment_emits_beat_change_event(handler, qtbot):
    with qtbot.waitSignal(handler.beat_change_event, timeout=1000) as blocker:
        handler.start_beat()
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
    assert handler.cur_freq != 0  # the next planned segment goes on the air right away


def test_toggle_blink_alternates_state(handler, qtbot):
    handler.is_red = False
    with qtbot.waitSignal(handler.beat_meter_update_event, timeout=1000) as blocker:
        handler.toggle_blink()
    assert handler.is_red is True
    assert blocker.args == ["DOWN", "down"]

    with qtbot.waitSignal(handler.beat_meter_update_event, timeout=1000) as blocker:
        handler.toggle_blink()
    assert handler.is_red is False
    assert blocker.args == ["UP", "up"]


def test_stop_emits_idle_beat_meter_reset(handler, qtbot):
    with qtbot.waitSignal(handler.beat_meter_update_event, timeout=1000) as blocker:
        handler.stop()
    assert blocker.args == ["Strokemeter appears here.", "idle"]


def test_beat_segment_emits_new_beat_meter_update(handler, qtbot):
    with qtbot.waitSignal(handler.beat_meter_update_event, timeout=1000) as blocker:
        handler.start_beat()
    text, kind = blocker.args
    assert text == f"New Beat! {handler.current_beat_pattern}"
    assert kind == "new_beat"


def test_start_pause_emits_pause_meter_update(handler, qtbot):
    handler.min_pause_dur = 5
    handler.max_pause_dur = 5
    with qtbot.waitSignal(handler.beat_meter_update_event, timeout=1000) as blocker:
        handler.start_pause()
    assert blocker.args == ["Pause: 5 seconds left.", "pause"]


def test_pause_loop_emits_pause_meter_update_on_tick(handler, qtbot):
    handler.cur_pause_dur = 3
    with qtbot.waitSignal(handler.beat_meter_update_event, timeout=1000) as blocker:
        handler.pause_loop()
    assert blocker.args == ["Pause: 2 seconds left.", "pause"]


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


def test_beat_interval_matches_freq_for_standard_beat(handler):
    handler.selected_beat_patterns = ["Standard Beat"]  # [1]
    handler.min_beat_freq = 2.0
    handler.max_beat_freq = 2.0
    handler.start_beat()
    assert handler.beat_meter_timer.interval() == int(1000 / 2.0)


def test_beat_interval_normalizes_audible_rate_for_sparse_pattern(handler):
    handler.selected_beat_patterns = ["Slow Pulse"]  # [1, 1, -1, -1, -1, -1, -1, -1]
    handler.min_beat_freq = 2.0
    handler.max_beat_freq = 2.0
    handler.start_beat()

    audible_count = 2
    inv_sum = 8  # all eight steps have abs value 1
    base_step_sec = audible_count / (2.0 * inv_sum)
    expected_ms = int(base_step_sec * 1000 / 1)  # position 0 has value 1
    assert handler.beat_meter_timer.interval() == expected_ms


# --- custom beat patterns ---


def test_custom_pattern_merges_into_available_patterns(handler):
    handler.add_or_update_custom_pattern("My Pattern", [1, -1, 2])
    assert handler.available_beat_patterns["My Pattern"] == [1, -1, 2]
    assert "Standard Beat" in handler.available_beat_patterns  # built-ins untouched


def test_add_custom_pattern_auto_selects_it(handler):
    handler.add_or_update_custom_pattern("My Pattern", [1, -1, 2])
    assert "My Pattern" in handler.selected_beat_patterns


def test_add_custom_pattern_persists_to_the_data_store(tmp_path):
    ini = tmp_path / "settings.ini"
    settings = QSettings(str(ini), QSettings.Format.IniFormat)
    store = UserDataStore(base_dir=tmp_path / "appdata")
    handler = BeatHandler(settings=settings, data_store=store)

    handler.add_or_update_custom_pattern("My Pattern", [1, -1, 2])

    assert json.loads(store.path_for("custom_patterns").read_text(encoding="utf-8")) == {
        "My Pattern": [1, -1, 2]
    }
    # The *selection* of which rhythms are active stays config, so it stays in QSettings.
    assert "My Pattern" in settings.value("BeatHandler/selected_beat_patterns")
    handler.stop()


def test_update_existing_custom_pattern_does_not_duplicate_selection(handler):
    handler.add_or_update_custom_pattern("My Pattern", [1, -1, 2])
    handler.add_or_update_custom_pattern("My Pattern", [2, -2])
    assert handler.available_beat_patterns["My Pattern"] == [2, -2]
    assert handler.selected_beat_patterns.count("My Pattern") == 1


def test_custom_patterns_loaded_from_the_data_store(tmp_path):
    store = UserDataStore(base_dir=tmp_path / "appdata")
    store.save("custom_patterns", {"Loaded": [1, -1]})

    handler = BeatHandler(data_store=store)

    assert handler.available_beat_patterns["Loaded"] == [1, -1]
    handler.stop()


def test_custom_patterns_migrate_out_of_settings_on_first_load(tmp_path):
    ini = tmp_path / "settings.ini"
    settings = QSettings(str(ini), QSettings.Format.IniFormat)
    settings.setValue("BeatHandler/custom_patterns", json.dumps({"Legacy": [1, -1]}))
    store = UserDataStore(base_dir=tmp_path / "appdata")

    handler = BeatHandler(settings=settings, data_store=store)

    assert handler.available_beat_patterns["Legacy"] == [1, -1]
    assert store.path_for("custom_patterns").exists()
    assert settings.value("BeatHandler/custom_patterns") is None
    handler.stop()


def test_delete_custom_pattern_removes_it_everywhere(handler):
    handler.add_or_update_custom_pattern("My Pattern", [1, -1, 2])
    handler.delete_custom_pattern("My Pattern")
    assert "My Pattern" not in handler.available_beat_patterns
    assert "My Pattern" not in handler.selected_beat_patterns
    assert "My Pattern" not in handler.custom_beat_patterns


@pytest.mark.parametrize(
    "steps",
    [
        [],
        [-1, -2],  # no audible step
        [0, 1],  # zero not allowed
        [1, 5],  # magnitude out of 1-4 range
    ],
)
def test_add_custom_pattern_rejects_invalid_steps(handler, steps):
    with pytest.raises(ValueError):
        handler.add_or_update_custom_pattern("Bad Pattern", steps)
    assert "Bad Pattern" not in handler.available_beat_patterns


def test_add_custom_pattern_rejects_name_colliding_with_builtin(handler):
    with pytest.raises(ValueError):
        handler.add_or_update_custom_pattern("Standard Beat", [1, -1])


def test_add_custom_pattern_rejects_empty_name(handler):
    with pytest.raises(ValueError):
        handler.add_or_update_custom_pattern("", [1, -1])


def test_beat_segment_emits_pattern_name(handler, qtbot):
    handler.selected_beat_patterns = ["Standard Beat"]
    with qtbot.waitSignal(handler.beat_change_event, timeout=1000) as blocker:
        handler.start_beat()
    _freq, pattern_name = blocker.args
    assert pattern_name == "Standard Beat"


# --- beat lookahead (drives the animated note highway) ---


def _arm(handler, pattern, freq=1.0, position=0, remaining_ms=100):
    """Puts the handler in a deterministic 'mid-session' state for lookahead tests."""
    handler.current_beat_pattern = pattern
    handler.current_beat_pattern_name = "Test"
    handler.cur_freq = freq
    handler.current_beat_position = position
    handler._pattern_audible_count = sum(1 for v in pattern if v > 0)
    handler._pattern_inv_sum = sum(1 / abs(v) for v in pattern)
    handler.beat_meter_timer.start(remaining_ms)


def _arm_before_a_boundary(handler, next_segment, ends_in=0.4, pattern=None, freq=1.0, remaining_ms=100):
    """As _arm, plus a running segment that ends in `ends_in` seconds and a plan holding
    `next_segment` behind it."""
    _arm(handler, pattern or [1], freq=freq, remaining_ms=remaining_ms)
    handler._current_segment = Segment("beat", 10.0, freq, "Test", 0)
    handler._current_segment_end = time.time() + ends_in
    handler._plan.clear()
    handler._plan.append(next_segment)


def test_upcoming_beats_empty_when_timer_inactive(handler):
    _arm(handler, [1])
    handler.beat_meter_timer.stop()
    assert handler.upcoming_beats(2.0) == []


def test_upcoming_beats_empty_before_any_pattern_selected(handler):
    assert handler.upcoming_beats(2.0) == []


def test_upcoming_beats_empty_when_freq_is_zero(handler):
    _arm(handler, [1], freq=0)
    assert handler.upcoming_beats(2.0) == []


def test_upcoming_beats_first_entry_tracks_remaining_time(handler):
    _arm(handler, [1], freq=1.0, remaining_ms=250)
    upcoming = handler.upcoming_beats(2.0)
    assert upcoming
    assert upcoming[0][0] == pytest.approx(0.25, abs=0.05)


def test_upcoming_beats_spacing_matches_frequency(handler):
    # Standard Beat at 1 Hz: one audible step per second.
    _arm(handler, [1], freq=1.0, remaining_ms=0)
    upcoming = handler.upcoming_beats(3.5)
    times = [t for t, _audible, _weight in upcoming]
    assert len(times) == 4  # 0.0, 1.0, 2.0, 3.0
    for earlier, later in zip(times, times[1:], strict=False):  # pairwise: last has no successor
        assert later - earlier == pytest.approx(1.0, abs=0.01)


def test_upcoming_beats_heavier_weight_is_a_shorter_step(handler):
    # Weight 2 lasts half as long as weight 1 - BeatHandler's inverse-duration encoding.
    _arm(handler, [1, 2], freq=1.0, position=0, remaining_ms=0)
    times = [t for t, _a, _w in handler.upcoming_beats(3.0)]
    first_gap = times[1] - times[0]  # follows the weight-1 step
    second_gap = times[2] - times[1]  # follows the weight-2 step
    assert second_gap == pytest.approx(first_gap / 2, abs=0.01)


def test_upcoming_beats_audibility_follows_current_position(handler):
    # Audibility of the note landing at t is pattern[current_beat_position], and the
    # interval after it comes from that same index (BeatHandler's existing off-by-one).
    _arm(handler, [1, -1, 1], position=1, remaining_ms=0)
    audible = [a for _t, a, _w in handler.upcoming_beats(5.0)]
    assert audible[0] is False  # pattern[1] == -1
    assert audible[1] is True   # pattern[2] == 1
    assert audible[2] is True   # wraps to pattern[0] == 1


def test_upcoming_beats_reports_step_weight(handler):
    _arm(handler, [1, -3], position=0, remaining_ms=0)
    weights = [w for _t, _a, w in handler.upcoming_beats(5.0)]
    assert weights[0] == 1
    assert weights[1] == 3


def test_upcoming_beats_respects_horizon(handler):
    _arm(handler, [1], freq=1.0, remaining_ms=0)
    assert len(handler.upcoming_beats(1.5)) == 2  # 0.0 and 1.0 only
    assert len(handler.upcoming_beats(0.5)) == 1


def test_upcoming_beats_does_not_mutate_handler_state(handler):
    _arm(handler, [1, 2, -1], position=1, remaining_ms=50)
    before = handler.current_beat_position

    handler.upcoming_beats(10.0)

    assert handler.current_beat_position == before


def test_upcoming_beats_is_capped_for_pathological_input(handler):
    # A very high frequency over a long horizon must not produce an unbounded list.
    _arm(handler, [4], freq=200.0, remaining_ms=0)
    assert len(handler.upcoming_beats(1000.0)) <= BeatHandler.MAX_LOOKAHEAD_NOTES


# --- lookahead across a segment boundary ---


def test_upcoming_beats_spaces_the_next_segment_by_its_own_frequency(handler):
    """The note that ends a segment is the last one at the old spacing; the gap after it
    already belongs to the next segment."""
    _arm_before_a_boundary(handler, Segment("beat", 10.0, 4.0, "Standard Beat", 1), ends_in=0.4)

    times = [round(t, 3) for t, _a, _w in handler.upcoming_beats(2.0)]

    # 0.1 and 1.1 at 1 Hz (1.1 is the note that trips the boundary), then 0.25s apart.
    assert times == [0.1, 1.1, 1.35, 1.6, 1.85]


def test_upcoming_beats_keeps_the_boundary_note_on_the_old_pattern(handler):
    """beat() plays the note using the pattern still on the air and only then moves on, so
    the note landing on the boundary is the old rhythm's. Reading it off the next segment
    instead drew a marker on the hit zone for a step that is actually silent."""
    # [1, -1] at 1 Hz: audible at 0.1, silent at 0.6 - and 0.6 is the boundary note.
    _arm_before_a_boundary(
        handler, Segment("beat", 10.0, 1.0, "Standard Beat", 1), ends_in=0.4, pattern=[1, -1], freq=1.0
    )

    upcoming = handler.upcoming_beats(2.0)

    assert [(round(t, 3), a) for t, a, _w in upcoming] == [(0.1, True), (0.6, False), (1.6, True)]


def test_upcoming_beats_stops_at_a_planned_pause(handler):
    _arm_before_a_boundary(handler, Segment("pause", 5.0, None, None, 1), ends_in=0.4)
    times = [t for t, _a, _w in handler.upcoming_beats(2.0)]
    assert times == pytest.approx([0.1, 1.1])


def test_upcoming_beats_stops_where_the_plan_runs_out(handler):
    _arm(handler, [1], freq=1.0)
    handler._current_segment = Segment("beat", 10.0, 1.0, "Test", 0)
    handler._current_segment_end = time.time() + 0.4
    handler._plan.clear()
    assert [t for t, _a, _w in handler.upcoming_beats(2.0)] == pytest.approx([0.1, 1.1])


def test_upcoming_beats_skips_a_segment_whose_pattern_was_deleted(handler):
    _arm_before_a_boundary(handler, Segment("beat", 10.0, 4.0, "Ghost Pattern", 1), ends_in=0.4)
    assert [t for t, _a, _w in handler.upcoming_beats(2.0)] == pytest.approx([0.1, 1.1])


# --- P0 crash paths: an unusable selection, inverted bounds, a corrupt pattern file ---


def _mutex_is_free(handler):
    """True if beat_pattern_mutex is not currently held.

    A non-recursive QMutex left locked by a raising method would block the next
    BeatTrackWidget.paintEvent -> upcoming_beats() on the same thread, i.e. freeze the GUI.
    """
    if handler.beat_pattern_mutex.tryLock():
        handler.beat_pattern_mutex.unlock()
        return True
    return False


def test_beat_segment_falls_back_when_nothing_is_selected(handler):
    handler.selected_beat_patterns = []
    handler.start_beat()
    assert handler.current_beat_pattern_name in BeatHandler.BEAT_PATTERNS_MAP


def test_beat_segment_skips_selected_patterns_that_no_longer_exist(handler):
    handler.selected_beat_patterns = ["Ghost Pattern", "Standard Beat"]
    handler.start_beat()
    planned = [handler.current_segment, *handler.planned_segments]
    assert {s.pattern_name for s in planned if s.kind != "pause"} == {"Standard Beat"}


def test_beat_segment_falls_back_when_every_selected_pattern_is_gone(handler):
    handler.selected_beat_patterns = ["Ghost Pattern"]
    handler.start_beat()
    assert handler.current_beat_pattern_name in BeatHandler.BEAT_PATTERNS_MAP


def test_beat_segment_treats_a_single_stored_string_as_one_name(handler):
    # A one-element QStringList can come back from QSettings as a bare str.
    handler.selected_beat_patterns = "Standard Beat"
    handler.start_beat()
    assert handler.current_beat_pattern_name == "Standard Beat"


def test_start_beat_survives_an_empty_selection(handler):
    handler.selected_beat_patterns = []
    handler.start_beat()
    assert handler.beat_meter_timer.isActive()


def test_start_pause_survives_inverted_pause_bounds(handler):
    handler.min_pause_dur = 30
    handler.max_pause_dur = 5
    handler.start_pause()
    assert 5 <= handler.cur_pause_dur <= 30


def test_applying_a_beat_segment_releases_the_mutex_when_it_raises(handler):
    class Exploding(dict):
        def get(self, *_args):
            raise RuntimeError("boom")

    handler.available_beat_patterns = Exploding()
    with pytest.raises(RuntimeError):
        handler._apply_beat_segment(Segment("beat", 10.0, 1.0, "Standard Beat", 0))
    assert _mutex_is_free(handler)


def test_reset_beat_timer_releases_the_mutex_when_it_raises(handler, monkeypatch):
    handler.start_beat()

    def boom():
        raise RuntimeError("boom")

    monkeypatch.setattr(handler, "_base_step_sec", boom)
    with pytest.raises(RuntimeError):
        handler.reset_beat_timer()
    assert _mutex_is_free(handler)


def test_beat_releases_the_mutex_when_it_raises(handler):
    handler.current_beat_pattern = None
    with pytest.raises(TypeError):
        handler.beat()
    assert _mutex_is_free(handler)


@pytest.mark.parametrize(
    "bad_steps",
    [
        [1, 0, 2],       # a zero step divides by zero in _base_step_sec
        [1, "x"],        # non-numeric
        [1, None],
        [],              # empty pattern indexes out of range
        [-1, -2],        # no audible step at all
        [1, 9],          # weight out of the 1..4 range the editor enforces
        5,               # not a list
        "nope",
        {"a": 1},
    ],
)
def test_invalid_custom_patterns_are_dropped_on_load(tmp_path, bad_steps):
    store = UserDataStore(base_dir=tmp_path / "appdata")
    store.save("custom_patterns", {"Bad": bad_steps, "Good": [1, -1]})

    handler = BeatHandler(data_store=store)

    assert "Bad" not in handler.available_beat_patterns
    assert handler.available_beat_patterns["Good"] == [1, -1]
    handler.stop()


def test_non_dict_custom_patterns_file_is_ignored(tmp_path):
    store = UserDataStore(base_dir=tmp_path / "appdata")
    store.save("custom_patterns", ["not", "a", "dict"])

    handler = BeatHandler(data_store=store)

    assert handler.available_beat_patterns == BeatHandler.BEAT_PATTERNS_MAP
    handler.stop()


def test_start_beat_survives_a_corrupt_custom_patterns_file(tmp_path):
    store = UserDataStore(base_dir=tmp_path / "appdata")
    store.save("custom_patterns", {"Broken": [1, 0]})
    handler = BeatHandler(data_store=store)
    handler.selected_beat_patterns = ["Broken"]

    handler.start_beat()

    assert handler.current_beat_pattern_name in BeatHandler.BEAT_PATTERNS_MAP
    handler.stop()


def test_beat_loudness_is_restored_from_settings(tmp_path):
    ini = tmp_path / "settings.ini"
    settings = QSettings(str(ini), QSettings.Format.IniFormat)
    settings.setValue("BeatHandler/beat_loudness", 0.3)

    handler = BeatHandler(settings=settings)

    assert handler.beat_loudness == 0.3
    handler.stop()


def test_clear_custom_patterns_removes_them_but_keeps_the_builtins(tmp_path):
    store = UserDataStore(base_dir=tmp_path / "appdata")
    handler = BeatHandler(data_store=store)
    handler.add_or_update_custom_pattern("Mine", [1, -1])

    handler.clear_custom_patterns()

    assert handler.custom_beat_patterns == {}
    assert "Mine" not in handler.available_beat_patterns
    assert "Mine" not in handler.selected_beat_patterns
    assert "Standard Beat" in handler.available_beat_patterns
    assert not store.path_for("custom_patterns").exists()
    handler.stop()


def test_beat_timer_is_a_precise_timer(handler):
    """Qt's default CoarseTimer has a 5% tolerance and rides the ~15.6ms Windows tick -
    at 5 beats/sec that is ~8% jitter on the one signal that has to be rhythmically exact."""
    from PyQt6.QtCore import Qt

    assert handler.beat_meter_timer.timerType() == Qt.TimerType.PreciseTimer


def test_beat_sound_resolves_from_the_project_root_not_the_cwd(qapp, monkeypatch, tmp_path):
    from pathlib import Path as _Path

    from src.utils import get_project_root

    monkeypatch.chdir(tmp_path)
    captured = {}
    monkeypatch.setattr(BeatHandler, "init_beat_sound", lambda self, path: captured.setdefault("path", path))

    handler = BeatHandler()

    expected = get_project_root() / "res" / "mixkit-cool-interface-click-tone-2568.wav"
    assert _Path(captured["path"]) == expected
    handler.stop()


def test_settings_group_is_an_explicit_constant():
    """SettingsDialog used to derive the QSettings key from __class__.__name__, so renaming
    the class silently orphaned every user's saved values."""
    assert BeatHandler.SETTINGS_GROUP == "BeatHandler"


def test_is_paused_reports_the_pause_phase(handler):
    assert handler.is_paused() is False

    handler.start_pause()
    assert handler.is_paused() is True

    handler.beat_meter_pause_timer.stop()
    assert handler.is_paused() is False


def test_a_pause_lasting_a_hair_under_a_whole_second_is_not_cut_short(handler):
    """A replayed pause carries the length it was *measured* at, not the whole number it was
    drawn as - and truncating 1.9987 to 1 made every replayed pause up to a second shorter
    than the one it was replaying."""
    handler._current_segment = Segment("pause", 1.9987, None, None, 0)

    handler.start_pause()

    assert handler.cur_pause_dur == 2


def test_a_pause_never_counts_down_from_zero(handler):
    """int() on a sub-second measurement gave 0, and pause_loop() would end the pause before
    it began."""
    handler._current_segment = Segment("pause", 0.4, None, None, 0)

    handler.start_pause()

    assert handler.cur_pause_dur == 1
