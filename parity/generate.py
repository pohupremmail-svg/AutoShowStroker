"""Golden files from the Python implementation, for the C# port in engine/ to reproduce.

The Python app is the oracle: every case here is run through the real module in src/, and
whatever it answers is written down as the expected answer. engine/Gooner.Parity.Tests reads
the same files and has to arrive at exactly the same result. A behaviour the port changes on
purpose is listed in parity/allowlist.json with the reason - everything else that differs is
drift.

Run from the repo root:  .venv/Scripts/python.exe -m parity.generate [out_dir]
"""
import base64
import copy
import json
import random
import re
import sys
import tempfile
import time
from contextlib import ExitStack
from pathlib import Path
from unittest import mock

from src import achievements, changelog, session_files, utils
from src.achievements import AchievementTracker
from src.BeatHandler import BeatHandler, Segment
from src.CalloutHandler import TRIGGER_KEYS, CalloutHandler
from src.ScoreTracker import ScoreTracker
from src.SessionRecorder import SessionRecorder
from src.SessionScript import SessionScript
from src.user_data import LEGACY_DEAD_KEYS, UserDataStore

OUT_DIR = Path(__file__).resolve().parent / "out"


# --- session_files ---------------------------------------------------------------------------


def _handwritten():
    """A minimal, complete session - the base every validation case mutates."""
    return {
        "format": 1,
        "duration_sec": 120.0,
        "segments": [
            {"kind": "beat", "pattern": "Standard Beat", "freq": 2.0, "duration_sec": 60.0},
            {"kind": "pause", "pattern": None, "freq": None, "duration_sec": 10.0},
            {"kind": "finale", "pattern": "Quick Swing", "freq": 4.5, "duration_sec": 50.0},
        ],
        "custom_patterns": {},
        "climax": {"at_sec": 100.0, "outcome": "ruined"},
        "fake_climaxes": [40.0],
        "media": [{"at_sec": 0.0}, {"at_sec": 30.0}],
    }


def _mutated(name, change):
    saved = _handwritten()
    change(saved)
    return name, saved


def _set(path, value):
    """A change that sets saved[a][b]... = value; a missing parent is an error in the case."""
    def change(saved):
        target = saved
        for key in path[:-1]:
            target = target[key]
        target[path[-1]] = value
    return change


def _delete(path):
    def change(saved):
        target = saved
        for key in path[:-1]:
            target = target[key]
        del target[path[-1]]
    return change


def validation_cases():
    cases = [
        _mutated("well_formed", lambda s: None),
        _mutated("no_media", _set(["media"], [])),
        _mutated("media_null", _set(["media"], None)),
        _mutated("no_segments", _set(["segments"], [])),
        _mutated("segments_not_a_list", _set(["segments"], {"kind": "beat"})),
        _mutated("segments_missing", _delete(["segments"])),
        _mutated("duration_missing", _delete(["duration_sec"])),
        _mutated("duration_zero", _set(["duration_sec"], 0)),
        _mutated("duration_negative", _set(["duration_sec"], -5.5)),
        _mutated("duration_string", _set(["duration_sec"], "120")),
        _mutated("duration_int", _set(["duration_sec"], 120)),
        _mutated("unknown_rhythm", _set(["segments", 0, "pattern"], "Furious Wiggle")),
        _mutated("rhythm_not_a_string", _set(["segments", 0, "pattern"], 5)),
        _mutated("rhythm_null_on_beat", _set(["segments", 0, "pattern"], None)),
        _mutated("rhythm_missing", _delete(["segments", 0, "pattern"])),
        _mutated("unknown_kind", _set(["segments", 0, "kind"], "crescendo")),
        _mutated("kind_null", _set(["segments", 0, "kind"], None)),
        _mutated("kind_number", _set(["segments", 0, "kind"], 3)),
        _mutated("kind_float", _set(["segments", 0, "kind"], 2.5)),
        _mutated("kind_list", _set(["segments", 0, "kind"], ["beat", 1, None, 1.5])),
        _mutated("kind_object", _set(["segments", 0, "kind"], {"a": "it's"})),
        _mutated("freq_missing", _set(["segments", 0, "freq"], None)),
        _mutated("freq_zero", _set(["segments", 0, "freq"], 0)),
        _mutated("freq_string", _set(["segments", 0, "freq"], "fast")),
        _mutated("segment_duration_missing", _delete(["segments", 0, "duration_sec"])),
        _mutated("segment_duration_zero", _set(["segments", 0, "duration_sec"], 0)),
        _mutated("segment_not_an_object", _set(["segments", 1], "pause")),
        _mutated("pause_with_pattern", _set(["segments", 1, "pattern"], "Standard Beat")),
        _mutated("pause_with_freq", _set(["segments", 1, "freq"], 2.0)),
        _mutated("custom_rhythm_defined", lambda s: (
            s.update(custom_patterns={"Furious Wiggle": [1, 2, -1]}),
            s["segments"][0].update(pattern="Furious Wiggle"),
        )),
        _mutated("custom_rhythm_broken_steps", lambda s: (
            s.update(custom_patterns={"Broken": [0, 9, "x"]}),
            s["segments"][0].update(pattern="Broken"),
        )),
        _mutated("custom_rhythm_float_step", _set(["custom_patterns"], {"Floaty": [1.0, 2]})),
        _mutated("custom_rhythm_empty", _set(["custom_patterns"], {"Empty": []})),
        _mutated("custom_rhythm_not_a_list", _set(["custom_patterns"], {"Odd": "1,2"})),
        _mutated("custom_patterns_not_an_object", _set(["custom_patterns"], [1, 2])),
        _mutated("custom_patterns_null", _set(["custom_patterns"], None)),
        _mutated("climax_missing", _delete(["climax"])),
        _mutated("climax_null", _set(["climax"], None)),
        _mutated("climax_not_an_object", _set(["climax"], "at 100")),
        _mutated("climax_past_end", _set(["climax", "at_sec"], 999.0)),
        _mutated("climax_past_end_rounding_half_even", _set(["climax", "at_sec"], 120.5)),
        _mutated("climax_past_end_rounding_up", _set(["climax", "at_sec"], 121.5)),
        _mutated("climax_negative", _set(["climax", "at_sec"], -1)),
        _mutated("climax_at_missing", _delete(["climax", "at_sec"])),
        _mutated("climax_outcome_invented", _set(["climax", "outcome"], "explosive")),
        _mutated("climax_outcome_missing", _delete(["climax", "outcome"])),
        _mutated("climax_outcome_number", _set(["climax", "outcome"], 1)),
        _mutated("fake_past_end", _set(["fake_climaxes"], [999.0])),
        _mutated("fake_int_past_end", _set(["fake_climaxes"], [40, 999])),
        _mutated("fake_negative", _set(["fake_climaxes"], [-0.5])),
        _mutated("fake_string", _set(["fake_climaxes"], ["40"])),
        _mutated("fake_tiny_float", _set(["fake_climaxes"], [1e-05, -1e-05])),
        _mutated("fake_not_a_list", _set(["fake_climaxes"], "40")),
        _mutated("fake_null", _set(["fake_climaxes"], None)),
        _mutated("media_backwards", _set(["media"], [{"at_sec": 30.0}, {"at_sec": 10.0}])),
        _mutated("media_past_end", _set(["media"], [{"at_sec": 0.0}, {"at_sec": 130.4}])),
        _mutated("media_entry_without_time", _set(["media"], [{"path": "a.png"}])),
        _mutated("media_entry_not_an_object", _set(["media"], [5])),
        _mutated("media_not_a_list", _set(["media"], {"at_sec": 0})),
        _mutated("everything_wrong_at_once", lambda s: (
            s.update(duration_sec=-1),
            s["segments"][0].update(kind="crescendo", freq=None, pattern="Nope"),
            s["segments"][1].update(pattern="Standard Beat", duration_sec=0),
            s.update(climax={"at_sec": "late", "outcome": "boom"}),
            s.update(fake_climaxes=[-3]),
            s.update(media=[{"at_sec": 5}, {"at_sec": 1}]),
        )),
        # Python quirk: bool is an int, so these pass there. The port refuses them on purpose
        # (parity/allowlist.json).
        _mutated("duration_bool", _set(["duration_sec"], True)),
        _mutated("freq_bool", _set(["segments", 0, "freq"], True)),
    ]
    return [{"name": name, "input": saved, "expected": session_files.validate_session(saved)}
            for name, saved in cases]


