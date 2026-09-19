"""The session plan: BeatHandler decides what the next segments are before they happen.

These tests never monkeypatch random - they pin the settings ranges to a single value so
the draw has only one possible outcome, which is both stricter and easier to read.
"""
import pytest

from src.BeatHandler import BeatHandler, Segment


@pytest.fixture
def handler(qtbot):
    h = BeatHandler()
    yield h
    h.stop()


def pin(handler, *, beat_dur=20.0, freq=2.0, pause_chance=0.0, pause_dur=5, ramping=False):
    """Collapses every range to a point, so the draws have only one outcome."""
    handler.min_beat_dur = handler.max_beat_dur = beat_dur
    handler.min_beat_freq = handler.max_beat_freq = freq
    handler.min_pause_dur = handler.max_pause_dur = pause_dur
    handler.pause_chance = pause_chance
    handler.ramping_active = ramping
    handler.selected_beat_patterns = ["Standard Beat"]


def freeze(monkeypatch, now):
    monkeypatch.setattr("src.BeatHandler.time.time", lambda: now)


# --- the buffer ---


def test_start_beat_fills_the_plan_buffer(handler):
    pin(handler)
    handler.start_beat()
    assert len(handler.planned_segments) == BeatHandler.PLAN_BUFFER_SEGMENTS


def test_start_beat_puts_the_first_segment_on_the_air(handler):
    pin(handler, freq=3.0)
    handler.start_beat()
    assert handler.current_segment.kind == "beat"
    assert handler.cur_freq == 3.0
    assert handler.current_beat_pattern_name == "Standard Beat"


def test_plan_refills_as_segments_are_consumed(handler):
    pin(handler)
    handler.start_beat()
    for _ in range(4):
        handler._begin_next_segment()
    assert len(handler.planned_segments) == BeatHandler.PLAN_BUFFER_SEGMENTS


def test_segment_indices_are_monotonic(handler):
    pin(handler)
    handler.start_beat()
    seen = [handler.current_segment.index]
    for _ in range(6):
        handler._begin_next_segment()
        seen.append(handler.current_segment.index)
    assert seen == list(range(len(seen)))


def test_replan_does_not_reuse_indices(handler):
    pin(handler)
    handler.start_beat()
    before = max(s.index for s in handler.planned_segments)
    handler.replan_from_next_segment()
    assert min(s.index for s in handler.planned_segments) > before


def test_plan_extended_event_carries_the_new_segments(handler, qtbot):
    pin(handler)
    with qtbot.waitSignal(handler.plan_extended_event, timeout=1000) as blocker:
        handler.start_beat()
    added = blocker.args[0]
    assert all(isinstance(s, Segment) for s in added)
    assert len(added) == BeatHandler.PLAN_BUFFER_SEGMENTS


def test_segment_started_event_carries_the_segment(handler, qtbot):
    """The whole Segment, not just its index - a consumer recording what actually played
    would otherwise have to reach back into the handler for the rest."""
    pin(handler)
    handler.start_beat()
    with qtbot.waitSignal(handler.segment_started_event, timeout=1000) as blocker:
        handler._begin_next_segment()
    assert blocker.args == [handler.current_segment]


def test_session_planned_event_reports_when_the_session_started(handler, qtbot, monkeypatch):
    pin(handler)
    freeze(monkeypatch, 1000.0)
    with qtbot.waitSignal(handler.session_planned_event, timeout=1000) as blocker:
        handler.start_beat()
    assert blocker.args == [1000.0, None]


def test_session_planned_event_fires_before_the_plan_is_built(handler):
    """Otherwise the run-in to the climax could already be planned by the time anyone
    gets to say where the climax is."""
    pin(handler)
    sizes = []
    handler.session_planned_event.connect(lambda _t: sizes.append(len(handler.planned_segments)))
    handler.start_beat()
    assert sizes == [0]


# --- segment contents ---


