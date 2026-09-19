import random
import time
from unittest.mock import MagicMock

import pytest
from PyQt6.QtCore import QSettings

from src.BeatHandler import Segment
from src.ClimaxHandler import ClimaxHandler


@pytest.fixture
def beat_handler():
    mock = MagicMock()
    # No ramp to wait for unless a test says otherwise - a bare MagicMock here would
    # compare as a stray object inside the climax_only_after_ramp clamp.
    mock.ramp_complete_at = None
    return mock


@pytest.fixture
def callout_handler():
    return MagicMock()


@pytest.fixture
def handler(qtbot, beat_handler, callout_handler):
    return ClimaxHandler(beat_handler, callout_handler)


def beats(*indices, kind="beat"):
    return [Segment(kind, 20.0, 2.0, "Standard Beat", i) for i in indices]


def test_defaults_dict_matches_init_defaults(handler):
    for var_name, default_value in ClimaxHandler.DEFAULTS.items():
        assert getattr(handler, var_name) == default_value


def test_settings_override_defaults(qtbot, beat_handler, callout_handler, tmp_path):
    ini = tmp_path / "settings.ini"
    settings = QSettings(str(ini), QSettings.Format.IniFormat)
    settings.setValue("ClimaxHandler/climax_active", False)
    settings.setValue("ClimaxHandler/min_climax_after", 60.0)
    settings.setValue("ClimaxHandler/max_climax_after", 90.0)
    settings.setValue("ClimaxHandler/ruined_orgasm_active", True)
    settings.setValue("ClimaxHandler/ruined_orgasm_chance", 0.35)
    settings.setValue("ClimaxHandler/denied_orgasm_active", True)
    settings.setValue("ClimaxHandler/denied_orgasm_chance", 0.45)
    settings.setValue("ClimaxHandler/fake_climax_active", False)
    settings.setValue("ClimaxHandler/fake_climax_chance", 0.25)
    settings.setValue("ClimaxHandler/min_fake_climax_delay", 1.5)
    settings.setValue("ClimaxHandler/max_fake_climax_delay", 9.5)

    handler = ClimaxHandler(beat_handler, callout_handler, settings=settings)

    assert handler.climax_active is False
    assert handler.min_climax_after == 60.0
    assert handler.max_climax_after == 90.0
    assert handler.ruined_orgasm_active is True
    assert handler.ruined_orgasm_chance == 0.35
    assert handler.denied_orgasm_active is True
    assert handler.denied_orgasm_chance == 0.45
    assert handler.fake_climax_active is False
    assert handler.fake_climax_chance == 0.25
    assert handler.min_fake_climax_delay == 1.5
    assert handler.max_fake_climax_delay == 9.5


# --- planning the climax ---


def test_the_climax_is_placed_the_configured_time_into_the_session(handler, beat_handler):
    handler.climax_active = True
    handler.min_climax_after = handler.max_climax_after = 100.0

    handler.on_session_planned(5000.0)  # the session started at 5000

    assert handler.finale_at == 5100.0
    beat_handler.set_finale_at.assert_called_once_with(5100.0)


def test_the_time_is_drawn_from_the_configured_range(handler):
    handler.climax_active = True
    handler.min_climax_after, handler.max_climax_after = 60.0, 300.0
    handler.on_session_planned(5000.0)
    assert 5060.0 <= handler.finale_at <= 5300.0


def test_the_climax_is_independent_of_the_difficulty_ramp(handler, beat_handler):
    """It is measured from the start of the session, not from the end of the ramp - so it
    can be set to land while the ramp is still climbing, and switching ramping off does
    not silently move it."""
    beat_handler.ramp_target_duration = 9999.0
    handler.climax_active = True
    handler.min_climax_after = handler.max_climax_after = 100.0

    handler.on_session_planned(5000.0)

    assert handler.finale_at == 5100.0


def test_no_climax_is_planned_when_it_is_switched_off(handler, beat_handler):
    handler.climax_active = False
    handler.on_session_planned(5000.0)
    assert handler.finale_at is None
    beat_handler.set_finale_at.assert_called_once_with(None)


