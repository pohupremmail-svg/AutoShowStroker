import random
from unittest.mock import MagicMock

import pytest
from PyQt6.QtCore import QSettings

from src.ClimaxHandler import ClimaxHandler


@pytest.fixture
def beat_handler():
    mock = MagicMock()
    mock.is_ramp_complete.return_value = False
    return mock


@pytest.fixture
def callout_handler():
    return MagicMock()


@pytest.fixture
def handler(qtbot, beat_handler, callout_handler):
    return ClimaxHandler(beat_handler, callout_handler)


def test_settings_override_defaults(qtbot, beat_handler, callout_handler, tmp_path):
    ini = tmp_path / "settings.ini"
    settings = QSettings(str(ini), QSettings.Format.IniFormat)
    settings.setValue("ClimaxHandler/climax_active", False)
    settings.setValue("ClimaxHandler/climax_chance", 0.5)
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
    assert handler.climax_chance == 0.5
    assert handler.ruined_orgasm_active is True
    assert handler.ruined_orgasm_chance == 0.35
    assert handler.denied_orgasm_active is True
    assert handler.denied_orgasm_chance == 0.45
    assert handler.fake_climax_active is False
    assert handler.fake_climax_chance == 0.25
    assert handler.min_fake_climax_delay == 1.5
    assert handler.max_fake_climax_delay == 9.5


def test_on_beat_change_noop_when_climax_inactive_and_ramp_incomplete(handler, beat_handler, callout_handler):
    handler.climax_active = False
    handler.fake_climax_active = False
    beat_handler.is_ramp_complete.return_value = False

    handler.on_beat_change(2.0, "Standard Beat")

    callout_handler.force_output_sentence.assert_not_called()


def test_on_beat_change_triggers_real_climax_when_ramp_complete_and_chance_hits(
    handler, beat_handler, callout_handler, monkeypatch
):
    handler.fake_climax_active = False
    handler.climax_active = True
    handler.climax_chance = 1.0
    beat_handler.is_ramp_complete.return_value = True
    monkeypatch.setattr(random, "uniform", lambda _a, _b: 0.0)

    handler.on_beat_change(2.0, "Standard Beat")

    callout_handler.force_output_sentence.assert_called_once_with("climax_real")
    assert handler.climax_triggered is True


def test_on_beat_change_does_not_trigger_when_ramp_incomplete(handler, beat_handler, callout_handler, monkeypatch):
    handler.fake_climax_active = False
    handler.climax_active = True
    handler.climax_chance = 1.0
    beat_handler.is_ramp_complete.return_value = False
    monkeypatch.setattr(random, "uniform", lambda _a, _b: 0.0)

    handler.on_beat_change(2.0, "Standard Beat")

    callout_handler.force_output_sentence.assert_not_called()


def test_climax_only_triggers_once_per_session(handler, beat_handler, callout_handler, monkeypatch):
    handler.fake_climax_active = False
    handler.climax_active = True
    handler.climax_chance = 1.0
    beat_handler.is_ramp_complete.return_value = True
    monkeypatch.setattr(random, "uniform", lambda _a, _b: 0.0)

    handler.on_beat_change(2.0, "Standard Beat")
    handler.on_beat_change(2.0, "Standard Beat")

    assert callout_handler.force_output_sentence.call_count == 1


def test_resolve_outcome_returns_real_without_random_call_when_no_extras_active(handler, monkeypatch):
    handler.ruined_orgasm_active = False
    handler.denied_orgasm_active = False
    calls = []
    monkeypatch.setattr(random, "choices", lambda *a, **kw: calls.append((a, kw)) or ["real"])

    outcome = handler._resolve_outcome()

    assert outcome == "real"
    assert calls == []


def test_resolve_outcome_chances_always_sum_to_one(handler, monkeypatch):
    handler.ruined_orgasm_active = True
    handler.ruined_orgasm_chance = 0.3
    handler.denied_orgasm_active = True
    handler.denied_orgasm_chance = 0.2

    captured = {}

    def fake_choices(population, weights, k):
        captured["population"] = population
        captured["weights"] = weights
        captured["k"] = k
        return ["denied"]

    monkeypatch.setattr(random, "choices", fake_choices)

    outcome = handler._resolve_outcome()

    assert outcome == "denied"
    assert captured["population"] == ["real", "ruined", "denied"]
    assert captured["weights"] == pytest.approx([0.5, 0.3, 0.2])
    assert sum(captured["weights"]) == pytest.approx(1.0)
    assert captured["k"] == 1


def test_resolve_outcome_normalizes_when_ruined_and_denied_exceed_one(handler, monkeypatch):
    handler.ruined_orgasm_active = True
    handler.ruined_orgasm_chance = 1.0
    handler.denied_orgasm_active = True
    handler.denied_orgasm_chance = 1.0

    captured = {}

    def fake_choices(population, weights, k):
        captured["weights"] = weights
        return ["ruined"]

    monkeypatch.setattr(random, "choices", fake_choices)

    handler._resolve_outcome()

    assert captured["weights"] == pytest.approx([0.0, 0.5, 0.5])
    assert sum(captured["weights"]) == pytest.approx(1.0)


def test_resolve_outcome_ruined_only(handler, monkeypatch):
    handler.ruined_orgasm_active = True
    handler.ruined_orgasm_chance = 0.4
    handler.denied_orgasm_active = False

    captured = {}

    def fake_choices(population, weights, k):
        captured["population"] = population
        captured["weights"] = weights
        return ["ruined"]

    monkeypatch.setattr(random, "choices", fake_choices)

    assert handler._resolve_outcome() == "ruined"
    assert captured["population"] == ["real", "ruined", "denied"]
    assert captured["weights"] == pytest.approx([0.6, 0.4, 0.0])