def test_beat_segment_duration_has_no_tail_beyond_the_configured_range(handler):
    handler.min_beat_dur = 10.0
    handler.max_beat_dur = 12.0
    handler.pause_chance = 0.0
    handler.selected_beat_patterns = ["Standard Beat"]
    handler.start_beat()
    durations = [handler.current_segment.duration_sec]
    for _ in range(40):
        handler._begin_next_segment()
        durations.append(handler.current_segment.duration_sec)
    assert all(10.0 <= d <= 12.0 for d in durations)


def test_pause_segments_use_the_pause_range(handler):
    pin(handler, pause_chance=1.0, pause_dur=7)
    handler.start_beat()
    pause = next(s for s in handler.planned_segments if s.kind == "pause")
    assert pause.duration_sec == 7


def test_a_session_never_opens_on_a_pause(handler):
    """start_beat() used to pick a beat outright - a session that begins by telling the
    user to take their hands off would be a new and unwelcome behaviour."""
    pin(handler, pause_chance=1.0)
    handler.start_beat()
    assert handler.current_segment.kind == "beat"


def test_a_pause_is_never_followed_by_another_pause(handler):
    pin(handler, pause_chance=1.0)
    handler.start_beat()
    kinds = [handler.current_segment.kind] + [s.kind for s in handler.planned_segments]
    assert "pause" in kinds
    assert not any(a == b == "pause" for a, b in zip(kinds, kinds[1:], strict=False))


def test_ramping_is_evaluated_at_each_segments_planned_start(handler, monkeypatch):
    """The whole point of planning ahead: the ramp becomes a curve the plan walks along,
    not a corridor sampled at whatever moment the dice happened to be thrown."""
    freeze(monkeypatch, 1000.0)
    handler.min_beat_freq, handler.max_beat_freq = 1.0, 5.0
    handler.min_beat_dur = handler.max_beat_dur = 20.0
    handler.min_ramp_duration = handler.max_ramp_duration = 400.0
    handler.ramp_window_width = 0.02  # a near-point window, so the draw is effectively fixed
    handler.ramping_active = True
    handler.pause_chance = 0.0
    handler.selected_beat_patterns = ["Standard Beat"]

    handler.start_beat()
    freqs = [handler.current_segment.freq] + [s.freq for s in handler.planned_segments]
    assert freqs == sorted(freqs)
    assert freqs[-1] > freqs[0]


# --- the finale ---


def _segment_covering(handler, moment, session_start=1000.0):
    """The planned segment spanning `moment`, or None if the plan does not reach it."""
    start = session_start
    for segment in [handler.current_segment, *handler.planned_segments]:
        end = start + segment.duration_sec
        if start <= moment < end:
            return segment
        start = end
    return None


def test_no_finale_is_planned_when_none_is_requested(handler):
    pin(handler)
    handler.start_beat()
    assert all(s.kind != "finale" for s in handler.planned_segments)


def test_the_finale_covers_the_moment_the_climax_is_announced(handler, monkeypatch):
    freeze(monkeypatch, 1000.0)
    pin(handler, beat_dur=20.0)
    handler.start_beat()
    handler.set_finale_at(1000.0 + 95.0)

    starts, finale = 1000.0, None
    for segment in [handler.current_segment, *handler.planned_segments]:
        if segment.kind == "finale":
            finale = (starts, starts + segment.duration_sec)
            break
        starts += segment.duration_sec
    assert finale is not None, "the planner never marked a run-in to the climax"
    assert finale[0] <= 1095.0 < finale[1]


def test_the_finale_runs_at_the_top_of_its_frequency_window(handler, monkeypatch):
    freeze(monkeypatch, 1000.0)
    handler.min_beat_freq, handler.max_beat_freq = 1.0, 5.0
    handler.min_beat_dur = handler.max_beat_dur = 20.0
    handler.ramping_active = False
    handler.pause_chance = 0.0
    handler.selected_beat_patterns = ["Standard Beat"]
    handler.start_beat()
    handler.set_finale_at(1000.0 + 95.0)

    finale = next(s for s in handler.planned_segments if s.kind == "finale")
    assert finale.freq == 5.0