def test_the_outcome_is_decided_up_front_not_at_the_moment_it_fires(handler):
    """The planner needs to know what it is building towards - a denial and a real
    orgasm are not the same run-in."""
    handler.climax_active = True
    handler.denied_orgasm_active = True
    handler.denied_orgasm_chance = 1.0

    handler.on_session_planned(5000.0)

    assert handler.outcome == "denied"


# --- "only after the ramp finishes" ---


def test_the_climax_waits_for_the_ramp_when_asked_to(handler, beat_handler):
    beat_handler.ramp_complete_at = 5500.0
    handler.climax_active = True
    handler.climax_only_after_ramp = True
    handler.min_climax_after = handler.max_climax_after = 100.0  # would land at 5100

    handler.on_session_planned(5000.0)

    assert handler.finale_at == 5500.0
    beat_handler.set_finale_at.assert_called_once_with(5500.0)


def test_waiting_for_the_ramp_never_pushes_a_late_climax_earlier(handler, beat_handler):
    beat_handler.ramp_complete_at = 5200.0
    handler.climax_active = True
    handler.climax_only_after_ramp = True
    handler.min_climax_after = handler.max_climax_after = 600.0

    handler.on_session_planned(5000.0)

    assert handler.finale_at == 5600.0


def test_the_climax_does_not_wait_when_the_box_is_unticked(handler, beat_handler):
    beat_handler.ramp_complete_at = 5500.0
    handler.climax_active = True
    handler.climax_only_after_ramp = False
    handler.min_climax_after = handler.max_climax_after = 100.0

    handler.on_session_planned(5000.0)

    assert handler.finale_at == 5100.0


def test_the_climax_does_not_wait_when_there_is_no_ramp(handler, beat_handler):
    """Ramping switched off means ramp_complete_at is None - the box has nothing to wait
    for, and the Ramp duration sliders must not reach the climax through the back door."""
    beat_handler.ramp_complete_at = None
    handler.climax_active = True
    handler.climax_only_after_ramp = True
    handler.min_climax_after = handler.max_climax_after = 100.0

    handler.on_session_planned(5000.0)

    assert handler.finale_at == 5100.0


def test_ticking_the_box_mid_session_pushes_the_climax_past_the_ramp(handler, beat_handler):
    beat_handler.ramp_complete_at = 5500.0
    beat_handler.session_start_time = 5000.0
    handler.climax_active = True
    handler.climax_only_after_ramp = False
    handler.min_climax_after = handler.max_climax_after = 100.0
    handler.on_session_planned(5000.0)
    assert handler.finale_at == 5100.0

    handler.climax_only_after_ramp = True
    handler.settings_changed()

    assert handler.finale_at == 5500.0
    assert beat_handler.set_finale_at.call_args.args == (5500.0,)


def test_unticking_the_box_mid_session_restores_the_drawn_time(handler, beat_handler):
    """The drawn moment is kept, so the box is a clamp that can be lifted again rather
    than a one-way re-roll."""
    beat_handler.ramp_complete_at = 5500.0
    beat_handler.session_start_time = 5000.0
    handler.climax_active = True
    handler.climax_only_after_ramp = True
    handler.min_climax_after = handler.max_climax_after = 100.0
    handler.on_session_planned(5000.0)
    assert handler.finale_at == 5500.0

    handler.climax_only_after_ramp = False
    handler.settings_changed()

    assert handler.finale_at == 5100.0


# --- firing the climax ---


def test_the_climax_fires_when_its_moment_arrives(handler, callout_handler):
    handler.climax_active = True
    handler.on_session_planned(time.time() + 100)

    handler._on_climax_due()

    callout_handler.force_output_sentence.assert_called_once_with("climax_real")
    assert handler.climax_triggered is True


def test_the_climax_fires_by_itself_via_the_real_timer(handler, callout_handler, qtbot):
    handler.climax_active = True
    handler.min_climax_after = handler.max_climax_after = 0.05

    handler.on_session_planned(time.time())

    qtbot.waitUntil(lambda: handler.climax_triggered, timeout=2000)
    callout_handler.force_output_sentence.assert_called_once_with("climax_real")