def _timeline(**overrides):
    base = {
        "started_at": 5000.0,
        "ended_at": 5200.0,
        "climax_at": 5180.0,
        "climax_outcome": "ruined",
        "fake_climaxes": [5040.0, 5120.0],
        "segments": [
            {"kind": "beat", "pattern": "Quick Swing", "freq": 2.3, "start": 5000.0, "end": 5060.0,
             "planned_sec": 58.0,
             "media": [{"path": "C:\\pics\\a.png", "start": 5000.0, "end": 5030.0, "carried_over": False},
                       {"path": "C:\\pics\\b.png", "start": 5030.0, "end": 5090.0, "carried_over": False}]},
            {"kind": "pause", "pattern": None, "freq": None, "start": 5060.0, "end": 5075.0,
             "media": [{"path": "C:\\pics\\b.png", "start": 5030.0, "end": 5090.0, "carried_over": True}]},
            {"kind": "beat", "pattern": "My Rhythm", "freq": 1.7, "start": 5075.0, "end": 5110.5,
             "planned_sec": 35.0, "media": []},
            {"kind": "finale", "pattern": "Build Up", "freq": 4.5, "start": 5110.5, "end": 5200.0,
             "planned_sec": 1.0, "media": []},
        ],
    }
    base.update(overrides)
    return base


def saving_cases():
    """to_saved_session, minus the two fields that depend on when and where it runs."""
    library = {"My Rhythm": [1, 2, -1], "Unused": [1], "Also Mine": [3, -3]}
    cases = [
        ("full", _timeline(), library),
        ("no_climax", _timeline(climax_at=None, climax_outcome=None), library),
        ("no_custom_library", _timeline(), {}),
        ("started_at_zero", _timeline(started_at=0.0, ended_at=200.0, climax_at=180.0,
                                      fake_climaxes=[]), library),
    ]
    out = []
    for name, timeline, patterns in cases:
        saved = session_files.to_saved_session(timeline, custom_patterns=patterns)
        saved.pop("app_version")
        saved.pop("saved_at")
        out.append({"name": name, "timeline": timeline, "custom_patterns": patterns, "expected": saved})
    return out


def describe_cases():
    complete = session_files.to_saved_session(_timeline())
    complete["saved_at"] = "2026-10-15 12:00"
    cases = {
        "complete": complete,
        "stripped": session_files.strip_paths(complete),
        "no_climax": dict(complete, climax=None),
        "one_fake": dict(complete, fake_climaxes=[12.0]),
        "no_fakes": dict(complete, fake_climaxes=[]),
        "unknown_outcome": dict(complete, climax={"at_sec": 1.0, "outcome": "explosive"}),
        "long": dict(complete, duration_sec=3735.9),
        "no_saved_at": {k: v for k, v in complete.items() if k != "saved_at"},
        "no_duration": dict(complete, duration_sec=None),
    }
    return [{"name": name, "input": saved, "expected": session_files.describe(saved)}
            for name, saved in cases.items()]


def reading_cases():
    """read_session_file on raw file contents: the session it returns, or the message it
    refuses with."""
    valid = json.dumps(_handwritten())
    future = _handwritten()
    future["format"] = session_files.FORMAT_VERSION + 1
    broken = _handwritten()
    broken["segments"][0]["pattern"] = "Furious Wiggle"
    broken["media"] = [{"at_sec": 5}, {"at_sec": 1}]
    raw = {
        "valid": valid,
        "future_format": json.dumps(future),
        "not_a_session": json.dumps({"hello": "world"}),
        "a_list": "[1, 2, 3]",
        "corrupt": "{not json at all",
        "empty": "",
        "invalid_content": json.dumps(broken),
        "non_ascii": json.dumps(dict(_handwritten(), saved_at="Süß – heute"), ensure_ascii=False),
    }
    out = []
    with tempfile.TemporaryDirectory() as tmp:
        for name, text in raw.items():
            path = Path(tmp) / f"{name}.json"
            path.write_text(text, encoding="utf-8")
            try:
                result = {"ok": session_files.read_session_file(path)}
            except session_files.UnsupportedSessionFile as error:
                result = {"refused": str(error)}
            out.append({"name": name, "text": text, "expected": result})
    return out


def clock_cases():
    seconds = [0, 0.4, 0.5, 1.5, 2.5, 42, 59.5, 60, 60.5, 125, 522.9, 3599.99, 3600, 3735, 86399, 90061.7]
    return {
        "format_clock": [[s, utils.format_clock(s)] for s in seconds],
        "format_duration": [[s, utils.format_duration(s)] for s in seconds],
    }


# --- SessionScript -----------------------------------------------------------------------------


def _drain(script, first_index):
    """Everything a script hands out, in the order the consumers would ask for it.

    Gaps and paths are drained one after the other, not interleaved: they run on separate
    cursors, and a port that shared one would hand out half as many of the second.
    """
    out = {
        "segment_count": script.segment_count,
        "duration": script.duration,
        "has_media_paths": script.has_media_paths,
        "climax_offset": script.climax_offset,
        "climax_outcome": script.climax_outcome,
        "fake_offsets": script.fake_offsets,
        "custom_patterns": script.custom_patterns,
        "had_segments": script.has_segments_left,
    }
    segments, index = [], first_index
    while (segment := script.next_segment(index)) is not None:
        segments.append(list(segment))
        index += 1
    out["segments"] = segments
    out["has_segments_left"] = script.has_segments_left
    out["gaps"] = list(iter(script.next_media_gap, None))
    out["paths"] = list(iter(script.next_media_path, None))
    return out


def script_cases():
    recorded = session_files.to_saved_session(_timeline(), {"My Rhythm": [1, -1, 0, 1], "Unplayed": [1]})
    partially_stripped = _handwritten()
    partially_stripped["media"] = [{"at_sec": 0.0, "path": "a.png"}, {"at_sec": 10.0},
                                   {"at_sec": 25.0, "path": "c.png"}]
    integers = _handwritten()
    integers["duration_sec"] = 120
    integers["segments"][0]["duration_sec"] = 60
    integers["segments"][0]["freq"] = 2
    integers["media"] = [{"at_sec": 0}, {"at_sec": 30}]
    overlong = _handwritten()
    overlong["media"] = [{"at_sec": 0.0}, {"at_sec": 150.0}]  # past the end: the gap clamps at 0
    sessions = {
        "handwritten": (_handwritten(), False),
        "recorded": (recorded, False),
        "recorded_paths_ignored": (recorded, True),
        "stripped": (session_files.strip_paths(recorded), False),
        "partially_stripped": (partially_stripped, False),
        "partially_stripped_ignored": (partially_stripped, True),
        "no_climax": (dict(_handwritten(), climax=None), False),
        "climax_without_moment": (dict(_handwritten(), climax={"at_sec": None, "outcome": None}), False),
        "no_fakes": (dict(_handwritten(), fake_climaxes=[]), False),
        "no_duration": (dict(_handwritten(), duration_sec=None), False),
        "zero_duration": (dict(_handwritten(), duration_sec=0), False),
        "integers": (integers, False),
        "overlong_media": (overlong, False),
        "no_media": (dict(_handwritten(), media=[]), False),
        "no_segments": (dict(_handwritten(), segments=[]), False),
        "no_custom_patterns": (dict(_handwritten(), custom_patterns=None), False),
    }
    return [{"name": name, "input": saved, "ignore_paths": ignore,
             "expected": _drain(SessionScript(copy.deepcopy(saved), ignore_paths=ignore), first_index=5)}
            for name, (saved, ignore) in sessions.items()]


# --- ScoreTracker ------------------------------------------------------------------------------

# Steps are [clock, call, *args]: the clock is set first, then the call is made. A call that is
# not a ScoreTracker method is a probe, and what it reads goes into "probes" in order.
_PROBES = {
    "live": lambda t: t.live_metrics(),
    "chase": lambda t, bests: (lambda s: list(s) if s else None)(t.record_chase_status(bests)),
    "bests": lambda t: t.get_all_time_bests(),
    "infos": lambda t: t.deliver_infos(),
    "records": lambda t: t.last_session_new_records,
    "history": lambda t: [_without_ended_at(e) for e in t.get_history()],
}


def _without_ended_at(entry):
    """ended_at is the wall clock at the end, which neither side can be held to."""
    return {k: v for k, v in entry.items() if k != "ended_at"}