def test_a_finale_inside_the_ramp_runs_at_the_top_of_the_window_it_is_in(handler, monkeypatch):
    """The climax can be set to land while the ramp is still climbing. "Fast" then means
    the fastest the ramp currently allows, not the absolute maximum - the run-in should be
    the hardest the session has been so far, not a jump out of its own difficulty curve."""
    freeze(monkeypatch, 1000.0)
    handler.min_beat_freq, handler.max_beat_freq = 1.0, 5.0
    handler.min_beat_dur = handler.max_beat_dur = 20.0
    handler.min_ramp_duration = handler.max_ramp_duration = 1000.0  # barely started
    handler.ramp_window_width = 0.4
    handler.ramping_active = True
    handler.pause_chance = 0.0
    handler.selected_beat_patterns = ["Standard Beat"]
    handler.start_beat()
    handler.set_finale_at(1095.0)

    finale = next(s for s in handler.planned_segments if s.kind == "finale")
    assert finale.freq < handler.max_beat_freq
    _window_min, window_max = handler._current_freq_range(at_time=1080.0)
    assert finale.freq == pytest.approx(window_max, abs=0.2)


def test_the_finale_is_never_a_pause(handler, monkeypatch):
    """Even with a pause due at every boundary, the moment the climax is announced is
    covered by a rhythm - being told to cum with your hands off would be nonsense."""
    freeze(monkeypatch, 1000.0)
    pin(handler, beat_dur=20.0, pause_chance=1.0)
    handler.start_beat()
    handler.set_finale_at(1060.0)

    covering = _segment_covering(handler, 1060.0)
    assert covering is not None
    assert covering.kind == "finale"


def test_the_finale_swallows_a_leftover_too_short_to_stand_alone(handler, monkeypatch):
    """4 x 20s lands on 80s, leaving 5s before an 85s climax - too short for a segment of
    its own, so the run-in is extended over it instead of a runt being planned."""
    freeze(monkeypatch, 1000.0)
    pin(handler, beat_dur=20.0)
    handler.start_beat()
    handler.set_finale_at(1000.0 + 85.0)

    planned = [handler.current_segment, *handler.planned_segments]
    assert all(s.duration_sec >= handler.min_beat_dur for s in planned)
    finale = next(s for s in planned if s.kind == "finale")
    assert finale.duration_sec > 20.0


def test_only_one_finale_is_planned(handler, monkeypatch):
    freeze(monkeypatch, 1000.0)
    pin(handler, beat_dur=20.0)
    handler.start_beat()
    handler.set_finale_at(1000.0 + 45.0)
    planned = [handler.current_segment, *handler.planned_segments]
    assert [s.kind for s in planned].count("finale") == 1


def test_a_segment_starts_exactly_as_it_was_planned(handler, monkeypatch):
    """Segments start at the first beat tick past their planned end, so they run late.
    Re-deriving the segment to account for that used to re-draw its pattern and frequency -
    and the beat track had already painted several of its notes, so the whole rhythm
    visibly jumped the instant the segment began."""
    freeze(monkeypatch, 1000.0)
    pin(handler, beat_dur=4.0)
    handler.start_beat()
    handler.set_finale_at(1015.0)
    next_planned = handler.planned_segments[0]

    freeze(monkeypatch, 1010.0)  # six seconds late
    handler._begin_next_segment()

    assert handler.current_segment == next_planned


def test_the_finale_segment_also_starts_exactly_as_it_was_planned(handler, monkeypatch):
    freeze(monkeypatch, 1000.0)
    pin(handler, beat_dur=4.0)
    handler.start_beat()
    handler.set_finale_at(1015.0)
    planned_finale = next(s for s in handler.planned_segments if s.kind == "finale")

    freeze(monkeypatch, 1010.0)
    handler._begin_next_segment()
    freeze(monkeypatch, 1016.0)
    handler._begin_next_segment()

    assert handler.current_segment == planned_finale
    assert handler.current_beat_pattern_name == planned_finale.pattern_name