def test_resolve_outcome_denied_chance_one_and_ruined_inactive_is_guaranteed(handler):
    handler.ruined_orgasm_active = False
    handler.denied_orgasm_active = True
    handler.denied_orgasm_chance = 1.0

    assert handler._resolve_outcome() == "denied"


def test_trigger_real_climax_emits_outcome_event(handler, beat_handler, callout_handler, qtbot, monkeypatch):
    handler.fake_climax_active = False
    handler.climax_active = True
    handler.climax_chance = 1.0
    beat_handler.is_ramp_complete.return_value = True
    monkeypatch.setattr(random, "uniform", lambda _a, _b: 0.0)

    with qtbot.waitSignal(handler.outcome_decided_event, timeout=1000) as blocker:
        handler.on_beat_change(2.0, "Standard Beat")

    assert blocker.args == ["real"]


def test_trigger_real_climax_emits_cum_status_for_real_outcome(
    handler, beat_handler, callout_handler, qtbot, monkeypatch
):
    handler.fake_climax_active = False
    handler.climax_active = True
    handler.climax_chance = 1.0
    beat_handler.is_ramp_complete.return_value = True
    monkeypatch.setattr(random, "uniform", lambda _a, _b: 0.0)

    with qtbot.waitSignal(handler.status_changed_event, timeout=1000) as blocker:
        handler.on_beat_change(2.0, "Standard Beat")

    assert blocker.args == ["cum"]


def test_trigger_real_climax_emits_ruined_status(handler, beat_handler, callout_handler, qtbot, monkeypatch):
    handler.fake_climax_active = False
    handler.climax_active = True
    handler.climax_chance = 1.0
    handler.ruined_orgasm_active = True
    handler.ruined_orgasm_chance = 1.0
    beat_handler.is_ramp_complete.return_value = True
    monkeypatch.setattr(random, "uniform", lambda _a, _b: 0.0)

    with qtbot.waitSignal(handler.status_changed_event, timeout=1000) as blocker:
        handler.on_beat_change(2.0, "Standard Beat")

    assert blocker.args == ["ruined"]


def test_trigger_real_climax_emits_denied_status(handler, beat_handler, callout_handler, qtbot, monkeypatch):
    handler.fake_climax_active = False
    handler.climax_active = True
    handler.climax_chance = 1.0
    handler.denied_orgasm_active = True
    handler.denied_orgasm_chance = 1.0
    beat_handler.is_ramp_complete.return_value = True
    monkeypatch.setattr(random, "uniform", lambda _a, _b: 0.0)

    with qtbot.waitSignal(handler.status_changed_event, timeout=1000) as blocker:
        handler.on_beat_change(2.0, "Standard Beat")

    assert blocker.args == ["denied"]


def test_on_beat_change_triggers_fake_climax_regardless_of_ramp_completion(
    handler, beat_handler, callout_handler, monkeypatch
):
    handler.fake_climax_active = True
    handler.fake_climax_chance = 1.0
    handler.climax_active = False
    beat_handler.is_ramp_complete.return_value = False
    monkeypatch.setattr(random, "uniform", lambda _a, _b: 0.0)

    handler.on_beat_change(2.0, "Standard Beat")

    callout_handler.force_output_sentence.assert_called_once_with("climax_real")
    assert handler._fake_climax_pending is True


def test_fake_climax_prompt_emits_cum_status(handler, beat_handler, callout_handler, qtbot, monkeypatch):
    handler.fake_climax_active = True
    handler.fake_climax_chance = 1.0
    handler.climax_active = False
    beat_handler.is_ramp_complete.return_value = False
    monkeypatch.setattr(random, "uniform", lambda _a, _b: 0.0)

    with qtbot.waitSignal(handler.status_changed_event, timeout=1000) as blocker:
        handler.on_beat_change(2.0, "Standard Beat")

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


def test_fake_climax_pending_blocks_further_rolls(handler, beat_handler, callout_handler, monkeypatch):
    handler.fake_climax_active = True
    handler.fake_climax_chance = 1.0
    handler.climax_active = True
    handler.climax_chance = 1.0
    beat_handler.is_ramp_complete.return_value = True
    monkeypatch.setattr(random, "uniform", lambda _a, _b: 0.0)

    handler.on_beat_change(2.0, "Standard Beat")  # triggers fake climax, sets pending
    callout_handler.force_output_sentence.reset_mock()

    handler.on_beat_change(2.0, "Standard Beat")  # blocked - still pending

    callout_handler.force_output_sentence.assert_not_called()


def test_fake_climax_reveal_fires_via_real_timer(handler, beat_handler, callout_handler, qtbot, monkeypatch):
    handler.fake_climax_active = True
    handler.fake_climax_chance = 1.0
    handler.min_fake_climax_delay = 0.05
    handler.max_fake_climax_delay = 0.05
    beat_handler.is_ramp_complete.return_value = False
    monkeypatch.setattr(random, "uniform", lambda a, _b: a)

    handler.on_beat_change(2.0, "Standard Beat")
    callout_handler.force_output_sentence.assert_called_once_with("climax_real")

    qtbot.wait(300)

    assert callout_handler.force_output_sentence.call_args_list[-1].args == ("fake_climax_reveal",)
    assert handler._fake_climax_pending is False


def test_session_started_resets_state(handler):
    handler.climax_triggered = True
    handler._fake_climax_pending = True

    handler.session_started()

    assert handler.climax_triggered is False
    assert handler._fake_climax_pending is False
