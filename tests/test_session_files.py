"""Turning a recorded session into something that survives on disk, and back.

Qt-free: this is serialisation, and the recorder's output is plain dicts.
"""
import json

import pytest

from src import session_files


def timeline(**overrides):
    """A recorded session as SessionRecorder.timeline() hands it over - wall-clock times."""
    base = {
        "started_at": 5000.0,
        "ended_at": 5200.0,
        "climax_at": 5180.0,
        "climax_outcome": "ruined",
        "fake_climaxes": [5040.0, 5120.0],
        "segments": [
            {"kind": "beat", "pattern": "Quick Swing", "freq": 2.3,
             "start": 5000.0, "end": 5060.0,
             "media": [{"path": "C:\\pics\\a.png", "start": 5000.0, "end": 5030.0, "carried_over": False},
                       {"path": "C:\\pics\\b.png", "start": 5030.0, "end": 5090.0, "carried_over": False}]},
            {"kind": "pause", "pattern": None, "freq": None,
             "start": 5060.0, "end": 5075.0,
             "media": [{"path": "C:\\pics\\b.png", "start": 5030.0, "end": 5090.0, "carried_over": True}]},
            {"kind": "finale", "pattern": "Build Up", "freq": 4.5,
             "start": 5075.0, "end": 5200.0, "media": []},
        ],
    }
    base.update(overrides)
    return base


# --- saving ---


def test_times_are_stored_as_offsets_not_wall_clock():
    """A replay starts whenever it starts - an absolute timestamp from last Tuesday is
    meaningless to it."""
    saved = session_files.to_saved_session(timeline())

    assert saved["segments"][0]["duration_sec"] == pytest.approx(60.0)
    assert saved["climax"]["at_sec"] == pytest.approx(180.0)
    assert saved["fake_climaxes"] == pytest.approx([40.0, 120.0])
    assert saved["media"][0]["at_sec"] == pytest.approx(0.0)


def test_every_segment_keeps_its_rhythm_speed_and_type():
    saved = session_files.to_saved_session(timeline())

    first, pause, finale = saved["segments"]

    assert (first["kind"], first["pattern"], first["freq"]) == ("beat", "Quick Swing", 2.3)
    assert (pause["kind"], pause["pattern"], pause["freq"]) == ("pause", None, None)
    assert finale["kind"] == "finale"


def test_media_are_listed_once_in_order():
    """Carried-over entries are the same medium listed again under the next segment -
    replaying it twice would show it twice."""
    saved = session_files.to_saved_session(timeline())

    assert [m["path"] for m in saved["media"]] == ["C:\\pics\\a.png", "C:\\pics\\b.png"]


def test_the_session_duration_is_recorded():
    assert session_files.to_saved_session(timeline())["duration_sec"] == pytest.approx(200.0)


def test_a_session_without_a_climax_saves_cleanly():
    saved = session_files.to_saved_session(timeline(climax_at=None, climax_outcome=None))
    assert saved["climax"] is None


def test_custom_patterns_used_by_the_session_are_carried_along():
    """Without the definition, a replay on another machine dies the moment it reaches one."""
    saved = session_files.to_saved_session(
        timeline(), custom_patterns={"My Rhythm": [1, 2, -1], "Unused": [1]},
    )
    # Only what the session actually used.
    assert saved["custom_patterns"] == {}

    used = timeline()
    used["segments"][0]["pattern"] = "My Rhythm"
    saved = session_files.to_saved_session(used, custom_patterns={"My Rhythm": [1, 2, -1], "Unused": [1]})

    assert saved["custom_patterns"] == {"My Rhythm": [1, 2, -1]}


def test_the_format_version_and_app_version_are_stamped():
    saved = session_files.to_saved_session(timeline())
    assert saved["format"] == session_files.FORMAT_VERSION
    assert saved["app_version"]
    assert saved["saved_at"]


# --- stripping paths ---


