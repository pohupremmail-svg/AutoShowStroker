"""What played when, in order - the data behind the Session Explorer.

Deliberately Qt-free: the recorder is a plain class fed by slots, so the stitching can be
tested by handing it timestamps directly.
"""
import pytest

from src.BeatHandler import Segment
from src.SessionRecorder import SessionRecorder


@pytest.fixture
def recorder():
    r = SessionRecorder()
    r.session_started(at=1000.0)
    return r


def beat(index=0, freq=2.0, pattern="Standard Beat", kind="beat"):
    return Segment(kind, 20.0, freq, pattern, index)


# --- segments ---


def test_a_fresh_recorder_has_an_empty_timeline():
    assert SessionRecorder().timeline()["segments"] == []


def test_a_segment_ends_where_the_next_one_starts(recorder):
    recorder.segment_started(beat(0), at=1000.0)
    recorder.segment_started(beat(1), at=1030.0)
    recorder.session_ended(at=1100.0)

    segments = recorder.timeline()["segments"]

    assert (segments[0]["start"], segments[0]["end"]) == (1000.0, 1030.0)


def test_the_last_segment_ends_when_the_session_does(recorder):
    recorder.segment_started(beat(0), at=1000.0)
    recorder.segment_started(beat(1), at=1030.0)
    recorder.session_ended(at=1100.0)

    assert recorder.timeline()["segments"][-1]["end"] == 1100.0


def test_a_segment_carries_its_rhythm_speed_and_type(recorder):
    recorder.segment_started(beat(0, freq=3.5, pattern="Quick Swing"), at=1000.0)
    recorder.session_ended(at=1030.0)

    segment = recorder.timeline()["segments"][0]

    assert segment["kind"] == "beat"
    assert segment["pattern"] == "Quick Swing"
    assert segment["freq"] == 3.5


def test_a_pause_segment_keeps_its_kind_and_has_no_rhythm(recorder):
    recorder.segment_started(Segment("pause", 5.0, None, None, 0), at=1000.0)
    recorder.session_ended(at=1010.0)

    segment = recorder.timeline()["segments"][0]

    assert segment["kind"] == "pause"
    assert segment["pattern"] is None
    assert segment["freq"] is None


def test_a_session_that_ends_without_a_segment_yields_nothing(recorder):
    recorder.session_ended(at=1100.0)
    assert recorder.timeline()["segments"] == []


# --- media inside the segments ---


def test_media_land_in_the_segment_they_were_shown_in(recorder):
    # b.png is still up when the second segment begins, so it belongs to both - see
    # test_a_medium_spanning_a_boundary_is_listed_in_both_segments.
    recorder.segment_started(beat(0), at=1000.0)
    recorder.media_shown("a.png", at=1005.0)
    recorder.media_shown("b.png", at=1010.0)
    recorder.segment_started(beat(1), at=1030.0)
    recorder.media_shown("c.png", at=1035.0)
    recorder.session_ended(at=1060.0)

    segments = recorder.timeline()["segments"]

    assert [m["path"] for m in segments[0]["media"]] == ["a.png", "b.png"]
    assert [m["path"] for m in segments[1]["media"]] == ["b.png", "c.png"]


def test_a_medium_spanning_a_boundary_is_listed_in_both_segments(recorder):
    """It really was on screen in both - dropping it from the second would hide the very
    picture someone is scrolling back to find."""
    recorder.segment_started(beat(0), at=1000.0)
    recorder.media_shown("long.png", at=1020.0)
    recorder.segment_started(beat(1), at=1030.0)
    recorder.session_ended(at=1060.0)

    segments = recorder.timeline()["segments"]

    assert [m["path"] for m in segments[0]["media"]] == ["long.png"]
    assert [m["path"] for m in segments[1]["media"]] == ["long.png"]


def test_a_medium_carried_into_the_next_segment_is_marked_as_such(recorder):
    recorder.segment_started(beat(0), at=1000.0)
    recorder.media_shown("long.png", at=1020.0)
    recorder.segment_started(beat(1), at=1030.0)
    recorder.session_ended(at=1060.0)

    segments = recorder.timeline()["segments"]

    assert segments[0]["media"][0]["carried_over"] is False
    assert segments[1]["media"][0]["carried_over"] is True


def test_a_medium_ends_where_the_next_one_starts(recorder):
    recorder.segment_started(beat(0), at=1000.0)
    recorder.media_shown("a.png", at=1005.0)
    recorder.media_shown("b.png", at=1012.0)
    recorder.session_ended(at=1030.0)

    media = recorder.timeline()["segments"][0]["media"]

    assert (media[0]["start"], media[0]["end"]) == (1005.0, 1012.0)
    assert media[1]["end"] == 1030.0