def test_the_climax_only_fires_once(handler, callout_handler):
    handler.climax_active = True
    handler.on_session_planned(time.time() + 100)

    handler._on_climax_due()
    handler._on_climax_due()

    assert callout_handler.force_output_sentence.call_count == 1


def test_the_climax_emits_its_outcome(handler, qtbot):
    handler.climax_active = True
    handler.on_session_planned(time.time() + 100)

    with qtbot.waitSignal(handler.outcome_decided_event, timeout=1000) as blocker:
        handler._on_climax_due()

    assert blocker.args == ["real"]


@pytest.mark.parametrize(
    "flag, chance, status",
    [
        ("ruined_orgasm", 1.0, "ruined"),
        ("denied_orgasm", 1.0, "denied"),
    ],
)
def test_the_climax_emits_the_status_for_its_outcome(handler, qtbot, flag, chance, status):
    handler.climax_active = True
    setattr(handler, f"{flag}_active", True)
    setattr(handler, f"{flag}_chance", chance)
    handler.on_session_planned(time.time() + 100)

    with qtbot.waitSignal(handler.status_changed_event, timeout=1000) as blocker:
        handler._on_climax_due()

    assert blocker.args == [status]


def test_a_real_outcome_emits_the_cum_status(handler, qtbot):
    handler.climax_active = True
    handler.on_session_planned(time.time() + 100)

    with qtbot.waitSignal(handler.status_changed_event, timeout=1000) as blocker:
        handler._on_climax_due()

    assert blocker.args == ["cum"]


def test_the_climax_holds_the_rhythm_it_was_announced_over(handler, beat_handler):
    """No new beat, no pause and no beat-change callout on top of the moment the whole
    session was built towards."""
    handler.climax_active = True
    handler.on_session_planned(time.time() + 100)

    handler._on_climax_due()

    beat_handler.hold_final_segment.assert_called_once_with()


def test_a_fake_climax_does_not_hold_the_rhythm(handler, beat_handler):
    """The session carries on after the reveal, so the plan has to carry on too."""
    handler.fake_climax_active = True
    handler.fake_climax_chance = 1.0
    handler.on_plan_extended(beats(7))

    handler.on_segment_started(beats(7)[0])

    beat_handler.hold_final_segment.assert_not_called()


# --- fake climaxes ---


def test_fakes_are_planned_onto_newly_planned_boundaries(handler):
    handler.fake_climax_active = True
    handler.fake_climax_chance = 1.0

    handler.on_plan_extended(beats(3, 4, 5))

    assert handler.planned_fake_boundaries == {3, 4, 5}


def test_no_fakes_are_planned_when_they_are_switched_off(handler):
    handler.fake_climax_active = False
    handler.fake_climax_chance = 1.0
    handler.on_plan_extended(beats(3, 4, 5))
    assert handler.planned_fake_boundaries == set()


def test_the_finale_is_never_planned_as_a_fake(handler):
    """Faking the climax at the exact moment the real one is due would be indistinguishable
    from a bug, and the reveal would land on top of the real announcement."""
    handler.fake_climax_active = True
    handler.fake_climax_chance = 1.0

    handler.on_plan_extended(beats(3) + beats(4, kind="finale") + beats(5))

    assert handler.planned_fake_boundaries == {3, 5}


def test_a_planned_fake_fires_when_its_segment_starts(handler, callout_handler):
    handler.fake_climax_active = True
    handler.fake_climax_chance = 1.0
    handler.on_plan_extended(beats(7))

    handler.on_segment_started(beats(7)[0])

    callout_handler.force_output_sentence.assert_called_once_with("climax_real")
    assert handler._fake_climax_pending is True


def test_a_segment_with_no_planned_fake_does_nothing(handler, callout_handler):
    handler.on_plan_extended(beats(7))
    handler.on_segment_started(beats(7)[0])
    callout_handler.force_output_sentence.assert_not_called()


