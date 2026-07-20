import json
import random

import pytest
from PyQt6.QtCore import QSettings

from src import CalloutHandler as callout_module
from src.CalloutHandler import TRIGGER_KEYS, CalloutHandler


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


def test_defaults_dict_matches_init_defaults(handler):
    for var_name, default_value in CalloutHandler.DEFAULTS.items():
        assert getattr(handler, var_name) == default_value


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


def test_force_output_sentence_ignores_active_callout_flag(handler, qtbot):
    handler.active_callout = False
    with qtbot.waitSignal(handler.new_tease_event, timeout=1000) as blocker:
        handler.force_output_sentence("session_started")
    assert blocker.args == ["en session_started phrase"]


def test_force_output_sentence_ignores_talking_chance(handler, qtbot):
    handler.active_callout = True
    handler.talking_chance = 0.0
    with qtbot.waitSignal(handler.new_tease_event, timeout=1000):
        handler.force_output_sentence("session_started")


def test_force_output_sentence_overrides_in_progress_tease(handler, qtbot):
    handler.active_callout = True
    handler.is_teasing = True
    with qtbot.waitSignal(handler.new_tease_event, timeout=1000):
        handler.force_output_sentence("session_started")
    assert handler.is_teasing is True


def test_force_output_sentence_missing_category_does_not_raise(handler, qtbot):
    with qtbot.assertNotEmitted(handler.new_tease_event, wait=200):
        handler.force_output_sentence("does_not_exist")


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


# --- custom phrase files ---


@pytest.fixture
def handler_with_settings(qapp, callout_dir, monkeypatch, tmp_path):
    monkeypatch.setattr(callout_module, "get_resource_path", lambda _relative_path: str(callout_dir))
    settings = QSettings(str(tmp_path / "settings.ini"), QSettings.Format.IniFormat)
    return CalloutHandler(settings=settings)


def _write_custom_file(tmp_path, name, data):
    path = tmp_path / name
    path.write_text(json.dumps(data), encoding="utf-8")
    return str(path)


def test_load_custom_file_merges_phrases_after_builtins(handler, tmp_path):
    custom_path = _write_custom_file(tmp_path, "custom_en.json", {"session_started": ["custom phrase"]})

    handler.load_custom_file(custom_path, "en")

    assert handler.callout_data["en"]["session_started"] == ["en session_started phrase", "custom phrase"]


def test_load_custom_file_raises_for_unknown_language(handler, tmp_path):
    custom_path = _write_custom_file(tmp_path, "custom.json", {"session_started": ["x"]})
    with pytest.raises(ValueError):
        handler.load_custom_file(custom_path, "fr")


def test_load_custom_file_raises_if_already_loaded(handler, tmp_path):
    custom_path = _write_custom_file(tmp_path, "custom.json", {"session_started": ["x"]})
    handler.load_custom_file(custom_path, "en")
    with pytest.raises(ValueError):
        handler.load_custom_file(custom_path, "en")


def test_load_custom_file_raises_for_missing_file(handler, tmp_path):
    with pytest.raises(ValueError):
        handler.load_custom_file(str(tmp_path / "does_not_exist.json"), "en")


def test_load_custom_file_raises_for_invalid_json(handler, tmp_path):
    path = tmp_path / "bad.json"
    path.write_text("{not valid json", encoding="utf-8")
    with pytest.raises(ValueError):
        handler.load_custom_file(str(path), "en")


def test_load_custom_file_raises_for_non_object_json(handler, tmp_path):
    path = tmp_path / "list.json"
    path.write_text("[1, 2, 3]", encoding="utf-8")
    with pytest.raises(ValueError):
        handler.load_custom_file(str(path), "en")


def test_load_custom_file_ignores_unknown_trigger_keys_and_bad_values(handler, tmp_path):
    custom_path = _write_custom_file(tmp_path, "custom.json", {
        "session_started": ["good phrase"],
        "not_a_real_key": ["ignored"],
        "pause_start": "not a list",
        "pause_end": [1, 2, 3],
    })

    handler.load_custom_file(custom_path, "en")

    assert handler.callout_data["en"]["session_started"] == ["en session_started phrase", "good phrase"]
    assert "not_a_real_key" not in handler.callout_data["en"]
    assert handler.callout_data["en"]["pause_start"] == ["en pause_start phrase"]
    assert handler.callout_data["en"]["pause_end"] == ["en pause_end phrase"]


def test_unload_custom_file_removes_entry_and_stops_contributing(handler, tmp_path):
    custom_en_1 = _write_custom_file(tmp_path, "custom_en_1.json", {"session_started": ["phrase one"]})
    custom_en_2 = _write_custom_file(tmp_path, "custom_en_2.json", {"session_started": ["phrase two"]})
    handler.load_custom_file(custom_en_1, "en")
    handler.load_custom_file(custom_en_2, "en")

    handler.unload_custom_file(custom_en_1)

    assert handler.callout_data["en"]["session_started"] == ["en session_started phrase", "phrase two"]
    assert [e["path"] for e in handler.custom_phrase_files] == [custom_en_2]


def test_unload_nonexistent_file_is_a_noop(handler):
    handler.unload_custom_file("/does/not/exist.json")


def test_settings_less_handler_supports_load_and_unload(handler, tmp_path):
    custom_path = _write_custom_file(tmp_path, "custom.json", {"session_started": ["x"]})
    handler.load_custom_file(custom_path, "en")  # would raise AttributeError pre-fix
    handler.unload_custom_file(custom_path)


def test_merged_custom_phrase_is_selectable(handler, tmp_path, monkeypatch, qtbot):
    custom_path = _write_custom_file(tmp_path, "custom.json", {"session_started": ["custom phrase!"]})
    handler.load_custom_file(custom_path, "en")
    handler.active_callout = True
    handler.talking_chance = 1.0
    monkeypatch.setattr(random, "choice", lambda seq: seq[-1])

    with qtbot.waitSignal(handler.new_tease_event, timeout=1000) as blocker:
        handler.select_and_output_sentence("session_started")

    assert blocker.args == ["custom phrase!"]


def test_load_custom_file_persists_and_reloads_across_instances(handler_with_settings, tmp_path):
    custom_path = _write_custom_file(tmp_path, "custom.json", {"session_started": ["persisted phrase"]})
    handler_with_settings.load_custom_file(custom_path, "en")

    second = CalloutHandler(settings=handler_with_settings.settings)

    assert second.callout_data["en"]["session_started"] == ["en session_started phrase", "persisted phrase"]
