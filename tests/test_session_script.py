"""The saved session, handed out to the things that would otherwise be drawing dice."""
import pytest

from src.SessionScript import SessionScript


def saved(**overrides):
    base = {
        "format": 1,
        "duration_sec": 200.0,
        "segments": [
            {"kind": "beat", "pattern": "Quick Swing", "freq": 2.3, "duration_sec": 60.0},
            {"kind": "pause", "pattern": None, "freq": None, "duration_sec": 15.0},
            {"kind": "finale", "pattern": "Build Up", "freq": 4.5, "duration_sec": 125.0},
        ],
        "custom_patterns": {},
        "climax": {"at_sec": 180.0, "outcome": "ruined"},
        "fake_climaxes": [40.0, 120.0],
        "media": [{"at_sec": 0.0, "path": "a.png"}, {"at_sec": 30.0, "path": "b.png"}],
    }
    base.update(overrides)
    return base


# --- segments ---


def test_segments_come_back_in_order_as_real_segments():
    script = SessionScript(saved())

    first = script.next_segment(index=0)
    second = script.next_segment(index=1)

    assert (first.kind, first.pattern_name, first.freq, first.duration_sec) == ("beat", "Quick Swing", 2.3, 60.0)
    assert second.kind == "pause"


def test_the_segment_index_is_carried_through():
    """BeatHandler's own monotonic counter owns the index, not the file - a replay may be
    the second session in the same run."""
    script = SessionScript(saved())
    assert script.next_segment(index=7).index == 7


def test_the_script_runs_out_once_every_segment_has_been_handed_over():
    script = SessionScript(saved())
    for i in range(3):
        assert script.next_segment(index=i) is not None
    assert script.next_segment(index=3) is None


def test_a_spent_script_reports_it():
    script = SessionScript(saved())
    assert script.has_segments_left is True
    for i in range(3):
        script.next_segment(index=i)
    assert script.has_segments_left is False


# --- climax and fake-outs ---


def test_the_climax_time_and_outcome_come_from_the_file():
    script = SessionScript(saved())
    assert script.climax_offset == 180.0
    assert script.climax_outcome == "ruined"


def test_a_session_saved_without_a_climax_has_none():
    script = SessionScript(saved(climax=None))
    assert script.climax_offset is None
    assert script.climax_outcome is None


def test_fake_out_offsets_come_from_the_file():
    assert SessionScript(saved()).fake_offsets == [40.0, 120.0]


# --- media ---


def test_media_gaps_are_the_recorded_ones():
    """The pacing is part of what was saved, not just the beats."""
    script = SessionScript(saved())

    assert script.next_media_gap() == pytest.approx(30.0)  # 0 -> 30
    assert script.next_media_gap() == pytest.approx(170.0)  # 30 -> end of session


def test_media_gaps_run_out_with_the_script():
    script = SessionScript(saved())
    script.next_media_gap()
    script.next_media_gap()
    assert script.next_media_gap() is None


def test_the_recorded_paths_are_handed_out_in_order():
    script = SessionScript(saved())
    assert script.next_media_path() == "a.png"
    assert script.next_media_path() == "b.png"


def test_a_script_without_paths_says_so():
    script = SessionScript(saved(media=[{"at_sec": 0.0}, {"at_sec": 30.0}]))
    assert script.has_media_paths is False
    assert script.next_media_path() is None


def test_paths_can_be_ignored_on_purpose():
    """Replaying someone else's difficulty against your own library."""
    script = SessionScript(saved(), ignore_paths=True)

    assert script.has_media_paths is False
    assert script.next_media_path() is None
    assert script.next_media_gap() == pytest.approx(30.0)  # the pacing still applies


# --- custom patterns ---


def test_custom_patterns_from_the_file_are_exposed():
    script = SessionScript(saved(custom_patterns={"Mine": [1, -1]}))
    assert script.custom_patterns == {"Mine": [1, -1]}


def test_a_session_with_no_custom_patterns_exposes_none():
    assert SessionScript(saved()).custom_patterns == {}