def test_a_fake_fires_only_once_for_its_boundary(handler, callout_handler):
    handler.fake_climax_active = True
    handler.fake_climax_chance = 1.0
    handler.on_plan_extended(beats(7))

    handler.on_segment_started(beats(7)[0])
    handler._reveal_fake_climax()
    callout_handler.force_output_sentence.reset_mock()
    handler.on_segment_started(beats(7)[0])

    callout_handler.force_output_sentence.assert_not_called()


def test_a_fake_does_not_fire_once_the_real_climax_has_happened(handler, callout_handler):
    handler.climax_active = True
    handler.fake_climax_active = True
    handler.fake_climax_chance = 1.0
    handler.on_session_planned(time.time() + 100)
    handler.on_plan_extended(beats(7))
    handler._on_climax_due()
    callout_handler.force_output_sentence.reset_mock()

    handler.on_segment_started(beats(7)[0])

    callout_handler.force_output_sentence.assert_not_called()


def test_the_real_climax_cancels_a_pending_fake_reveal(handler, callout_handler, qtbot):
    """Otherwise the fake's "only joking" would land seconds after the real announcement."""
    handler.climax_active = True
    handler.fake_climax_active = True
    handler.fake_climax_chance = 1.0
    handler.min_fake_climax_delay = handler.max_fake_climax_delay = 0.05
    handler.on_session_planned(time.time() + 100)
    handler.on_plan_extended(beats(7))
    handler.on_segment_started(beats(7)[0])

    handler._on_climax_due()
    qtbot.wait(300)

    assert handler._fake_climax_pending is False
    assert callout_handler.force_output_sentence.call_args_list[-1].args == ("climax_real",)


def test_fake_climax_triggered_event_is_emitted(handler, qtbot):
    handler.fake_climax_active = True
    handler.fake_climax_chance = 1.0
    handler.on_plan_extended(beats(7))

    with qtbot.waitSignal(handler.fake_climax_triggered_event, timeout=1000):
        handler.on_segment_started(beats(7)[0])


def test_the_real_climax_does_not_emit_the_fake_event(handler):
    handler.climax_active = True
    handler.on_session_planned(time.time() + 100)
    received = []
    handler.fake_climax_triggered_event.connect(lambda: received.append(True))

    handler._on_climax_due()

    assert received == []


def test_a_fake_prompt_emits_the_cum_status(handler, qtbot):
    handler.fake_climax_active = True
    handler.fake_climax_chance = 1.0
    handler.on_plan_extended(beats(7))

    with qtbot.waitSignal(handler.status_changed_event, timeout=1000) as blocker:
        handler.on_segment_started(beats(7)[0])

    assert blocker.args == ["cum"]


def test_fake_climax_reveal_fires_and_resets_pending(handler, callout_handler):
    handler._fake_climax_pending = True

    handler._reveal_fake_climax()

    callout_handler.force_output_sentence.assert_called_once_with("fake_climax_reveal")
    assert handler._fake_climax_pending is False


def test_fake_climax_reveal_emits_neutral_status(handler, callout_handler, qtbot):
    handler._fake_climax_pending = True

    with qtbot.waitSignal(handler.status_changed_event, timeout=1000) as blocker:
        handler._reveal_fake_climax()

    assert blocker.args == ["neutral"]


def test_fake_climax_reveal_fires_via_real_timer(handler, callout_handler, qtbot):
    handler.fake_climax_active = True
    handler.fake_climax_chance = 1.0
    handler.min_fake_climax_delay = handler.max_fake_climax_delay = 0.05
    handler.on_plan_extended(beats(7))

    handler.on_segment_started(beats(7)[0])
    callout_handler.force_output_sentence.assert_called_once_with("climax_real")

    qtbot.waitUntil(lambda: not handler._fake_climax_pending, timeout=2000)

    assert callout_handler.force_output_sentence.call_args_list[-1].args == ("fake_climax_reveal",)


# --- mid-session settings changes ---