def _run_tracker(history, steps):
    clock = [0.0]
    probes = []
    with tempfile.TemporaryDirectory() as tmp, mock.patch("time.time", lambda: clock[0]):
        store = UserDataStore(base_dir=Path(tmp))
        if history is not None:
            store.save("session_history", history)
        tracker = ScoreTracker(data_store=store)
        for at, call, *args in steps:
            clock[0] = at
            if call in _PROBES:
                probes.append(_PROBES[call](tracker, *args))
            else:
                getattr(tracker, call)(*args)
        saved = store.load("session_history", None)
    return {"probes": probes, "saved_history": None if saved is None else [_without_ended_at(e) for e in saved]}


def _past(dur, beats, speed, fakeouts, **extra):
    return {"ended_at": "2026-01-01 00:00", "total_dur_sec": dur, "total_num_beat": beats,
            "average_beat_speed_active": speed, "fakeout_count": fakeouts, **extra}


def tracker_cases():
    played = [
        [0.0, "session_started"],
        [1.0, "beat"], [1.5, "beat"], [2.0, "beat_changed", 2.0, "Quick Swing"],
        [2.5, "beat"], [3.0, "beat_paused"], [8.0, "beat_resumed"],
        [9.0, "beat_changed", 3.0, "Standard Beat"], [9.5, "beat"],
        [10.0, "beat_changed", 2.0, "Quick Swing"], [11.0, "media_skipped"], [12.0, "media_repeated"],
    ]
    history = [_past(30.0, 40, 1.5, 1), _past(12.0, 90, 2.5, 0, climax_outcome="ruined")]
    cases = {
        "a_plain_session": (None, [*played, [60.0, "session_ended"], [60.0, "infos"], [60.0, "records"],
                                   [60.0, "bests"], [60.0, "history"]]),
        "records_against_a_history": (history, [*played, [45.0, "beat"], [45.0, "session_ended"],
                                                [45.0, "records"], [45.0, "bests"]]),
        "tying_a_record": ([_past(10.0, 1, 0.1, 0)], [[0.0, "session_started"], [10.0, "session_ended"],
                                                      [10.0, "records"]]),
        "ended_without_a_start": (None, [[5.0, "session_ended"], [5.0, "infos"], [5.0, "history"]]),
        "ended_inside_a_pause": (None, [[0.0, "session_started"], [1.0, "beat"], [4.0, "beat_paused"],
                                        [10.0, "session_ended"], [10.0, "infos"]]),
        "paused_the_whole_time": (None, [[0.0, "session_started"], [0.0, "beat_paused"],
                                         [10.0, "beat_resumed"], [10.0, "session_ended"], [10.0, "infos"]]),
        "a_second_pause_at_zero": (None, [[0.0, "session_started"], [0.0, "beat_paused"],
                                          [3.0, "beat_paused"], [5.0, "beat_resumed"], [9.0, "session_ended"],
                                          [9.0, "infos"]]),
        "everything_the_user_reports": (None, [
            [0.0, "session_started"], [0.5, "replay_started"], [1.0, "fake_climax_triggered"],
            [2.0, "fell_for_fake_climax"], [3.0, "fake_climax_triggered"], [4.0, "edge_reached"],
            [5.0, "climax_decided", "denied"], [6.0, "outcome_reported", "came"],
            [7.0, "session_ended"], [7.0, "infos"], [7.0, "history"]]),
        "a_tie_goes_to_the_first_pattern": (None, [
            [0.0, "session_started"], [1.0, "beat_changed", 1.0, "A"], [2.0, "beat_changed", 1.0, "B"],
            [3.0, "beat_changed", 1.0, "B"], [4.0, "beat_changed", 1.0, "A"], [5.0, "infos"]]),
        "the_next_session_starts_clean": (None, [
            *played, [20.0, "climax_decided", "real"], [20.0, "session_ended"],
            [30.0, "session_started"], [31.0, "beat"], [40.0, "session_ended"], [40.0, "infos"],
            [40.0, "records"], [40.0, "history"]]),
        "an_old_history_without_newer_keys": ([
            {"ended_at": "2025-01-01 00:00", "total_dur_sec": 99.0, "total_num_beat": 10},
            {"ended_at": "2025-01-02 00:00", "total_dur_sec": None, "fakeout_count": 4},
        ], [[0.0, "bests"], [0.0, "session_started"], [100.0, "session_ended"], [100.0, "records"],
            [100.0, "bests"]]),
        "live_and_the_chase": ([_past(100.0, 10, 0.5, 2)], [
            [0.0, "live"], [0.0, "chase", {"total_num_beat": 10}],
            [0.0, "session_started"], [1.0, "beat"], [2.0, "beat"],
            [5.0, "live"], [5.0, "chase", {"total_num_beat": 10, "total_dur_sec": 100.0}],
            [5.0, "beat_paused"], [15.0, "beat_resumed"], [20.0, "live"],
            [81.0, "chase", {"total_dur_sec": 100.0, "fakeout_count": 2}],
            [82.0, "fake_climax_triggered"], [82.0, "fake_climax_triggered"], [82.0, "fake_climax_triggered"],
            [82.0, "chase", {"total_dur_sec": 100.0, "fakeout_count": 2}],
            [82.0, "chase", {"total_dur_sec": 0, "fakeout_count": None, "average_beat_speed_active": 0.01}],
            [90.0, "session_ended"], [90.0, "records"]]),
    }
    return [{"name": name, "history": history, "steps": steps, "expected": _run_tracker(copy.deepcopy(history), steps)}
            for name, (history, steps) in cases.items()]


def metric_format_cases():
    values = {
        "total_dur_sec": [0, 0.5, 59.4, 125.0, 3600.0, 3735.9],
        "average_beat_speed_active": [0, 0.005, 0.4321, 1.0, 2.675, 12.3456],
        "total_num_beat": [0, 42, 12345],
        "fakeout_count": [0, 3],
    }
    return [[metric, value, ScoreTracker.format_metric_value(metric, value)]
            for metric, metric_values in values.items() for value in metric_values]


# --- settings ----------------------------------------------------------------------------------

# Text the registry may hand over. QSettings gives most values back as strings, and the Python
# read sides turn them into numbers with float() / int(float()) and into switches with type=bool.
SETTING_TEXTS = {
    "integer": "45", "fraction": "45.5", "negative": "-2.5", "padded": " 7 ", "exponent": "1e3",
    "plus": "+5", "cut_up": "5.7", "cut_down": "-5.7", "zero": "0", "zero_point_zero": "0.0",
    "double_zero": "00", "minus_zero": "-0", "true": "true", "false": "false", "False": "False",
    "FALSE": "FALSE", "padded_false": " false ", "yes": "yes", "no": "no", "off": "off",
    "one": "1", "empty": "", "words": "lots", "path_like": "C:/somewhere",
}


def _settings_read(raw):
    from PyQt6.QtCore import QSettings

    def attempt(read):
        try:
            return read()
        except (ValueError, OverflowError):
            return "error"

    with tempfile.TemporaryDirectory() as tmp:
        settings = QSettings(str(Path(tmp) / "s.ini"), QSettings.Format.IniFormat)
        settings.setValue("k", raw)
        return {
            "double": attempt(lambda: float(settings.value("k", 0.0))),
            "int": attempt(lambda: int(float(settings.value("k", 0)))),
            "bool_default_false": bool(settings.value("k", False, type=bool)),
            "bool_default_true": bool(settings.value("k", True, type=bool)),
            "string": str(settings.value("k", "")),
        }


def settings_cases():
    return [{"name": name, "text": raw, "expected": _settings_read(raw)} for name, raw in SETTING_TEXTS.items()]


def settings_keys():
    """Every setting the app keeps, as "Group/name": what an owner reads with a default, and what
    is written by literal name outside those. The engine imports each one under that name, so it
    has to read every one of them by it."""
    from src.ClimaxHandler import ClimaxHandler
    from src.GoonerApp import GoonerApp
    from src.PlaylistPlayer import PlaylistPlayer
    from src.plugins.intiface.consent import CONSENT_KEY
    from src.plugins.intiface.controller import SETTINGS_PREFIX, IntifaceController
    from src.SessionHudWidget import SessionHudWidget

    keys = {CONSENT_KEY}
    for owner in (BeatHandler, CalloutHandler, ClimaxHandler, GoonerApp, PlaylistPlayer, SessionHudWidget):
        keys.update(f"{owner.SETTINGS_GROUP}/{name}" for name in owner.DEFAULTS)
    keys.update(f"{SETTINGS_PREFIX}/{name}" for name in IntifaceController.DEFAULTS)
    for path in sorted((utils.get_project_root() / "src").rglob("*.py")):
        keys.update(re.findall(r'setValue\(\s*"([A-Za-z]+/[a-z_]+)"', path.read_text(encoding="utf-8")))
    return sorted(keys)


# --- the registry the engine imports the settings from ---------------------------------------------