def test_media_shown_during_a_pause_land_in_the_pause_segment(recorder):
    """The slideshow keeps running while the beat rests, so the pause is not a gap."""
    recorder.segment_started(Segment("pause", 5.0, None, None, 0), at=1000.0)
    recorder.media_shown("during_pause.png", at=1002.0)
    recorder.session_ended(at=1010.0)

    assert [m["path"] for m in recorder.timeline()["segments"][0]["media"]] == ["during_pause.png"]


def test_media_shown_before_the_first_segment_are_not_lost(recorder):
    """start() loads the first medium before the beat handler plans anything."""
    recorder.media_shown("first.png", at=1000.0)
    recorder.segment_started(beat(0), at=1000.5)
    recorder.session_ended(at=1030.0)

    assert [m["path"] for m in recorder.timeline()["segments"][0]["media"]] == ["first.png"]


# --- the climax ---


def test_the_climax_moment_and_outcome_are_recorded(recorder):
    recorder.segment_started(beat(0), at=1000.0)
    recorder.climax_recorded("ruined", at=1042.0)
    recorder.session_ended(at=1060.0)

    timeline = recorder.timeline()

    assert timeline["climax_at"] == 1042.0
    assert timeline["climax_outcome"] == "ruined"


def test_no_climax_leaves_both_unset(recorder):
    recorder.segment_started(beat(0), at=1000.0)
    recorder.session_ended(at=1060.0)

    timeline = recorder.timeline()

    assert timeline["climax_at"] is None
    assert timeline["climax_outcome"] is None


# --- session boundaries ---


def test_the_timeline_reports_the_session_bounds(recorder):
    recorder.segment_started(beat(0), at=1000.0)
    recorder.session_ended(at=1234.0)

    timeline = recorder.timeline()

    assert timeline["started_at"] == 1000.0
    assert timeline["ended_at"] == 1234.0


def test_a_second_session_does_not_inherit_the_first(recorder):
    recorder.segment_started(beat(0), at=1000.0)
    recorder.media_shown("old.png", at=1005.0)
    recorder.climax_recorded("real", at=1010.0)
    recorder.session_ended(at=1030.0)

    recorder.session_started(at=2000.0)
    recorder.segment_started(beat(0), at=2000.0)
    recorder.session_ended(at=2030.0)

    timeline = recorder.timeline()

    assert timeline["started_at"] == 2000.0
    assert len(timeline["segments"]) == 1
    assert timeline["segments"][0]["media"] == []
    assert timeline["climax_at"] is None


def test_a_timeline_read_mid_session_ends_at_the_moment_it_is_read(recorder):
    """The explorer only opens after Stop, but nothing should blow up if it is asked
    earlier - the last segment is simply still running."""
    recorder.segment_started(beat(0), at=1000.0)

    timeline = recorder.timeline(now=1020.0)

    assert timeline["segments"][0]["end"] == 1020.0


# --- fake climaxes ---


def test_fake_climaxes_are_recorded_in_order(recorder):
    recorder.segment_started(beat(0), at=1000.0)
    recorder.fake_climax_recorded(at=1012.0)
    recorder.fake_climax_recorded(at=1040.0)
    recorder.session_ended(at=1060.0)

    assert recorder.timeline()["fake_climaxes"] == [1012.0, 1040.0]


def test_a_session_without_fake_climaxes_reports_none(recorder):
    recorder.segment_started(beat(0), at=1000.0)
    recorder.session_ended(at=1060.0)
    assert recorder.timeline()["fake_climaxes"] == []


def test_fake_climaxes_do_not_survive_into_the_next_session(recorder):
    recorder.fake_climax_recorded(at=1012.0)
    recorder.session_ended(at=1060.0)

    recorder.session_started(at=2000.0)

    assert recorder.timeline(now=2010.0)["fake_climaxes"] == []


def test_a_segment_carries_the_length_it_was_planned_to_be(recorder):
    """Next to the length it measured. A saved session replays the *planned* one: the
    measured one already includes the overshoot to the next note, and replaying that makes
    the segment overshoot a second time."""
    recorder.segment_started(Segment("beat", 20.0, 2.0, "Standard Beat", 0), at=1000.0)
    recorder.segment_started(Segment("beat", 5.0, 3.0, "Quick Swing", 1), at=1020.4)
    recorder.session_ended(at=1030.0)

    first, second = recorder.timeline()["segments"]

    assert first["planned_sec"] == 20.0
    assert first["end"] - first["start"] == pytest.approx(20.4)  # what it actually ran
    assert second["planned_sec"] == 5.0