def test_a_settings_save_keeps_the_climax_where_it_was(handler):
    """Re-drawing it would make "open Settings and save" a lever for a different climax."""
    handler.climax_active = True
    handler.min_climax_after = handler.max_climax_after = 100.0
    handler.on_session_planned(5000.0)

    handler.min_climax_after = handler.max_climax_after = 5.0
    handler.settings_changed()

    assert handler.finale_at == 5100.0


def test_a_settings_save_re_resolves_the_outcome(handler):
    """Otherwise switching denial on mid-session would quietly do nothing."""
    handler.climax_active = True
    handler.on_session_planned(time.time() + 100)
    assert handler.outcome == "real"

    handler.denied_orgasm_active = True
    handler.denied_orgasm_chance = 1.0
    handler.settings_changed()

    assert handler.outcome == "denied"


def test_switching_the_climax_off_mid_session_withdraws_it(handler, beat_handler):
    handler.climax_active = True
    handler.on_session_planned(time.time() + 100)

    handler.climax_active = False
    handler.settings_changed()

    assert handler.finale_at is None
    assert beat_handler.set_finale_at.call_args.args == (None,)


def test_switching_the_climax_on_mid_session_plans_one(handler, beat_handler):
    beat_handler.session_start_time = 5000.0
    handler.climax_active = False
    handler.on_session_planned(5000.0)

    handler.climax_active = True
    handler.min_climax_after = handler.max_climax_after = 100.0
    handler.settings_changed()

    assert handler.finale_at == 5100.0


def test_a_settings_save_after_the_climax_changes_nothing(handler):
    handler.climax_active = True
    handler.on_session_planned(time.time() + 100)
    handler._on_climax_due()
    outcome = handler.outcome

    handler.denied_orgasm_active = True
    handler.denied_orgasm_chance = 1.0
    handler.settings_changed()

    assert handler.outcome == outcome


# --- outcome resolution (unchanged behaviour) ---


def test_resolve_outcome_returns_real_without_random_call_when_no_extras_active(handler, monkeypatch):
    calls = []
    monkeypatch.setattr(random, "choices", lambda *a, **kw: calls.append((a, kw)) or ["real"])
    handler.ruined_orgasm_active = False
    handler.denied_orgasm_active = False

    assert handler._resolve_outcome() == "real"
    assert calls == []


def test_resolve_outcome_chances_always_sum_to_one(handler, monkeypatch):
    captured = {}

    def fake_choices(population, weights, k):
        captured["weights"] = weights
        return ["real"]

    monkeypatch.setattr(random, "choices", fake_choices)
    handler.ruined_orgasm_active = True
    handler.ruined_orgasm_chance = 0.3
    handler.denied_orgasm_active = True
    handler.denied_orgasm_chance = 0.2

    handler._resolve_outcome()

    assert captured["weights"] == pytest.approx([0.5, 0.3, 0.2])


def test_resolve_outcome_normalizes_when_ruined_and_denied_exceed_one(handler, monkeypatch):
    captured = {}

    def fake_choices(population, weights, k):
        captured["weights"] = weights
        return ["ruined"]

    monkeypatch.setattr(random, "choices", fake_choices)
    handler.ruined_orgasm_active = True
    handler.ruined_orgasm_chance = 0.8
    handler.denied_orgasm_active = True
    handler.denied_orgasm_chance = 0.8

    handler._resolve_outcome()

    assert sum(captured["weights"]) == pytest.approx(1.0)
    assert captured["weights"][0] == pytest.approx(0.0)


def test_resolve_outcome_ruined_only(handler, monkeypatch):
    captured = {}

    def fake_choices(population, weights, k):
        captured["weights"] = weights
        return ["ruined"]

    monkeypatch.setattr(random, "choices", fake_choices)
    handler.ruined_orgasm_active = True
    handler.ruined_orgasm_chance = 1.0
    handler.denied_orgasm_active = False

    assert handler._resolve_outcome() == "ruined"
    assert captured["weights"] == pytest.approx([0.0, 1.0, 0.0])


def test_session_started_resets_state(handler):
    handler.climax_triggered = True
    handler._fake_climax_pending = True
    handler.finale_at = 123.0

    handler.session_started()

    assert handler.climax_triggered is False
    assert handler._fake_climax_pending is False
    assert handler.finale_at is None
    assert handler.planned_fake_boundaries == set()