# What the app hands QSettings, by type - SettingsDialog's spin boxes floats and ints, its check
# boxes bools, the language box a str, the tone and rhythm choices a list of str - and the corners
# QSettings' native format escapes or stores differently.
REGISTRY_VALUES = {
    "float": 0.25, "float_whole": 2.0, "float_small": 1e-05, "float_inexact": 0.1 + 0.2,
    "float_negative": -2.5, "int": 7, "int_negative": -3, "int_large": 2**40,
    "true": True, "false": False, "text": "en", "empty_text": "", "version": "0.7.1",
    "number_text": "45.5", "at_text": "@home", "double_at_text": "@@home", "unicode": "Süß 日本語",
    "newline": "a\nb", "path_like": "C:/somewhere", "json_text": json.dumps([{"total_num_beat": 1}]),
    "list": ["flirty", "teasing"], "list_of_one": ["flirty"], "list_empty": [],
    "list_with_empty_text": ["", "x"], "list_with_at": ["@a", "b"], "none": None,
}

_REGISTRY_KINDS = {0: "None", 1: "Sz", 2: "ExpandSz", 3: "Binary", 4: "DWord", 7: "MultiSz", 11: "QWord"}


def _raw_values(key):
    """Every value of an open key as the bytes the registry holds - what Qt wrote, before anything
    decodes it - and its kind. winreg only hands values back decoded."""
    import ctypes
    import winreg
    from ctypes import wintypes

    query = ctypes.windll.advapi32.RegQueryValueExW
    query.argtypes = [wintypes.HKEY, wintypes.LPCWSTR, wintypes.LPDWORD, wintypes.LPDWORD,
                      ctypes.c_void_p, wintypes.LPDWORD]
    index = 0
    while True:
        try:
            name = winreg.EnumValue(key, index)[0]
        except OSError:
            return
        index += 1
        kind, size = wintypes.DWORD(), wintypes.DWORD()
        query(key.handle, name, None, ctypes.byref(kind), None, ctypes.byref(size))
        data = (ctypes.c_ubyte * size.value)()
        query(key.handle, name, None, ctypes.byref(kind), data, ctypes.byref(size))
        yield name, kind.value, bytes(data[: size.value])


def _registry_dump(key, prefix=""):
    import winreg

    found = [{"key": prefix + name, "kind": _REGISTRY_KINDS.get(kind, "Unknown"),
              "bytes": base64.b64encode(data).decode("ascii")} for name, kind, data in _raw_values(key)]
    index = 0
    while True:
        try:
            group = winreg.EnumKey(key, index)
        except OSError:
            return found
        index += 1
        with winreg.OpenKey(key, group) as child:
            found.extend(_registry_dump(child, prefix + group + "/"))


def _delete_tree(root, path):
    import winreg

    with winreg.OpenKey(root, path) as key:
        groups = []
        while True:
            try:
                groups.append(winreg.EnumKey(key, len(groups)))
            except OSError:
                break
    for group in groups:
        _delete_tree(root, f"{path}\\{group}")
    winreg.DeleteKey(root, path)


def _typed_reads(settings, key):
    """The read sides on a scalar: float(), int(float()), type=bool, str()."""
    def attempt(read):
        try:
            return read()
        except (ValueError, OverflowError, TypeError):
            return "error"

    return {
        "double": attempt(lambda: float(settings.value(key, 0.0))),
        "int": attempt(lambda: int(float(settings.value(key, 0)))),
        "bool_default_false": bool(settings.value(key, False, type=bool)),
        "bool_default_true": bool(settings.value(key, True, type=bool)),
        "string": attempt(lambda: str(settings.value(key, ""))),
    }


def registry_cases():
    """REGISTRY_VALUES written by a real QSettings in its native format, into a throwaway key of
    its own - never the app's - then dumped byte for byte and read back by QSettings, the way the
    app would read them. The key goes again however this ends."""
    import uuid
    import winreg

    from PyQt6.QtCore import QSettings

    parent = f"Software\\Gooner.Parity-{uuid.uuid4().hex}"
    path = f"{parent}\\GoonerApp"
    try:
        settings = QSettings(f"HKEY_CURRENT_USER\\{path}", QSettings.Format.NativeFormat)
        for name, value in REGISTRY_VALUES.items():
            settings.setValue(f"Values/{name}", value)
        settings.setValue("top", "at the root")
        settings.setValue("Outer/Inner/deep", "two groups down")
        settings.sync()
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, path) as key:
            dump = _registry_dump(key)
        cases = []
        for entry in dump:
            value = settings.value(entry["key"])
            case = {"name": entry["key"], **entry, "value": value}
            if isinstance(value, (str, int)):
                case["reads"] = _typed_reads(settings, entry["key"])
            cases.append(case)
        del settings
        return cases
    finally:
        try:
            _delete_tree(winreg.HKEY_CURRENT_USER, parent)
        except OSError:
            pass


# --- CalloutHandler ----------------------------------------------------------------------------


def _qt_core():
    """CalloutHandler is a QObject with a QTimer, which wants an application object to exist."""
    from PyQt6.QtCore import QCoreApplication

    return QCoreApplication.instance() or QCoreApplication([])


def _write_synthetic_callouts(root: Path):
    """Two languages, tones of different sizes, one tone only one language has, one tone file
    that is broken. Lines name where they come from, so nothing explicit lands in a golden file."""
    sizes = {("en", "flirty"): 3, ("en", "shy"): 1, ("en", "degrading"): 2, ("en", "zzz_extra"): 1,
             ("de", "flirty"): 2, ("de", "shy"): 2}
    for (lang, tone), size in sizes.items():
        (root / lang).mkdir(parents=True, exist_ok=True)
        data = {key: [f"{lang} {tone} {key} {i}" for i in range(size)] for key in TRIGGER_KEYS}
        (root / lang / f"{tone}.json").write_text(json.dumps(data), encoding="utf-8")
    (root / "de" / "degrading.json").write_text("{broken", encoding="utf-8")


def _snapshot(handler):
    return {
        "lang": handler.lang,
        "available_languages": list(handler.available_languages),
        "available_tones": handler.available_tones,
        "tones_for": {lang: handler.tones_for(lang) for lang in handler.available_languages},
        "selected_tones": list(handler.selected_tones),
        "usable_tones": handler._usable_tones(),
    }


def callout_structure():
    """The shipped files, as numbers: which languages and tones exist, and how many lines each
    category holds. Never the lines themselves."""
    _qt_core()
    handler = CalloutHandler()
    counts = {lang: {tone: {key: len(phrases) for key, phrases in data.items()} for tone, data in tones.items()}
              for lang, tones in handler.callout_data.items()}
    labels = {tone: CalloutHandler.tone_label(tone)
              for tone in [*CalloutHandler.TONE_LABELS, "my_new_tone", "o'neil", "2nd_wave", "ALL_CAPS", "x"]}
    return {"snapshot": _snapshot(handler), "counts": counts, "labels": labels}


def _scripted(uniforms, choices):
    """random.uniform and random.choice answering from the given lists, as the C# side's scripted
    source does: a choice index is taken modulo the length of what is chosen from."""
    def uniform(_a, _b):
        return uniforms.pop(0)

    def choice(seq):
        return seq[choices.pop(0) % len(seq)]

    return mock.patch("random.uniform", uniform), mock.patch("random.choice", choice)


def _talk(handler, steps):
    said = []
    handler.new_tease_event.connect(said.append)
    for call, *args in steps:
        if call == "hide":
            handler._tease_timer_handler()
        elif call == "set":
            setattr(handler, args[0], args[1])
        else:
            getattr(handler, call)(*args)
        said.append(["is_teasing", handler.is_teasing])
    return said


