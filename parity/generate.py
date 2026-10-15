"""Golden files from the Python implementation, for the C# port in engine/ to reproduce.

The Python app is the oracle: every case here is run through the real module in src/, and
whatever it answers is written down as the expected answer. engine/Gooner.Parity.Tests reads
the same files and has to arrive at exactly the same result. A behaviour the port changes on
purpose is listed in parity/allowlist.json with the reason - everything else that differs is
drift.

Run from the repo root:  .venv/Scripts/python.exe -m parity.generate [out_dir]
"""
import copy
import json
import sys
import tempfile
import time
from pathlib import Path

from src import session_files, utils
from src.SessionScript import SessionScript

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


# --- writing -----------------------------------------------------------------------------------


def _write_module(out_dir: Path, module: str, golden: dict) -> None:
    golden = {"generated_at": time.strftime("%Y-%m-%d %H:%M:%S"), **golden}
    target = out_dir / f"{module}.json"
    target.write_text(json.dumps(golden, ensure_ascii=False, indent=2), encoding="utf-8")
    counts = {key: len(value) for key, value in golden.items() if isinstance(value, list)}
    print(f"{target}: {counts}")


def write(out_dir: Path) -> None:
    out_dir.mkdir(parents=True, exist_ok=True)
    _write_module(out_dir, "session_files", {
        "validate": validation_cases(),
        "to_saved_session": saving_cases(),
        "describe": describe_cases(),
        "read_session_file": reading_cases(),
        **clock_cases(),
    })
    _write_module(out_dir, "session_script", {"drain": script_cases()})


if __name__ == "__main__":
    write(Path(sys.argv[1]) if len(sys.argv) > 1 else OUT_DIR)