def test_only_one_finale_survives_a_late_start(handler, monkeypatch):
    freeze(monkeypatch, 1000.0)
    pin(handler, beat_dur=4.0)
    handler.start_beat()
    handler.set_finale_at(1015.0)

    freeze(monkeypatch, 1010.0)
    handler._begin_next_segment()

    planned = [handler.current_segment, *handler.planned_segments]
    assert [s.kind for s in planned].count("finale") == 1


def test_planning_continues_normally_past_the_finale(handler, monkeypatch):
    freeze(monkeypatch, 1000.0)
    pin(handler, beat_dur=20.0)
    handler.start_beat()
    handler.set_finale_at(1000.0 + 45.0)
    planned = [handler.current_segment, *handler.planned_segments]
    after = planned[[s.kind for s in planned].index("finale") + 1:]
    assert after and all(s.kind == "beat" for s in after)


# --- replanning ---


def test_replan_leaves_the_running_segment_alone(handler):
    pin(handler, beat_dur=20.0)
    handler.start_beat()
    running = handler.current_segment
    handler.min_beat_dur = handler.max_beat_dur = 40.0
    handler.replan_from_next_segment()
    assert handler.current_segment is running
    assert all(s.duration_sec == 40.0 for s in handler.planned_segments)


def test_replan_keeps_the_finale_where_it_was(handler, monkeypatch):
    freeze(monkeypatch, 1000.0)
    pin(handler, beat_dur=20.0)
    handler.start_beat()
    handler.set_finale_at(1000.0 + 95.0)
    handler.replan_from_next_segment()
    assert handler._finale_at == 1095.0
    assert any(s.kind == "finale" for s in handler.planned_segments)


def test_replan_before_a_session_starts_does_nothing(handler):
    handler.replan_from_next_segment()
    assert handler.planned_segments == ()


def test_stop_clears_the_plan(handler):
    pin(handler)
    handler.start_beat()
    handler.stop()
    assert handler.planned_segments == ()
    assert handler.current_segment is None


# --- when the ramp tops out ---


def test_ramp_complete_at_is_none_before_the_session_starts(handler):
    assert handler.ramp_complete_at is None


def test_ramp_complete_at_reports_the_end_of_the_ramp(handler, monkeypatch):
    freeze(monkeypatch, 1000.0)
    pin(handler, ramping=True)
    handler.min_ramp_duration = handler.max_ramp_duration = 600.0
    handler.start_beat()
    assert handler.ramp_complete_at == 1600.0


def test_ramp_complete_at_is_none_while_ramping_is_switched_off(handler, monkeypatch):
    """There is no ramp to wait for - which is what keeps the Ramp duration sliders from
    influencing anything while their own checkbox is unticked."""
    freeze(monkeypatch, 1000.0)
    pin(handler, ramping=False)
    handler.min_ramp_duration = handler.max_ramp_duration = 600.0
    handler.start_beat()
    assert handler.ramp_complete_at is None


# --- holding the last segment once the climax has landed ---


def test_holding_the_final_segment_stops_segment_changes(handler, monkeypatch):
    """Once she has told you to cum, the rhythm she said it over is the one that stays.
    A new beat - or worse, a pause - landing on top of the climax reads as the app
    having moved on without you."""
    freeze(monkeypatch, 1000.0)
    pin(handler, beat_dur=20.0)
    handler.start_beat()
    running = handler.current_segment

    handler.hold_final_segment()
    freeze(monkeypatch, 1000.0 + 600.0)  # long past when the segment would have ended
    handler.reset_beat_timer()

    assert handler.current_segment is running
    assert handler.beat_meter_timer.isActive()


def test_holding_the_final_segment_empties_the_plan(handler):
    """Nothing further is decided, so nothing further is announced - which is what stops
    fake climaxes being rolled onto segments that will never start."""
    pin(handler)
    handler.start_beat()
    assert handler.planned_segments

    handler.hold_final_segment()

    assert handler.planned_segments == ()