def callout_draws():
    _qt_core()
    runs = {
        "gates": ([0.4, 0.6, 0.2], [0, 0, 1, 1, 2, 0], [
            ["set", "active_callout", True], ["set", "talking_chance", 0.5],
            ["select_and_output_sentence", "session_started"], ["hide"],
            ["select_and_output_sentence", "session_started"],
            ["select_and_output_sentence", "pause_start"], ["select_and_output_sentence", "pause_end"], ["hide"],
            ["force_output_sentence", "climax_real"]]),
        "inactive_and_forced": ([], [3, 5, 1, 1], [
            ["select_and_output_sentence", "session_started"], ["force_output_sentence", "climax_denied"],
            ["force_output_sentence", "fake_climax_reveal"]]),
        "several_tones": ([0.0] * 6, [0, 0, 1, 0, 2, 1, 1, 2, 0, 1, 2, 2], [
            ["set", "active_callout", True], ["set", "talking_chance", 1.0],
            ["set_tones", ["flirty", "shy", "degrading"]],
            ["select_and_output_sentence", "media_skipped"], ["hide"],
            ["select_and_output_sentence", "media_skipped"], ["hide"],
            ["select_and_output_sentence", "media_repeated"], ["hide"],
            ["select_and_output_sentence", "media_repeated"], ["hide"],
            ["select_and_output_sentence", "edge_reached"], ["hide"],
            ["select_and_output_sentence", "edge_reached"]]),
        "beat_changes": ([0.7, 0.0, 0.7, 0.0, 0.7, 0.0, 0.2, 0.0], [0, 0] * 4, [
            ["set", "active_callout", True], ["set", "talking_chance", 1.0],
            ["beat_change_general", 2.0, "Standard Beat"], ["hide"],
            ["beat_change_general", 1.0, "Quick Swing"], ["hide"],
            ["beat_change_general", 1.0, "Quick Swing"], ["hide"],
            ["beat_change_general", 3.0, "Build Up"], ["hide"]]),
        "other_language_and_fallback": ([0.0] * 3, [1, 1, 0, 1, 1, 0], [
            ["set", "active_callout", True], ["set", "talking_chance", 1.0],
            ["set_tones", ["zzz_extra"]], ["select_and_output_sentence", "session_started"], ["hide"],
            ["set_lang", "de"], ["select_and_output_sentence", "session_started"], ["hide"],
            ["set_tones", ["nonexistent"]], ["select_and_output_sentence", "session_started"]]),
        # A draw exactly on the talking chance still speaks: the gate is "> chance", not ">=".
        "a_draw_on_the_chance_itself": ([0.5, 1.0, 0.0], [0, 0, 1, 1, 0, 2], [
            ["set", "active_callout", True], ["set", "talking_chance", 0.5],
            ["select_and_output_sentence", "session_started"], ["hide"],
            ["set", "talking_chance", 1.0], ["select_and_output_sentence", "pause_end"], ["hide"],
            ["set", "talking_chance", 0.0], ["select_and_output_sentence", "pause_end"]]),
        "an_unknown_category": ([0.0], [0, 0], [
            ["set", "active_callout", True], ["set", "talking_chance", 1.0],
            ["select_and_output_sentence", "does_not_exist"], ["force_output_sentence", "nor_this"]]),
    }
    out = []
    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp) / "callouts"
        _write_synthetic_callouts(root)
        for name, (uniforms, choices, steps) in runs.items():
            uniforms, choices = list(uniforms), list(choices)
            draws = {"uniforms": list(uniforms), "choices": list(choices)}
            patch_uniform, patch_choice = _scripted(uniforms, choices)
            with patch_uniform, patch_choice:
                handler = CalloutHandler(callout_dir=root)
                said = _talk(handler, steps)
            out.append({"name": name, **draws, "steps": steps,
                        "expected": {"said": said, "snapshot": _snapshot(handler),
                                     "left": {"uniforms": len(uniforms), "choices": len(choices)}}})
    return out


def callout_custom_files():
    """Loading and unloading phrase files. Paths in messages are written as <dir>, since the two
    sides run in different temporary folders."""
    _qt_core()
    files = {
        "plain.json": {"session_started": ["custom start"], "pause_start": ["custom pause"]},
        "messy.json": {"session_started": ["messy start"], "not_a_real_key": ["x"], "pause_start": "not a list",
                       "pause_end": [1, 2], "edge_reached": []},
        "second.json": {"session_started": ["second start"]},
        "a_list.json": [1, 2, 3],
    }
    runs = {
        "any_tone": [["load", "plain.json", "en", None]],
        "one_tone": [["load", "plain.json", "en", "shy"]],
        "a_tone_the_language_lacks": [["load", "plain.json", "de", "zzz_extra"]],
        "messy": [["load", "messy.json", "en", "flirty"]],
        "two_then_unload_the_first": [["load", "plain.json", "en", None], ["load", "second.json", "en", None],
                                      ["unload", "plain.json"]],
        "unload_what_was_never_loaded": [["unload", "nowhere.json"]],
        "refusals": [["load", "plain.json", "fr", None], ["load", "plain.json", "en", "nonexistent"],
                     ["load", "plain.json", "en", ""], ["load", "missing.json", "en", None],
                     ["load", "a_list.json", "en", None], ["load", "plain.json", "en", None],
                     ["load", "plain.json", "en", None]],
        "clear": [["load", "plain.json", "en", None], ["clear"]],
        "reopened": [["load", "plain.json", "de", None], ["load", "second.json", "en", "shy"], ["reopen"]],
    }
    out = []
    for name, steps in runs.items():
        with tempfile.TemporaryDirectory() as tmp:
            base = Path(tmp)
            _write_synthetic_callouts(base / "callouts")
            for file_name, data in files.items():
                (base / file_name).write_text(json.dumps(data), encoding="utf-8")
            store = UserDataStore(base_dir=base / "appdata")
            handler = CalloutHandler(data_store=store, callout_dir=base / "callouts")
            results = []
            for call, *args in steps:
                try:
                    if call == "load":
                        handler.load_custom_file(str(base / args[0]), args[1], args[2])
                    elif call == "unload":
                        handler.unload_custom_file(str(base / args[0]))
                    elif call == "clear":
                        handler.clear_custom_phrase_files()
                    elif call == "reopen":
                        handler = CalloutHandler(data_store=store, callout_dir=base / "callouts")
                    results.append("ok")
                except ValueError as error:
                    message = str(error).replace(str(base), "<dir>")
                    # The reason after "Couldn't read" / "isn't valid JSON" is the OS's or the
                    # parser's wording - only the shape of the message is the app's.
                    for marker in ("Couldn't read", "isn't valid JSON"):
                        if marker in message:
                            message = message[:message.index(marker) + len(marker)]
                    results.append(message)
            saved = store.load("custom_phrase_files", None)

            def anonymous(entries, base=base):
                return [dict(e, path=e["path"].replace(str(base), "<dir>")) for e in entries]

            out.append({"name": name, "steps": steps, "files": files, "expected": {
                "results": results,
                "data": handler.callout_data,
                "snapshot": _snapshot(handler),
                "custom_phrase_files": anonymous(handler.custom_phrase_files),
                "saved": None if saved is None else anonymous(saved),
            }})
    return out


# --- BeatHandler -------------------------------------------------------------------------------
#
# Whole sessions, driven deterministically. The QTimers become timers on a fake clock that fire
# exactly when due, time.time() reads that clock, and every draw from random is recorded - the C#
# side replays the same draws and has to produce the same events at the same moments.


class _Clock:
    now = 0.0
    # Every fake QTimer of the run, in the order they were created - the order a tie fires in,
    # as on the C# side's ClockScheduler.
    timers = []


class _FakeTimer:
    """A QTimer on _Clock: it fires when advance() reaches its due time, and repeats unless the
    slot restarts or stops it, as a QTimer does."""

    def __init__(self, *_args):
        self.active = False
        self.due = 0.0
        self.interval_ms = 0
        self.generation = 0
        self.single_shot = False
        self._slots = []
        self.timeout = self
        _Clock.timers.append(self)

    def setSingleShot(self, single_shot):
        self.single_shot = single_shot

    def connect(self, slot):
        self._slots.append(slot)

    def setTimerType(self, *_args):
        pass

    def start(self, ms):
        self.interval_ms = ms
        self.due = _Clock.now + ms / 1000
        self.active = True
        self.generation += 1

    def stop(self):
        self.active = False
        self.generation += 1

    def isActive(self):
        return self.active

    def interval(self):
        return self.interval_ms

    def remainingTime(self):
        return max(0.0, (self.due - _Clock.now) * 1000) if self.active else -1

    def fire(self):
        for slot in self._slots:
            slot()


class _Recorder:
    """random.uniform/randint/choice/choices on a seeded generator, every draw written down in the
    two primitives the C# side's IRandomSource has: a fraction, or an index below n."""

    def __init__(self, seed):
        self._rng = random.Random(seed)
        self.draws = []

    def fraction(self):
        u = self._rng.random()
        self.draws.append(["u", u])
        return u

    def below(self, n):
        k = self._rng.randrange(n)
        self.draws.append(["i", n, k])
        return k

    def patches(self):
        def choices(population, weights=None, *, cum_weights=None, k=1):
            # CPython's own algorithm, on the recorded fraction.
            from bisect import bisect
            from itertools import accumulate

            cum = list(cum_weights) if cum_weights is not None else list(accumulate(weights))
            total = cum[-1] + 0.0
            hi = len(cum) - 1
            return [population[bisect(cum, self.fraction() * total, 0, hi)] for _ in range(k)]

        return [
            mock.patch("random.uniform", lambda a, b: a + (b - a) * self.fraction()),
            mock.patch("random.randint", lambda a, b: a + self.below(b - a + 1)),
            mock.patch("random.choice", lambda seq: seq[self.below(len(seq))]),
            mock.patch("random.choices", choices),
        ]