def test_stripping_removes_every_path_but_keeps_the_timing():
    """Exporting with paths carries the sender's account name, folder layout and file
    names - stripping has to leave none of it."""
    saved = session_files.strip_paths(session_files.to_saved_session(timeline()))

    assert all("path" not in entry for entry in saved["media"])
    assert [entry["at_sec"] for entry in saved["media"]] == pytest.approx([0.0, 30.0])
    assert "C:\\" not in json.dumps(saved)


def test_stripping_leaves_the_original_untouched():
    saved = session_files.to_saved_session(timeline())
    session_files.strip_paths(saved)
    assert "path" in saved["media"][0]


def test_stripping_an_already_stripped_session_is_harmless():
    once = session_files.strip_paths(session_files.to_saved_session(timeline()))
    assert session_files.strip_paths(once) == once


# --- loading ---


def test_a_saved_session_parses_back(tmp_path):
    path = tmp_path / "s.json"
    path.write_text(json.dumps(session_files.to_saved_session(timeline())), encoding="utf-8")

    loaded = session_files.read_session_file(path)

    assert [s["pattern"] for s in loaded["segments"]] == ["Quick Swing", None, "Build Up"]
    assert loaded["climax"]["outcome"] == "ruined"


def test_a_future_format_is_refused_rather_than_half_read(tmp_path):
    path = tmp_path / "s.json"
    saved = session_files.to_saved_session(timeline())
    saved["format"] = session_files.FORMAT_VERSION + 1
    path.write_text(json.dumps(saved), encoding="utf-8")

    with pytest.raises(session_files.UnsupportedSessionFile):
        session_files.read_session_file(path)


def test_a_file_that_is_not_a_session_is_refused(tmp_path):
    path = tmp_path / "s.json"
    path.write_text(json.dumps({"hello": "world"}), encoding="utf-8")

    with pytest.raises(session_files.UnsupportedSessionFile):
        session_files.read_session_file(path)


def test_a_corrupt_file_is_refused_rather_than_raising_json_errors(tmp_path):
    path = tmp_path / "s.json"
    path.write_text("{not json at all", encoding="utf-8")

    with pytest.raises(session_files.UnsupportedSessionFile):
        session_files.read_session_file(path)


def test_a_missing_file_is_refused_the_same_way(tmp_path):
    with pytest.raises(session_files.UnsupportedSessionFile):
        session_files.read_session_file(tmp_path / "gone.json")


# --- labels for the manager ---


def test_a_session_gets_a_readable_label():
    saved = session_files.to_saved_session(timeline())
    label = session_files.describe(saved)

    assert "3:20" in label  # 200 seconds
    assert "Ruined" in label


def test_a_session_without_paths_says_so_in_its_label():
    saved = session_files.strip_paths(session_files.to_saved_session(timeline()))
    assert "no media" in session_files.describe(saved).lower()


# --- the shelf of saved sessions ---


def test_a_saved_session_lands_in_the_data_store(data_store):
    session_files.store_session(data_store, session_files.to_saved_session(timeline()))

    stored = session_files.load_saved_sessions(data_store)

    assert len(stored) == 1
    assert stored[0]["climax"]["outcome"] == "ruined"


def test_saved_sessions_keep_their_order(data_store):
    for outcome in ("real", "ruined", "denied"):
        saved = session_files.to_saved_session(timeline(climax_outcome=outcome))
        session_files.store_session(data_store, saved)

    stored = session_files.load_saved_sessions(data_store)

    assert [entry["climax"]["outcome"] for entry in stored] == ["real", "ruined", "denied"]


def test_the_shelf_is_capped_so_it_cannot_grow_forever(data_store, monkeypatch):
    """A saved session is orders of magnitude bigger than a history entry - a few hundred
    media paths each - so the cap is much lower than ScoreTracker's."""
    monkeypatch.setattr(session_files, "MAX_SAVED_SESSIONS", 3)
    for index in range(5):
        saved = session_files.to_saved_session(timeline())
        saved["saved_at"] = f"entry {index}"
        session_files.store_session(data_store, saved)

    stored = session_files.load_saved_sessions(data_store)

    assert [entry["saved_at"] for entry in stored] == ["entry 2", "entry 3", "entry 4"]