def test_holding_the_final_segment_plans_no_further_pause(handler, monkeypatch):
    freeze(monkeypatch, 1000.0)
    pin(handler, beat_dur=20.0, pause_chance=1.0)
    handler.start_beat()

    handler.hold_final_segment()
    freeze(monkeypatch, 1000.0 + 600.0)
    handler.reset_beat_timer()

    assert not handler.is_paused()


def test_a_new_session_lifts_the_hold(handler):
    pin(handler)
    handler.start_beat()
    handler.hold_final_segment()

    handler.start_beat()

    assert handler.planned_segments


def test_the_track_stays_full_while_the_final_segment_is_held(handler, monkeypatch):
    """A held segment runs past its planned end, which made the lookahead treat every note
    as a segment boundary it could not cross - so the beat track emptied out to a single
    note for the whole rest of the session, right when the climax was on screen."""
    freeze(monkeypatch, 1000.0)
    pin(handler, beat_dur=20.0, freq=4.0)
    handler.start_beat()
    before = len(handler.upcoming_beats(2.5))
    assert before > 1

    handler.hold_final_segment()
    freeze(monkeypatch, 1000.0 + 600.0)  # long past where the segment would have ended

    assert len(handler.upcoming_beats(2.5)) == before


# --- replaying a saved session ---


def _script(segments, **overrides):
    from src.SessionScript import SessionScript

    saved = {"duration_sec": sum(s["duration_sec"] for s in segments), "segments": segments,
             "custom_patterns": {}, "climax": None, "fake_climaxes": [], "media": []}
    saved.update(overrides)
    return SessionScript(saved)


def test_a_scripted_session_replays_the_recorded_segments(handler):
    """The whole point of the rebuild: the planner reads a plan instead of drawing one."""
    pin(handler)
    script = _script([
        {"kind": "beat", "pattern": "Quick Swing", "freq": 2.3, "duration_sec": 30.0},
        {"kind": "pause", "pattern": None, "freq": None, "duration_sec": 12.0},
        {"kind": "beat", "pattern": "Slow Pulse", "freq": 4.1, "duration_sec": 25.0},
    ])

    handler.start_beat(script=script)

    planned = [handler.current_segment, *handler.planned_segments][:3]
    assert [(s.kind, s.pattern_name, s.freq, s.duration_sec) for s in planned] == [
        ("beat", "Quick Swing", 2.3, 30.0),
        ("pause", None, None, 12.0),
        ("beat", "Slow Pulse", 4.1, 25.0),
    ]


def test_a_scripted_session_ignores_the_configured_ranges(handler):
    """A replay is the recorded session, not the recorded session filtered through whatever
    the sliders happen to say today."""
    pin(handler, beat_dur=99.0, freq=1.0)
    script = _script([{"kind": "beat", "pattern": "Standard Beat", "freq": 3.3, "duration_sec": 7.0}])

    handler.start_beat(script=script)

    assert handler.current_segment.duration_sec == 7.0
    assert handler.cur_freq == 3.3


def test_the_planner_takes_over_once_the_script_is_spent(handler):
    """A replay that outlives its recording keeps playing rather than stopping dead."""
    pin(handler, beat_dur=20.0)
    script = _script([{"kind": "beat", "pattern": "Standard Beat", "freq": 2.0, "duration_sec": 5.0}])

    handler.start_beat(script=script)

    drawn = handler.planned_segments
    assert drawn, "nothing was planned after the script ran out"
    assert all(s.duration_sec == 20.0 for s in drawn)


def test_scripted_segments_keep_the_handlers_own_indices(handler):
    pin(handler)
    script = _script([
        {"kind": "beat", "pattern": "Standard Beat", "freq": 2.0, "duration_sec": 10.0},
        {"kind": "beat", "pattern": "Standard Beat", "freq": 2.0, "duration_sec": 10.0},
    ])

    handler.start_beat(script=script)

    planned = [handler.current_segment, *handler.planned_segments]
    assert [s.index for s in planned] == list(range(len(planned)))