def _advance(until):
    """Fires every fake timer that has come due by `until`, earliest first, a tie going to the
    timer created first - each at the moment it was due."""
    while True:
        due = [timer for timer in _Clock.timers if timer.active and timer.due <= until]
        if not due:
            break
        timer = min(due, key=lambda candidate: candidate.due)  # min() keeps the first of a tie
        _Clock.now = timer.due
        if timer.single_shot:
            timer.stop()  # a single-shot QTimer has stopped by the time it signals
        generation = timer.generation
        timer.fire()
        if not timer.single_shot and timer.generation == generation and timer.active:
            timer.due += timer.interval_ms / 1000
    _Clock.now = until


def _segment(s):
    return None if s is None else [s.kind, s.duration_sec, s.freq, s.pattern_name, s.index]


class _Voice:
    """The callouts, as far as the climax is concerned: the lines it forces, written down."""

    def __init__(self, note):
        self._note = note

    def force_output_sentence(self, category):
        self._note("said", category)


def _play(config, steps, seed, climax_config=None):
    """One session through BeatHandler - and, given a climax_config, through ClimaxHandler as
    well, wired the way GoonerApp wires them. Returns the draws and the event log."""
    from src.ClimaxHandler import ClimaxHandler
    from src.SessionScript import SessionScript

    recorder = _Recorder(seed)
    log = []
    _Clock.timers = []
    with ExitStack() as stack:
        for patch in [
            mock.patch("src.BeatHandler.QTimer", _FakeTimer),
            mock.patch("src.ClimaxHandler.QTimer", _FakeTimer),
            mock.patch("time.time", lambda: _Clock.now),
            mock.patch.object(BeatHandler, "init_beat_sound", lambda self, path: None),
            *recorder.patches(),
        ]:
            stack.enter_context(patch)
        _Clock.now = steps[0][1]

        def note(name, *args):
            log.append([_Clock.now, name, *args])

        handler = BeatHandler()
        handler.selected_beat_patterns = list(BeatHandler.BEAT_PATTERNS_MAP)
        for key, value in config.items():
            setattr(handler, key, value)

        handler.beat_paused_event.connect(lambda: note("paused"))
        handler.beat_resumed_event.connect(lambda: note("resumed"))
        handler.beat_change_event.connect(lambda freq, name: note("changed", freq, name))
        handler.beat_event.connect(lambda: note("hit"))
        handler.beat_meter_update_event.connect(lambda state: note("meter", state))
        handler.session_planned_event.connect(lambda start, script: note("planned", start, script is not None))
        handler.plan_extended_event.connect(lambda added: note("extended", [_segment(s) for s in added]))
        handler.segment_started_event.connect(lambda s: note("segment", _segment(s)))
        handler.note_scheduled_event.connect(lambda: note("scheduled", handler.beat_meter_timer.interval_ms))

        climax = None
        if climax_config is not None:
            climax = ClimaxHandler(handler, _Voice(note))
            for key, value in climax_config.items():
                setattr(climax, key, value)
            # GoonerApp's wiring: the climax hears about the plan from the beat.
            handler.session_planned_event.connect(climax.on_session_planned)
            handler.plan_extended_event.connect(climax.on_plan_extended)
            handler.segment_started_event.connect(climax.on_segment_started)
            climax.outcome_decided_event.connect(lambda outcome: note("outcome", outcome))
            climax.status_changed_event.connect(lambda status: note("status", status))
            climax.fake_climax_triggered_event.connect(lambda: note("fake"))
            climax.fake_climax_revealed_event.connect(lambda: note("revealed"))

        for call, at, *args in steps:
            _advance(at)
            if call == "start":
                if climax is not None:
                    climax.session_started()
                handler.start_beat(script=SessionScript(args[0]) if args else None)
            elif call == "edge":
                # GoonerApp.edge_reached: the beat grants the pause, the climax waits as long.
                seconds = handler.edge_relief()
                note("edge_given", seconds)
                if seconds and climax is not None:
                    climax.postpone(seconds)
            elif call == "finale":
                handler.set_finale_at(args[0])
            elif call == "hold":
                handler.hold_final_segment()
            elif call == "replan":
                handler.replan_from_next_segment()
            elif call == "set":
                setattr(handler, args[0], args[1])
            elif call == "climax_set":
                setattr(climax, args[0], args[1])
                climax.settings_changed()
            elif call == "stop":
                handler.stop()
            elif call == "probe":
                extra = [] if climax is None else [
                    climax.finale_at, climax.outcome, sorted(climax.planned_fake_boundaries),
                    climax.scripted_fake_count, climax.climax_triggered, climax._fake_climax_pending]
                note("probe", [list(b) for b in handler.upcoming_beats(args[0])], handler.pause_progress(),
                     handler.is_paused(), _segment(handler.current_segment),
                     [_segment(s) for s in handler.planned_segments], handler._finale_at, handler.ramp_complete_at,
                     *extra)
    return recorder.draws, log


def beat_sessions():
    plain = {"ramping_active": False, "pause_chance": 0.0}
    probes = [["probe", t, 2.5] for t in (1000.3, 1007.77, 1031.0, 1059.9)]
    replay = {
        "format": 1, "duration_sec": 70.0, "custom_patterns": {"Borrowed": [1, -1, 3]},
        "climax": None, "fake_climaxes": [], "media": [],
        "segments": [
            {"kind": "beat", "pattern": "Quick Swing", "freq": 2.3, "duration_sec": 20.0},
            {"kind": "pause", "pattern": None, "freq": None, "duration_sec": 6.4},
            {"kind": "beat", "pattern": "Borrowed", "freq": 3.1, "duration_sec": 15.0},
            {"kind": "finale", "pattern": "Build Up", "freq": 4.6, "duration_sec": 28.6},
        ],
    }
    runs = {
        "defaults_with_ramp": ({}, [["start", 1000.0], *probes, ["advance", 1300.0], ["probe", 1300.0, 2.5]]),
        "plain_rhythms": (plain, [["start", 1000.0], *probes, ["advance", 1240.0]]),
        "pauses_everywhere": ({"pause_chance": 0.8, "min_pause_dur": 2, "max_pause_dur": 9},
                              [["start", 1000.0], *probes, ["advance", 1400.0]]),
        "a_finale": ({"min_ramp_duration": 120.0, "max_ramp_duration": 200.0},
                     [["start", 1000.0], ["finale", 1000.0, 1150.0], *probes, ["advance", 1145.0],
                      ["probe", 1145.0, 3.0], ["hold", 1150.0], ["advance", 1200.0], ["probe", 1200.0, 2.5]]),
        "finale_set_late": (plain, [["start", 1000.0], ["advance", 1050.0], ["finale", 1050.0, 1090.5],
                                    ["advance", 1110.0], ["probe", 1110.0, 2.5]]),
        "edges": ({"pause_chance": 0.1, "edge_pause_dur": 7},
                  [["start", 1000.0], ["finale", 1000.0, 1200.0], ["edge", 1033.3], ["probe", 1034.1, 2.5],
                   ["edge", 1036.0], ["advance", 1090.0], ["edge", 1090.0], ["advance", 1250.0]]),
        "settings_changed_mid_session": (plain, [
            ["start", 1000.0], ["advance", 1020.0], ["set", 1020.0, "min_beat_freq", 3.0],
            ["set", 1020.0, "max_beat_freq", 4.5],
            ["set", 1020.0, "selected_beat_patterns", ["Double Tap", "Held Breath"]],
            ["replan", 1020.0], ["advance", 1200.0]]),
        "inverted_and_odd_ranges": ({"min_pause_dur": 9, "max_pause_dur": 3, "pause_chance": 0.5,
                                     "min_beat_dur": 7.5, "max_beat_dur": 7.5, "selected_beat_patterns": []},
                                    [["start", 1000.0], ["advance", 1150.0]]),
        "a_replay": ({}, [["start", 1000.0, replay], *probes, ["edge", 1062.0], ["advance", 1200.0]]),
        "a_replay_that_runs_out": (plain, [["start", 1000.0, replay], ["finale", 1000.0, 1100.0], ["advance", 1180.0]]),
        "stopped_and_restarted": (plain, [["start", 1000.0], ["advance", 1012.4], ["stop", 1012.4],
                                          ["probe", 1013.0, 2.5], ["start", 1020.0], ["advance", 1060.0]]),
    }
    out = []
    for seed, (name, (config, steps)) in enumerate(runs.items()):
        draws, log = _play(config, steps, seed)
        out.append({"name": name, "config": config, "steps": steps, "draws": draws, "expected": log})
    return out