def test_a_saved_session_can_be_deleted_again(data_store):
    for index in range(3):
        saved = session_files.to_saved_session(timeline())
        saved["saved_at"] = f"entry {index}"
        session_files.store_session(data_store, saved)

    session_files.delete_saved_session(data_store, 1)

    stored = session_files.load_saved_sessions(data_store)
    assert [entry["saved_at"] for entry in stored] == ["entry 0", "entry 2"]


def test_deleting_something_that_is_not_there_is_harmless(data_store):
    session_files.store_session(data_store, session_files.to_saved_session(timeline()))

    assert session_files.delete_saved_session(data_store, 7) is False
    assert len(session_files.load_saved_sessions(data_store)) == 1


def test_a_data_file_that_is_not_a_list_reads_as_an_empty_shelf(data_store):
    data_store.save(session_files.SAVED_SESSIONS_KEY, {"not": "a list"})

    assert session_files.load_saved_sessions(data_store) == []


def test_an_exported_session_reads_back_from_the_file_it_was_written_to(tmp_path):
    saved = session_files.to_saved_session(timeline())

    assert session_files.write_session_file(tmp_path / "out.gooner", saved) is True

    assert session_files.read_session_file(tmp_path / "out.gooner")["climax"]["outcome"] == "ruined"


def test_an_export_that_cannot_be_written_reports_failure_rather_than_raising(tmp_path):
    saved = session_files.to_saved_session(timeline())

    assert session_files.write_session_file(tmp_path / "no" / "such" / "dir" / "o.json", saved) is False


# --- do the recorded files still exist? ---


def test_the_recorded_paths_come_back_in_the_order_they_were_shown():
    saved = session_files.to_saved_session(timeline())

    assert session_files.recorded_paths(saved) == ["C:\\pics\\a.png", "C:\\pics\\b.png"]


def test_a_stripped_session_has_no_recorded_paths():
    saved = session_files.strip_paths(session_files.to_saved_session(timeline()))

    assert session_files.recorded_paths(saved) == []


def test_missing_files_are_reported_so_the_replay_can_offer_another_library(tmp_path):
    there = tmp_path / "there.png"
    there.write_bytes(b"x")
    saved = {"format": 1, "segments": [], "media": [
        {"at_sec": 0.0, "path": str(there)},
        {"at_sec": 5.0, "path": str(tmp_path / "gone.png")},
    ]}

    assert session_files.missing_paths(saved) == [str(tmp_path / "gone.png")]


def test_a_session_stopped_before_the_climax_is_marked_as_such():
    """Replaying it does something different from replaying a complete one - it carries on
    past the recording - so it has to be tellable apart in the list."""
    saved = session_files.to_saved_session(timeline(climax_at=None, climax_outcome=None))

    assert "stopped early" in session_files.describe(saved).lower()


def test_a_complete_session_is_not_marked_as_stopped_early():
    assert "stopped early" not in session_files.describe(
        session_files.to_saved_session(timeline())
    ).lower()


# --- planned lengths, not measured ones ---


def test_a_saved_segment_takes_the_length_it_was_planned_to_be():
    """A measured length already carries the overshoot to the next note. Feeding that back
    to the planner makes the replay overshoot again, so every segment of a replay came out
    a little longer than the session it was reproducing."""
    recorded = timeline()
    recorded["segments"][0]["planned_sec"] = 58.0  # planned 58, measured 60

    saved = session_files.to_saved_session(recorded)

    assert saved["segments"][0]["duration_sec"] == pytest.approx(58.0)


def test_the_last_segment_keeps_the_length_it_actually_ran():
    """It is the one segment whose plan was not what happened: the climax holds it open
    until the session ends, or the user stopped partway through it."""
    recorded = timeline()
    for data in recorded["segments"]:
        data["planned_sec"] = 1.0

    saved = session_files.to_saved_session(recorded)

    assert saved["segments"][-1]["duration_sec"] == pytest.approx(125.0)  # 5075 -> 5200