def test_a_scripted_custom_pattern_is_registered_before_it_plays(handler):
    """Otherwise the replay reaches a rhythm the machine has never heard of and falls back
    to something else entirely."""
    pin(handler)
    script = _script(
        [{"kind": "beat", "pattern": "Borrowed", "freq": 2.0, "duration_sec": 10.0}],
        custom_patterns={"Borrowed": [1, -1, 2]},
    )

    handler.start_beat(script=script)

    assert handler.current_beat_pattern_name == "Borrowed"
    assert handler.current_beat_pattern == [1, -1, 2]


def test_a_scripted_custom_pattern_is_not_saved_to_the_users_library(handler, tmp_path):
    """A replay borrows someone else's rhythm for the session; it does not adopt it."""
    pin(handler)
    script = _script(
        [{"kind": "beat", "pattern": "Borrowed", "freq": 2.0, "duration_sec": 10.0}],
        custom_patterns={"Borrowed": [1, -1, 2]},
    )

    handler.start_beat(script=script)

    assert "Borrowed" not in handler.custom_beat_patterns


def test_starting_a_normal_session_afterwards_drops_the_script(handler):
    pin(handler, beat_dur=20.0)
    handler.start_beat(script=_script(
        [{"kind": "beat", "pattern": "Standard Beat", "freq": 3.3, "duration_sec": 7.0}]
    ))

    handler.start_beat()

    assert handler.current_segment.duration_sec == 20.0


def test_session_planned_event_carries_the_script_of_a_replay(handler, qtbot):
    """The climax learns a session is a replay from this signal and nowhere else."""
    pin(handler)
    script = _script([{"kind": "beat", "pattern": "Standard Beat", "freq": 2.0, "duration_sec": 10.0}])
    with qtbot.waitSignal(handler.session_planned_event, timeout=1000) as blocker:
        handler.start_beat(script=script)
    assert blocker.args[1] is script


# --- relief on demand ---


def test_an_edge_cuts_the_running_segment_short_for_a_pause(handler):
    pin(handler, beat_dur=60.0)
    handler.edge_pause_dur = 12
    handler.start_beat()
    assert handler.current_segment.kind == "beat"

    given = handler.edge_relief()

    assert given == pytest.approx(12.0)
    assert handler.current_segment.kind == "pause"
    assert handler.is_paused() is True


def test_the_rhythm_behind_an_edge_comes_back_at_the_bottom_of_the_window(handler):
    """A gap on its own is not relief - what follows has to be gentler than what drove the
    user to press the button."""
    pin(handler)
    handler.min_beat_freq, handler.max_beat_freq = 1.0, 5.0
    handler.start_beat()

    handler.edge_relief()

    assert handler.planned_segments[0].freq == pytest.approx(1.0)


def test_only_the_first_segment_after_an_edge_is_slowed(handler):
    pin(handler)
    handler.min_beat_freq, handler.max_beat_freq = 1.0, 1.0
    handler.start_beat()
    handler.edge_relief()
    handler.min_beat_freq, handler.max_beat_freq = 4.0, 4.0

    # Everything queued behind the relief segment is drawn normally again.
    handler._plan.clear()
    handler._extend_plan()

    assert handler.planned_segments[0].freq == pytest.approx(4.0)


def test_an_edge_during_a_pause_does_nothing(handler):
    """Nothing to be relieved of - and it would hand out a free second pause."""
    pin(handler, pause_chance=1.0, pause_dur=10)
    handler.start_beat()
    handler._begin_next_segment()
    assert handler.is_paused() is True

    assert handler.edge_relief() == 0.0


def test_an_edge_after_the_climax_does_nothing(handler):
    pin(handler)
    handler.start_beat()
    handler.hold_final_segment()

    assert handler.edge_relief() == 0.0


def test_an_edge_keeps_the_segment_counter_climbing(handler):
    """The pause is a real segment - the recorder and the explorer both index by it."""
    pin(handler)
    handler.start_beat()
    before = handler.current_segment.index

    handler.edge_relief()

    assert handler.current_segment.index > before