def climax_sessions():
    """Whole sessions with the climax wired in: drawn, clamped to the ramp, ruined and denied,
    fakes rolled and revealed, edges that push it back, settings changed on the way, replays."""
    quick = {"min_ramp_duration": 60.0, "max_ramp_duration": 90.0, "pause_chance": 0.1}
    soon = {"min_climax_after": 70.0, "max_climax_after": 120.0, "fake_climax_chance": 0.4,
            "min_fake_climax_delay": 2.0, "max_fake_climax_delay": 5.0}
    replay = {
        "format": 1, "duration_sec": 64.0, "custom_patterns": {},
        "climax": {"at_sec": 58.5, "outcome": "ruined"}, "fake_climaxes": [12.25, 31.0], "media": [],
        "segments": [
            {"kind": "beat", "pattern": "Quick Swing", "freq": 2.3, "duration_sec": 20.0},
            {"kind": "pause", "pattern": None, "freq": None, "duration_sec": 6.0},
            {"kind": "finale", "pattern": "Build Up", "freq": 4.6, "duration_sec": 38.0},
        ],
    }
    unfinished = dict(replay, climax=None, duration_sec=40.0)
    runs = {
        "a_real_climax": (quick, soon, [["start", 1000.0], ["probe", 1050.0, 2.5], ["advance", 1200.0],
                                        ["probe", 1200.0, 2.5]]),
        "held_back_to_the_ramp": ({"min_ramp_duration": 150.0, "max_ramp_duration": 150.0}, soon,
                                  [["start", 1000.0], ["probe", 1001.0, 2.5], ["advance", 1260.0]]),
        "ruined": (quick, dict(soon, ruined_orgasm_active=True, ruined_orgasm_chance=0.9),
                   [["start", 1000.0], ["advance", 1200.0]]),
        "denied_stops_the_beat": (quick, dict(soon, denied_orgasm_active=True, denied_orgasm_chance=1.0,
                                              ruined_orgasm_active=True, ruined_orgasm_chance=0.6),
                                  [["start", 1000.0], ["advance", 1200.0], ["probe", 1200.0, 2.5]]),
        "fakes_everywhere": ({"min_beat_dur": 8.0, "max_beat_dur": 12.0, "pause_chance": 0.0},
                             dict(soon, fake_climax_chance=1.0), [["start", 1000.0], ["advance", 1250.0]]),
        "edges_push_it_back": (quick, soon, [["start", 1000.0], ["edge", 1030.0], ["edge", 1047.5],
                                             ["probe", 1048.0, 2.5], ["advance", 1065.0], ["edge", 1065.0],
                                             ["advance", 1300.0]]),
        "settings_changed_on_the_way": (quick, soon, [
            ["start", 1000.0], ["advance", 1020.0], ["climax_set", 1020.0, "denied_orgasm_active", True],
            ["climax_set", 1021.0, "climax_only_after_ramp", False], ["probe", 1021.0, 2.5],
            ["climax_set", 1030.0, "climax_active", False], ["advance", 1060.0],
            ["climax_set", 1060.0, "climax_active", True], ["advance", 1400.0]]),
        "switched_off": (quick, dict(soon, climax_active=False), [["start", 1000.0], ["advance", 1150.0]]),
        "a_replay": ({}, dict(soon, fake_climax_chance=1.0),
                     [["start", 1000.0, replay], ["probe", 1001.0, 2.5], ["edge", 1015.0], ["advance", 1150.0]]),
        "a_replay_that_never_climaxed": (quick, soon, [["start", 1000.0, unfinished], ["advance", 1300.0]]),
        "two_sessions_in_a_row": (quick, soon, [["start", 1000.0], ["advance", 1200.0], ["stop", 1200.0],
                                                ["start", 1210.0], ["advance", 1400.0]]),
    }
    out = []
    for seed, (name, (config, climax_config, steps)) in enumerate(runs.items(), start=100):
        draws, log = _play(config, steps, seed, climax_config)
        out.append({"name": name, "config": config, "climax_config": climax_config, "steps": steps,
                    "draws": draws, "expected": log})
    return out


# --- applog ------------------------------------------------------------------------------------

# Python formats with %s, C# with {0}: each line carries both spellings of the same message.
_LOG_LINES = [
    ["info", "BeatHandler", "Beat paused", "Beat paused", []],
    ["warning", "CalloutHandler", "Language %r is not available", "Language {0} is not available", ["'fr'"]],
    ["error", "user_data", "Could not save %s: %s", "Could not save {0}: {1}", ["history", "disk full"]],
    ["info", "GoonerApp", "Playlist loaded: %d files", "Playlist loaded: {0} files", [42]],
    ["warning", "Test", "Umlaute bleiben: %s", "Umlaute bleiben: {0}", ["Süß"]],
]


def applog_cases():
    from src import applog

    out = []
    for level in ["INFO", "WARNING", "ERROR", "DEBUG", "loud"]:
        with tempfile.TemporaryDirectory() as tmp:
            applog.configure(enabled=True, log_dir=tmp, level=level)
            for name, category, py_message, _cs_message, args in _LOG_LINES:
                logger = applog.get_logger(f"src.{category}")
                getattr(logger, name)(py_message, *[a.strip("'") if py_message.endswith("%r is not available") else a
                                                    for a in args])
            applog.configure(enabled=False, log_dir=None)
            path = applog.log_file_path(tmp)
            text = path.read_text(encoding="utf-8") if path.exists() else ""
        # The timestamp is the wall clock - everything after it is the format.
        out.append({"name": level, "lines": _LOG_LINES, "expected": [line[20:] for line in text.splitlines()]})
    return out


# --- SessionRecorder ---------------------------------------------------------------------------


def _record(steps):
    """Plays [call, *args] steps into a recorder. A "segment" step carries the Segment's fields;
    "timeline" is a probe, read with the moment given (or none). The last timeline read is also
    turned into a saved session, so the whole way from recording to file is compared."""
    recorder = SessionRecorder()
    timelines = []
    for call, *args in steps:
        if call == "timeline":
            timelines.append(recorder.timeline(now=args[0]))
        elif call == "segment":
            kind, duration, freq, pattern, index, at = args
            recorder.segment_started(Segment(kind, duration, freq, pattern, index), at=at)
        else:
            *values, at = args
            getattr(recorder, call)(*values, at=at)
    saved = session_files.to_saved_session(timelines[-1], {"My Rhythm": [1, 0, 1]})
    saved.pop("app_version")
    saved.pop("saved_at")
    return {"timelines": timelines, "saved": saved}