def test_a_segment_without_a_planned_length_falls_back_to_what_it_measured():
    saved = session_files.to_saved_session(timeline())

    assert saved["segments"][0]["duration_sec"] == pytest.approx(60.0)


# --- refusing a file the app cannot actually play ---


def handwritten(**overrides):
    """A session as an LLM would write one - no media paths, minimal but complete."""
    base = {
        "format": session_files.FORMAT_VERSION,
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
    base.update(overrides)
    return base


def test_a_well_formed_handwritten_session_is_accepted():
    assert session_files.validate_session(handwritten()) == []


def test_a_session_with_no_media_at_all_is_fine():
    """It replays against the reader's own library - an LLM should never have to invent a
    path, and refusing this would force it to."""
    assert session_files.validate_session(handwritten(media=[])) == []


def test_an_unknown_rhythm_is_refused_by_name():
    """The mistake an LLM will actually make."""
    saved = handwritten()
    saved["segments"][0]["pattern"] = "Furious Wiggle"

    problems = session_files.validate_session(saved)

    assert any("Furious Wiggle" in problem for problem in problems)


def test_a_rhythm_the_file_defines_itself_is_accepted():
    saved = handwritten(custom_patterns={"Furious Wiggle": [1, 2, -1]})
    saved["segments"][0]["pattern"] = "Furious Wiggle"

    assert session_files.validate_session(saved) == []


def test_a_pattern_definition_has_to_be_playable():
    saved = handwritten(custom_patterns={"Broken": [0, 9, "x"]})
    saved["segments"][0]["pattern"] = "Broken"

    assert session_files.validate_session(saved)


def test_an_unknown_segment_kind_is_refused():
    saved = handwritten()
    saved["segments"][0]["kind"] = "crescendo"

    assert any("crescendo" in problem for problem in session_files.validate_session(saved))


def test_a_beat_without_a_speed_is_refused():
    saved = handwritten()
    saved["segments"][0]["freq"] = None

    assert session_files.validate_session(saved)


def test_a_segment_without_a_length_is_refused():
    saved = handwritten()
    del saved["segments"][0]["duration_sec"]

    assert session_files.validate_session(saved)


def test_a_zero_length_segment_is_refused():
    saved = handwritten()
    saved["segments"][0]["duration_sec"] = 0

    assert session_files.validate_session(saved)


def test_a_climax_past_the_end_of_the_session_is_refused():
    """It would simply never fire, and the session would run on with nothing at the end."""
    saved = handwritten()
    saved["climax"]["at_sec"] = 999.0

    assert session_files.validate_session(saved)


def test_an_invented_climax_outcome_is_refused():
    saved = handwritten()
    saved["climax"]["outcome"] = "explosive"

    assert any("explosive" in problem for problem in session_files.validate_session(saved))


def test_a_fake_out_past_the_end_is_refused():
    assert session_files.validate_session(handwritten(fake_climaxes=[999.0]))


def test_media_moments_have_to_run_forwards():
    assert session_files.validate_session(
        handwritten(media=[{"at_sec": 30.0}, {"at_sec": 10.0}])
    )


def test_a_session_with_no_segments_is_refused():
    assert session_files.validate_session(handwritten(segments=[]))


def test_reading_a_broken_file_reports_what_is_actually_wrong(tmp_path):
    """One flat "cannot be replayed" is useless to somebody iterating on a generated file."""
    saved = handwritten()
    saved["segments"][0]["pattern"] = "Furious Wiggle"
    path = tmp_path / "bad.json"
    path.write_text(json.dumps(saved), encoding="utf-8")

    with pytest.raises(session_files.UnsupportedSessionFile) as raised:
        session_files.read_session_file(path)

    assert "Furious Wiggle" in str(raised.value)


def test_a_session_the_app_recorded_itself_always_validates():
    """The serialiser and the validator must not drift apart."""
    assert session_files.validate_session(session_files.to_saved_session(timeline())) == []