def test_an_edge_announces_its_pause_like_any_other_segment(handler, qtbot):
    pin(handler)
    handler.start_beat()

    with qtbot.waitSignal(handler.segment_started_event, timeout=1000) as blocker:
        handler.edge_relief()

    assert blocker.args[0].kind == "pause"


def test_an_edge_still_leaves_a_fast_run_in_to_the_climax(handler):
    """The run-in is rebuilt from the replan - a pause pressed just before the climax must
    not swallow it."""
    pin(handler, beat_dur=20.0)
    handler.min_beat_freq, handler.max_beat_freq = 1.0, 5.0
    handler.edge_pause_dur = 5
    handler.start_beat()
    handler.set_finale_at(handler.session_start_time + 40.0)

    handler.edge_relief()

    assert any(segment.kind == "finale" for segment in handler.planned_segments)


def test_an_edge_in_a_replay_keeps_the_recording_that_was_already_queued(handler):
    """The plan buffer holds segments already taken off the script, and the script cursor
    never rewinds - clearing the queue silently skipped five recorded segments and left the
    planner drawing its own from there."""
    pin(handler)
    script = _script([
        {"kind": "beat", "pattern": "Standard Beat", "freq": 2.0, "duration_sec": 5.0},
        {"kind": "beat", "pattern": "Quick Swing", "freq": 3.0, "duration_sec": 6.0},
        {"kind": "beat", "pattern": "Double Tap", "freq": 4.0, "duration_sec": 7.0},
    ])
    handler.start_beat(script=script)

    handler.edge_relief()

    # The break itself is on the air; the rest of the recording is untouched behind it.
    assert handler.current_segment.kind == "pause"
    assert [(s.pattern_name, s.freq) for s in handler.planned_segments][:2] == [
        ("Quick Swing", 3.0),
        ("Double Tap", 4.0),
    ]


def test_an_edge_past_the_end_of_a_recording_still_slows_the_beat_down(handler):
    """Once the script is spent the planner is drawing again, so relief applies as usual."""
    pin(handler)
    handler.min_beat_freq, handler.max_beat_freq = 1.0, 5.0
    script = _script([{"kind": "beat", "pattern": "Standard Beat", "freq": 2.0,
                       "duration_sec": 5.0}])
    handler.start_beat(script=script)

    handler.edge_relief()

    assert handler.planned_segments[0].freq == pytest.approx(1.0)


def test_an_edge_moves_the_finale_marker_along_with_everything_else(handler):
    """The pause displaces the whole plan, so "be fast here" has to travel with it - or the
    marker now points into a segment that has moved out from under it."""
    pin(handler, beat_dur=10.0)
    handler.edge_pause_dur = 8
    handler.start_beat()
    handler.set_finale_at(handler.session_start_time + 40.0)

    handler.edge_relief()

    # Compared as an offset: pytest.approx is *relative*, and against a Unix timestamp
    # its default tolerance is well over an hour - an 8 second error would sail through.
    assert handler._finale_at - handler.session_start_time == pytest.approx(48.0)


def test_an_edge_in_a_replay_keeps_the_recorded_run_in_too(handler):
    """_revalidate_finale() rebuilds the queue by *drawing* when a segment no longer matches
    the finale window - which a replay can never survive, because the script cursor does not
    rewind. A recorded queue is already correct and is left alone."""
    pin(handler)
    script = _script([
        {"kind": "beat", "pattern": "Standard Beat", "freq": 2.0, "duration_sec": 5.0},
        {"kind": "beat", "pattern": "Quick Swing", "freq": 3.0, "duration_sec": 6.0},
        {"kind": "finale", "pattern": "Double Tap", "freq": 5.0, "duration_sec": 9.0},
    ])
    handler.start_beat(script=script)
    handler.set_finale_at(handler.session_start_time + 14.0)

    handler.edge_relief()

    assert [(s.kind, s.pattern_name) for s in handler.planned_segments][:2] == [
        ("beat", "Quick Swing"),
        ("finale", "Double Tap"),
    ]