def recorder_cases():
    played = [
        ["media_shown", "first.png", 1000.0],
        ["segment", "beat", 20.0, 2.0, "Standard Beat", 0, 1000.5],
        ["media_shown", "a.png", 1005.0], ["media_shown", "b.png", 1012.0],
        ["segment", "pause", 10.0, None, None, 1, 1020.4],
        ["media_shown", "c.png", 1025.0], ["fake_climax_recorded", 1027.0],
        ["segment", "beat", 15.0, 1.7, "My Rhythm", 2, 1030.4],
        ["media_shown", "d.png", 1030.4],
        ["segment", "finale", 30.0, 4.5, "Quick Swing", 3, 1045.9],
        ["climax_recorded", "ruined", 1070.0], ["media_shown", "e.png", 1071.0],
    ]
    cases = {
        "never_started": [["timeline", 5.0]],
        "started_and_nothing_else": [["session_started", 1000.0], ["timeline", 1010.0]],
        "a_whole_session": [["session_started", 1000.0], *played, ["session_ended", 1090.0], ["timeline", None]],
        "read_while_running": [["session_started", 1000.0], *played, ["timeline", 1080.0], ["timeline", 1085.0]],
        "a_medium_across_three_segments": [
            ["session_started", 0.0], ["segment", "beat", 5.0, 2.0, "Standard Beat", 0, 0.0],
            ["media_shown", "long.png", 1.0], ["segment", "pause", 5.0, None, None, 1, 5.0],
            ["segment", "beat", 5.0, 3.0, "Quick Swing", 2, 10.0], ["session_ended", 20.0], ["timeline", None]],
        "a_medium_shown_on_a_boundary": [
            ["session_started", 0.0], ["segment", "beat", 5.0, 2.0, "Standard Beat", 0, 0.0],
            ["media_shown", "edge.png", 5.0], ["segment", "beat", 5.0, 2.0, "Standard Beat", 1, 5.0],
            ["session_ended", 10.0], ["timeline", None]],
        "two_media_at_the_same_moment": [
            ["session_started", 0.0], ["segment", "beat", 5.0, 2.0, "Standard Beat", 0, 0.0],
            ["media_shown", "x.png", 2.0], ["media_shown", "y.png", 2.0],
            ["session_ended", 6.0], ["timeline", None]],
        "two_segments_at_the_same_moment": [
            ["session_started", 0.0], ["media_shown", "x.png", 0.0],
            ["segment", "beat", 5.0, 2.0, "Standard Beat", 0, 1.0], ["segment", "pause", 5.0, None, None, 1, 1.0],
            ["session_ended", 6.0], ["timeline", None]],
        "a_medium_after_the_end": [
            ["session_started", 0.0], ["segment", "beat", 5.0, 2.0, "Standard Beat", 0, 0.0],
            ["session_ended", 6.0], ["media_shown", "late.png", 8.0], ["timeline", None]],
        "a_second_session": [
            ["session_started", 1000.0], *played, ["session_ended", 1090.0],
            ["session_started", 2000.0], ["segment", "beat", 20.0, 2.0, "Standard Beat", 4, 2000.0],
            ["media_shown", "new.png", 2001.0], ["session_ended", 2030.0], ["timeline", None]],
        "a_climax_without_an_outcome": [
            ["session_started", 0.0], ["segment", "finale", 5.0, 4.5, "Quick Swing", 0, 0.0],
            ["climax_recorded", None, 3.0], ["session_ended", 6.0], ["timeline", None]],
    }
    return [{"name": name, "steps": steps, "expected": _record(steps)} for name, steps in cases.items()]


# --- achievements ------------------------------------------------------------------------------


def catalogue_cases():
    return [{"id": a.id, "name": a.name, "description": a.description, "icon": a.icon,
             "secret": a.secret, "track": a.track, "level": a.level, "has_progress": a.progress is not None}
            for a in achievements.CATALOGUE]


def _judged(played, history, stored):
    """What a tracker with `stored` on disk makes of one session: newly unlocked, in order, and
    the progress every rule reports."""
    with tempfile.TemporaryDirectory() as tmp:
        store = UserDataStore(base_dir=Path(tmp))
        if stored is not None:
            store.save("achievements", stored)
        tracker = AchievementTracker(data_store=store)
        newly = [a.id for a in tracker.evaluate(played, history)]
        progress = {a.id: (lambda p: list(p) if p else None)(tracker.progress_for(a, played, history))
                    for a in achievements.CATALOGUE}
        return {"newly": newly, "progress": progress, "unlocked": sorted(tracker.unlocked)}


def evaluation_cases():
    def entry(**overrides):
        return {"ended_at": "2026-09-17 20:00", "total_dur_sec": 600.0, "total_num_beat": 1200,
                "average_beat_speed_active": 2.0, "fakeout_count": 0, "climax_outcome": None,
                "reported_outcome": None, "fakeouts_fallen_for": 0, "edge_count": 0, **overrides}

    long_quiet = entry(total_dur_sec=45 * 60)
    obeyed = [entry(climax_outcome="denied", reported_outcome="stopped") for _ in range(6)] + \
             [entry(climax_outcome="ruined", reported_outcome="ruined") for _ in range(4)]
    answers = [("real", "came"), ("denied", "came"), ("ruined", "came"), ("denied", None),
               (None, "stopped"), (5, "stopped"), ("denied", "ruined")]
    mixed = [entry(climax_outcome=demanded, reported_outcome=reported) for demanded, reported in answers]
    cases = {
        "nothing_at_all": ({}, [], None),
        "an_ordinary_session": (entry(), [entry()], None),
        "a_long_quiet_session": (long_quiet, [long_quiet], None),
        "edges_unrecorded": (entry(total_dur_sec=50 * 60, edge_count=None), [entry()], None),
        "a_long_session_with_edges": (entry(total_dur_sec=200 * 60, edge_count=12), [entry()], None),
        "obedience_adds_up": (entry(), obeyed, None),
        "every_kind_of_answer": (entry(), mixed, None),
        "a_hundred_sessions": (entry(total_num_beat=21_000), [entry(total_num_beat=21_000) for _ in range(100)], None),
        "fooled_once": (entry(fakeout_count=5, fakeouts_fallen_for=1), [entry()], None),
        "not_fooled": (entry(fakeout_count=4, fakeouts_fallen_for=0), [entry()], None),
        "fractional_counts": (entry(fakeout_count=2.5, edge_count=9.99), [entry(total_num_beat=0.5)], None),
        "booleans_where_numbers_go": (entry(total_num_beat=True, fakeout_count=True, fakeouts_fallen_for=False,
                                            was_replay=True), [entry(total_num_beat=True)], None),
        "text_where_numbers_go": (entry(total_dur_sec="long", fakeout_count="", edge_count="0"),
                                  [entry(total_num_beat="many")], None),
        "a_replay": (entry(was_replay=True), [entry()], None),
        "a_replay_flag_of_zero": (entry(was_replay=0), [entry()], None),
        "already_unlocked": (long_quiet, [long_quiet], {"endurance_45": "2026-01-01 00:00", "unknown_id": 3}),
        "an_unlock_file_that_is_a_list": (long_quiet, [long_quiet], ["endurance_45"]),
    }
    return [{"name": name, "played": played, "history": history, "stored": stored,
             "expected": _judged(copy.deepcopy(played), copy.deepcopy(history), copy.deepcopy(stored))}
            for name, (played, history, stored) in cases.items()]


# --- changelog ---------------------------------------------------------------------------------


def changelog_entries():
    """Every entry, word for word and in order: the port carries a copy until src/ is retired."""
    return [{"name": version, "html": text} for version, text in changelog.CHANGELOG.items()]


# --- guide -------------------------------------------------------------------------------------


def guide_topics():
    """The Guide's tabs, word for word; the engine's own differences are edits on top of them."""
    from src.HelpDialog import HELP_TOPICS

    return [{"name": title, "html": body} for title, body in HELP_TOPICS]


# --- writing -----------------------------------------------------------------------------------


def _modules() -> dict:
    return {
        "session_files": {
            "validate": validation_cases(),
            "to_saved_session": saving_cases(),
            "describe": describe_cases(),
            "read_session_file": reading_cases(),
            **clock_cases(),
        },
        "session_script": {"drain": script_cases()},
        "score_tracker": {"run": tracker_cases(), "format_metric_value": metric_format_cases()},
        "session_recorder": {"record": recorder_cases()},
        "settings": {"read": settings_cases(), "keys": settings_keys()},
        "registry": {"values": registry_cases(), "dead_keys": list(LEGACY_DEAD_KEYS)},
        "applog": {"files": applog_cases()},
        "beat_handler": {"sessions": beat_sessions()},
        "climax_handler": {"sessions": climax_sessions()},
        "callouts": {
            "structure": callout_structure(),
            "draws": callout_draws(),
            "custom_files": callout_custom_files(),
        },
        "achievements": {
            "catalogue": catalogue_cases(),
            "grouped": [[a.id for a in tile] for tile in achievements.grouped()],
            "evaluate": evaluation_cases(),
        },
        "changelog": {"entries": changelog_entries()},
        "guide": {"topics": guide_topics()},
    }


def write(out_dir: Path) -> None:
    """Every case is computed before anything is written, and the old golden files go first: a
    generator that fails half way must never leave yesterday's answers behind for the C# side
    to pass against."""
    modules = _modules()
    out_dir.mkdir(parents=True, exist_ok=True)
    for stale in out_dir.glob("*.json"):
        stale.unlink()
    stamp = time.strftime("%Y-%m-%d %H:%M:%S")
    for module, golden in modules.items():
        golden = {"generated_at": stamp, **golden}
        target = out_dir / f"{module}.json"
        target.write_text(json.dumps(golden, ensure_ascii=False, indent=2), encoding="utf-8")
        counts = {key: len(value) for key, value in golden.items() if isinstance(value, list)}
        print(f"{target}: {counts}")


if __name__ == "__main__":
    write(Path(sys.argv[1]) if len(sys.argv) > 1 else OUT_DIR)