# --- replaying a saved session ---


def script(climax_at=180.0, outcome="ruined", fakes=(40.0, 120.0), segments=6,
           duration=200.0):
    from src.SessionScript import SessionScript

    recorded = [
        {"kind": "beat", "pattern": "Standard Beat", "freq": 2.0, "duration_sec": 20.0}
        for _ in range(segments)
    ]
    return SessionScript({
        "duration_sec": duration, "segments": recorded, "custom_patterns": {}, "media": [],
        "climax": None if climax_at is None else {"at_sec": climax_at, "outcome": outcome},
        "fake_climaxes": list(fakes),
    })


def test_a_replay_takes_the_recorded_climax_time_and_outcome(handler, beat_handler):
    handler.climax_active = True
    handler.min_climax_after = handler.max_climax_after = 999.0  # would land nowhere near

    handler.on_session_planned(5000.0, script=script(climax_at=180.0, outcome="denied"))

    assert handler.finale_at == 5180.0
    assert handler.outcome == "denied"
    beat_handler.set_finale_at.assert_called_once_with(5180.0)


def test_a_replay_is_not_clamped_to_the_ramp(handler, beat_handler):
    """The time was recorded, not negotiated - holding it back would replay a different
    session than the one that was saved."""
    beat_handler.ramp_complete_at = 5900.0
    handler.climax_active = True
    handler.climax_only_after_ramp = True

    handler.on_session_planned(5000.0, script=script(climax_at=100.0))

    assert handler.finale_at == 5100.0


def test_a_replay_of_a_session_that_never_climaxed_draws_one(handler, beat_handler):
    """A recording that stops before the climax is almost always one the user did not last
    through - replaying it to try again has to be a session that can actually be finished,
    so from the end of the recording on it is an ordinary session."""
    handler.climax_active = True
    handler.min_climax_after = handler.max_climax_after = 500.0

    handler.on_session_planned(5000.0, script=script(climax_at=None))

    assert handler.finale_at == 5500.0
    assert handler.outcome in ("real", "ruined", "denied")
    beat_handler.set_finale_at.assert_called_once_with(5500.0)


def test_the_drawn_climax_never_lands_inside_the_recording(handler):
    """Otherwise the retry would end sooner than the session it is retrying - backwards,
    and the run-in cannot be built into segments that are read from the file anyway."""
    handler.climax_active = True
    handler.min_climax_after = handler.max_climax_after = 30.0

    handler.on_session_planned(5000.0, script=script(climax_at=None, duration=200.0))

    assert handler.finale_at >= 5200.0


def test_a_replay_of_a_session_without_a_climax_still_obeys_the_climax_switch(
    handler, beat_handler
):
    handler.climax_active = False

    handler.on_session_planned(5000.0, script=script(climax_at=None))

    assert handler.finale_at is None
    beat_handler.set_finale_at.assert_called_once_with(None)


def test_a_replay_pins_the_recorded_fake_outs(handler):
    handler.fake_climax_active = True
    handler.fake_climax_chance = 0.0  # would never roll one live

    handler.on_session_planned(5000.0, script=script(fakes=(40.0, 120.0)))

    assert handler.scripted_fake_count == 2


def test_a_replay_does_not_roll_fake_outs_of_its_own(handler):
    handler.fake_climax_active = True
    handler.fake_climax_chance = 1.0  # would roll one on every boundary live

    handler.on_session_planned(5000.0, script=script(fakes=(), segments=6))
    handler.on_plan_extended(beats(3, 4, 5))  # all still inside the recording

    assert handler.planned_fake_boundaries == set()


def test_fake_outs_start_again_past_the_end_of_the_recording(handler):
    """The stretch after the recording is an ordinary session, and an ordinary session has
    fake-outs in it."""
    handler.fake_climax_active = True
    handler.fake_climax_chance = 1.0

    handler.on_session_planned(5000.0, script=script(climax_at=None, fakes=(), segments=4))
    handler.on_plan_extended(beats(2, 3, 4, 5))

    assert handler.planned_fake_boundaries == {4, 5}


