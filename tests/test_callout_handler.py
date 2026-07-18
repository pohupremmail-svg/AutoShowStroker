import json
import random

import pytest

from src import CalloutHandler as callout_module
from src.CalloutHandler import CalloutHandler

TRIGGER_KEYS = [
    "beat_change_general",
    "beat_change_faster",
    "beat_change_slower",
    "pause_start",
    "pause_end",
    "media_skipped",
    "media_repeated",
    "session_started",
]


@pytest.fixture
def callout_dir(tmp_path):
    en = {key: [f"en {key} phrase"] for key in TRIGGER_KEYS}
    de = {key: [f"de {key} phrase"] for key in TRIGGER_KEYS}
    (tmp_path / "en.json").write_text(json.dumps(en), encoding="utf-8")
    (tmp_path / "de.json").write_text(json.dumps(de), encoding="utf-8")
    return tmp_path


@pytest.fixture
def handler(qapp, callout_dir, monkeypatch):
    monkeypatch.setattr(callout_module, "get_resource_path", lambda _relative_path: str(callout_dir))
    return CalloutHandler()


def test_loads_available_languages(handler):
    assert set(handler.available_languages) == {"en", "de"}


def test_defaults_to_english(handler):
    assert handler.lang == "en"


def test_set_lang_switches_language(handler):
    handler.set_lang("de")
    assert handler.lang == "de"


def test_set_lang_ignores_unknown_language(handler):
    handler.set_lang("fr")
    assert handler.lang == "en"


def test_select_and_output_sentence_emits_when_active(handler, qtbot):
    handler.active_callout = True
    handler.talking_chance = 1.0

    with qtbot.waitSignal(handler.new_tease_event, timeout=1000) as blocker:
        handler.select_and_output_sentence("session_started")

    assert blocker.args == ["en session_started phrase"]
    assert handler.is_teasing is True


def test_select_and_output_sentence_noop_when_inactive(handler, qtbot):
    handler.active_callout = False
    with qtbot.assertNotEmitted(handler.new_tease_event, wait=200):
        handler.select_and_output_sentence("session_started")


def test_select_and_output_sentence_noop_while_already_teasing(handler, qtbot):
    handler.active_callout = True
    handler.talking_chance = 1.0
    handler.is_teasing = True
    with qtbot.assertNotEmitted(handler.new_tease_event, wait=200):
        handler.select_and_output_sentence("session_started")


def test_select_and_output_sentence_respects_talking_chance(handler, qtbot):
    handler.active_callout = True
    handler.talking_chance = 0.0
    with qtbot.assertNotEmitted(handler.new_tease_event, wait=200):
        handler.select_and_output_sentence("session_started")


def test_missing_category_does_not_raise(handler):
    handler.active_callout = True
    handler.talking_chance = 1.0
    handler.callout_data["en"]["session_started"] = []
    handler.select_and_output_sentence("session_started")


def test_tease_timer_hides_after_timeout(handler, qtbot):
    handler.active_callout = True
    handler.talking_chance = 1.0
    handler.tease_time = 50

    with qtbot.waitSignal(handler.hide_tease_event, timeout=1000):
        handler.select_and_output_sentence("session_started")

    assert handler.is_teasing is False


def test_beat_change_general_picks_faster_when_freq_increases(handler, monkeypatch, qtbot):
    handler.active_callout = True
    handler.talking_chance = 1.0
    handler.cur_freq = 1.0
    monkeypatch.setattr(random, "uniform", lambda _a, _b: 0.9)

    with qtbot.waitSignal(handler.new_tease_event, timeout=1000) as blocker:
        handler.beat_change_general(2.0, "Standard Beat")

    assert blocker.args == ["en beat_change_faster phrase"]


def test_beat_change_general_picks_slower_when_freq_decreases(handler, monkeypatch, qtbot):
    handler.active_callout = True
    handler.talking_chance = 1.0
    handler.cur_freq = 2.0
    monkeypatch.setattr(random, "uniform", lambda _a, _b: 0.9)

    with qtbot.waitSignal(handler.new_tease_event, timeout=1000) as blocker:
        handler.beat_change_general(1.0, "Standard Beat")

    assert blocker.args == ["en beat_change_slower phrase"]


# --- directory-loading edge cases ---


def test_missing_callout_dir_raises(qapp, tmp_path, monkeypatch):
    monkeypatch.setattr(callout_module, "get_resource_path", lambda _relative_path: str(tmp_path / "does-not-exist"))
    with pytest.raises(AssertionError):
        CalloutHandler()


def test_empty_callout_dir_does_not_crash_and_disables_callouts(qapp, tmp_path, monkeypatch):
    monkeypatch.setattr(callout_module, "get_resource_path", lambda _relative_path: str(tmp_path))

    handler = CalloutHandler()

    assert handler.available_languages == []
    assert handler.callout_data == {}
    # active_callout defaults False anyway, but even if turned on, there is no
    # data to select from - select_and_output_sentence must not raise.
    handler.active_callout = True
    handler.talking_chance = 1.0
    handler.select_and_output_sentence("session_started")


def test_invalid_json_file_is_skipped_but_language_stays_listed(qapp, tmp_path, monkeypatch):
    (tmp_path / "en.json").write_text("{not valid json", encoding="utf-8")
    monkeypatch.setattr(callout_module, "get_resource_path", lambda _relative_path: str(tmp_path))

    handler = CalloutHandler()

    assert handler.available_languages == ["en"]
    assert handler.callout_data == {}
    # lang falls back to "en" but there's no data loaded for it - selecting
    # a sentence must degrade gracefully rather than raise.
    handler.active_callout = True
    handler.talking_chance = 1.0
    handler.select_and_output_sentence("session_started")