def test_a_scripted_fake_out_fires_when_its_moment_arrives(handler, callout_handler, qtbot):
    handler.fake_climax_active = True
    handler.min_fake_climax_delay = handler.max_fake_climax_delay = 0.05

    handler.on_session_planned(time.time(), script=script(climax_at=None, fakes=(0.05,)))

    qtbot.waitUntil(lambda: handler._fake_climax_pending or callout_handler.force_output_sentence.called,
                    timeout=2000)
    callout_handler.force_output_sentence.assert_called_with("climax_real")


def test_a_new_live_session_forgets_the_script(handler, beat_handler):
    handler.climax_active = True
    handler.min_climax_after = handler.max_climax_after = 100.0
    handler.on_session_planned(5000.0, script=script(climax_at=180.0))

    handler.session_started()
    handler.on_session_planned(6000.0)

    assert handler.finale_at == 6100.0


# --- a denial ends the rhythm on the spot ---


def test_a_denied_climax_stops_the_beat_immediately(handler, beat_handler):
    """There is nothing left to stroke to, and that is the point: carrying on after a denial
    has to be the user's own doing, not the app still driving them."""
    handler.climax_active = True
    handler.denied_orgasm_active = True
    handler.denied_orgasm_chance = 1.0
    handler.ruined_orgasm_active = False

    handler.on_session_planned(time.time() - 1)
    handler._on_climax_due()

    assert handler.outcome == "denied"
    beat_handler.stop.assert_called_once()
    beat_handler.hold_final_segment.assert_not_called()


def test_a_real_climax_still_carries_the_beat_through_it(handler, beat_handler):
    handler.climax_active = True
    handler.ruined_orgasm_active = False
    handler.denied_orgasm_active = False

    handler.on_session_planned(time.time() - 1)
    handler._on_climax_due()

    assert handler.outcome == "real"
    beat_handler.hold_final_segment.assert_called_once()
    beat_handler.stop.assert_not_called()


# --- an edge pushes everything back ---


def test_postponing_moves_the_climax_and_tells_the_planner(handler, beat_handler):
    """Without this an edge pause would silently cost the user the rhythm leading up to the
    climax rather than buying them time - the climax sits on an absolute clock."""
    handler.climax_active = True
    handler.min_climax_after = handler.max_climax_after = 100.0
    handler.on_session_planned(5000.0)
    beat_handler.set_finale_at.reset_mock()

    handler.postpone(20.0)

    assert handler.finale_at == 5120.0
    beat_handler.set_finale_at.assert_called_once_with(5120.0)


def test_postponing_survives_a_later_settings_change(handler, beat_handler):
    """The pre-clamp moment has to move too, or saving Settings snaps the climax back to
    where it was before the edge."""
    handler.climax_active = True
    handler.min_climax_after = handler.max_climax_after = 100.0
    handler.on_session_planned(5000.0)

    handler.postpone(20.0)
    handler.settings_changed()

    assert handler.finale_at == 5120.0


def test_postponing_shifts_a_replays_recorded_fake_outs_too(handler):
    """Everything after the edge plays as recorded, just later - the fakes included."""
    handler.fake_climax_active = True
    handler.on_session_planned(time.time(), script=script(climax_at=200.0, fakes=(60.0,)))
    before = handler._scripted_fake_timers[0].remainingTime()

    handler.postpone(20.0)

    assert handler._scripted_fake_timers[0].remainingTime() >= before + 19000


def test_postponing_does_nothing_once_the_climax_has_already_landed(handler, beat_handler):
    handler.climax_active = True
    handler.min_climax_after = handler.max_climax_after = 100.0
    handler.on_session_planned(5000.0)
    handler.climax_triggered = True

    handler.postpone(20.0)

    assert handler.finale_at == 5100.0


def test_postponing_a_session_without_a_climax_is_harmless(handler, beat_handler):
    handler.climax_active = False
    handler.on_session_planned(5000.0)

    handler.postpone(20.0)

    assert handler.finale_at is None
