"""Reading and writing saved sessions.

A saved session is what SessionRecorder recorded, turned into something a later run can
replay: the segments in order, when the climax landed and how it went, the fake-outs, the
media and when each came up.

Every time in the file is **seconds from the start of the session**, never a wall clock. A
replay starts whenever it starts, and an absolute timestamp from last Tuesday would mean
nothing to it.

Note what this writes: media file paths. That is the one thing this app otherwise keeps off
disk entirely - it is not even allowed in the log (see CLAUDE.md). It happens here because
the user asked for it by pressing Save, it is disclosed and deletable under Privacy & Data,
and strip_paths() exists so a session can be handed to someone else without carrying the
sender's account name, folder layout and file names along with it.
"""
import json
import time
from pathlib import Path

from src.applog import get_logger
from src.BeatHandler import BeatHandler
from src.utils import format_clock, get_current_version

log = get_logger(__name__)

# Bumped whenever the shape below changes incompatibly. read_session_file() refuses
# anything newer rather than half-reading it into a session that then behaves oddly.
FORMAT_VERSION = 1

OUTCOME_LABELS = {"real": "Climax", "ruined": "Ruined", "denied": "Denied"}


class UnsupportedSessionFile(Exception):
    """The file is not a session this build can replay - missing, corrupt, or newer."""


def to_saved_session(timeline, custom_patterns=None) -> dict:
    """Converts a SessionRecorder timeline into the saved form."""
    start = timeline.get("started_at") or 0.0
    recorded = timeline["segments"]
    segments = [
        {
            "kind": data["kind"],
            "pattern": data["pattern"],
            "freq": data["freq"],
            "duration_sec": _length_to_replay(data, is_last=index == len(recorded) - 1),
        }
        for index, data in enumerate(recorded)
    ]

    climax = None
    if timeline.get("climax_at") is not None:
        climax = {"at_sec": timeline["climax_at"] - start, "outcome": timeline.get("climax_outcome")}

    return {
        "format": FORMAT_VERSION,
        "app_version": get_current_version(),
        "saved_at": time.strftime("%Y-%m-%d %H:%M", time.localtime()),
        "duration_sec": (timeline.get("ended_at") or start) - start,
        "segments": segments,
        "custom_patterns": _patterns_used(timeline, custom_patterns or {}),
        "climax": climax,
        "fake_climaxes": [at - start for at in timeline.get("fake_climaxes", [])],
        "media": _media_script(timeline, start),
    }


def _length_to_replay(data, is_last: bool) -> float:
    """The planned length, not the measured one - except for the last segment.

    A segment does not stop the instant its time is up; it runs to the first note past it,
    so what it measured is always a little more than what it was planned to be. Handing the
    *measured* figure back to the planner makes the replay overshoot a second time, and
    every segment of a replay came out longer than the one it was reproducing. Handing back
    the planned figure makes the replay overshoot exactly the way the recording did, which
    is the point.

    The last segment is the exception both ways: the climax holds it open until the session
    ends, or the user stopped partway through it. Neither has anything to do with its plan,
    so there what it actually ran is the truth.
    """
    measured = data["end"] - data["start"]
    if is_last:
        return measured
    return data.get("planned_sec", measured)


def _patterns_used(timeline, custom_patterns) -> dict:
    """Only the custom rhythms this session actually played.

    Carrying the definitions matters: a replay on another machine hits the first scripted
    segment naming a pattern it has never heard of and has nothing to play. Carrying *all*
    of them would quietly hand over the user's whole pattern library instead.
    """
    used = {data["pattern"] for data in timeline["segments"] if data["pattern"]}
    return {name: list(steps) for name, steps in custom_patterns.items() if name in used}


def _media_script(timeline, start) -> list:
    """Each medium once, in the order it came up.

    The timeline lists a medium again under every segment it spanned (flagged
    carried_over), which is right for the explorer and wrong here - replaying it would
    show the same picture twice.
    """
    script = []
    for data in timeline["segments"]:
        for entry in data["media"]:
            if entry["carried_over"]:
                continue
            script.append({"at_sec": entry["start"] - start, "path": entry["path"]})
    return script


def strip_paths(saved: dict) -> dict:
    """A copy with every media path removed, keeping the timings.

    What is left replays the session's difficulty and pacing against whatever collection
    the other person has. What is gone is everything that would tell them where yours
    lives.
    """
    stripped = dict(saved)
    stripped["media"] = [{"at_sec": entry["at_sec"]} for entry in saved.get("media", [])]
    return stripped


def has_paths(saved: dict) -> bool:
    return any("path" in entry for entry in saved.get("media", []))


SEGMENT_KINDS = ("beat", "pause", "finale")
CLIMAX_OUTCOMES = ("real", "ruined", "denied")


def validate_session(saved: dict) -> list:
    """Everything wrong with a session, in plain language. Empty means it will play.

    This exists because a session is no longer only ever written by the app: the format is
    documented (docs/SESSION_FORMAT.md) so an LLM can compose one, and a hand-written file
    gets every detail wrong that a serialiser gets right for free. Without this the mistakes
    surface *during* the session - a pattern name nothing can play, a climax that never
    fires - which is the worst possible moment to find out.

    Collects rather than raises on the first problem: somebody iterating on a generated file
    wants the whole list, not one line at a time.
    """
    problems = []
    segments = saved.get("segments")
    if not isinstance(segments, list) or not segments:
        return ["The session has no segments."]

    duration = saved.get("duration_sec")
    if not isinstance(duration, int | float) or duration <= 0:
        problems.append("duration_sec has to be a positive number of seconds.")
        duration = float("inf")

    known = set(BeatHandler.BEAT_PATTERNS_MAP) | set(_validated_patterns(saved, problems))
    for index, segment in enumerate(segments, start=1):
        problems.extend(_segment_problems(segment, index, known))

    problems.extend(_climax_problems(saved.get("climax"), duration))
    problems.extend(_moment_problems(saved.get("fake_climaxes") or [], duration, "fake_climaxes"))
    problems.extend(_media_problems(saved.get("media") or [], duration))
    return problems


def _validated_patterns(saved, problems) -> dict:
    custom = saved.get("custom_patterns") or {}
    if not isinstance(custom, dict):
        problems.append("custom_patterns has to be an object of name -> list of steps.")
        return {}
    for name, steps in custom.items():
        if not isinstance(steps, list) or not steps:
            problems.append(f"The rhythm '{name}' has no steps.")
        elif not all(isinstance(step, int) and step != 0 and abs(step) <= 4 for step in steps):
            # Same rule the pattern editor enforces: 1-4 for a beat, negative for a silent
            # step of the same length, and never 0.
            problems.append(
                f"The rhythm '{name}' has steps outside 1..4 (negative for silence, never 0)."
            )
    return custom


def _segment_problems(segment, index, known_patterns) -> list:
    where = f"Segment {index}"
    if not isinstance(segment, dict):
        return [f"{where} is not an object."]

    problems = []
    kind = segment.get("kind")
    if kind not in SEGMENT_KINDS:
        problems.append(f"{where} has kind '{kind}' - expected one of {', '.join(SEGMENT_KINDS)}.")

    duration = segment.get("duration_sec")
    if not isinstance(duration, int | float) or duration <= 0:
        problems.append(f"{where} needs a positive duration_sec.")

    pattern, freq = segment.get("pattern"), segment.get("freq")
    if kind == "pause":
        if pattern is not None or freq is not None:
            problems.append(f"{where} is a pause, so it carries no pattern and no freq.")
        return problems

    if not isinstance(freq, int | float) or freq <= 0:
        problems.append(f"{where} needs a freq in beats per second.")
    if pattern is None:
        problems.append(f"{where} needs a pattern name.")
    elif pattern not in known_patterns:
        problems.append(
            f"{where} plays '{pattern}', which is neither built in nor defined in "
            "custom_patterns."
        )
    return problems


def _climax_problems(climax, duration) -> list:
    if climax is None:
        return []  # a session that was stopped before one - see describe()
    if not isinstance(climax, dict):
        return ["climax has to be an object with at_sec and outcome."]

    problems = []
    at = climax.get("at_sec")
    if not isinstance(at, int | float) or at < 0:
        problems.append("The climax needs an at_sec in seconds from the start.")
    elif at > duration:
        problems.append(
            f"The climax is at {at:.0f}s but the session is only {duration:.0f}s long, so it "
            "would never arrive."
        )
    if climax.get("outcome") not in CLIMAX_OUTCOMES:
        problems.append(
            f"The climax outcome '{climax.get('outcome')}' is not one of "
            f"{', '.join(CLIMAX_OUTCOMES)}."
        )
    return problems


def _moment_problems(moments, duration, field) -> list:
    if not isinstance(moments, list):
        return [f"{field} has to be a list of seconds."]
    for moment in moments:
        if not isinstance(moment, int | float) or moment < 0 or moment > duration:
            return [f"{field} contains {moment}, which is outside the session."]
    return []


def _media_problems(media, duration) -> list:
    if not isinstance(media, list):
        return ["media has to be a list."]
    previous = -1.0
    for entry in media:
        if not isinstance(entry, dict) or not isinstance(entry.get("at_sec"), int | float):
            return ["Every media entry needs an at_sec in seconds from the start."]
        at = entry["at_sec"]
        if at < previous:
            return ["The media moments have to run forwards - one of them goes backwards."]
        if at > duration:
            return [f"A medium is shown at {at:.0f}s, past the end of the session."]
        previous = at
    return []


def read_session_file(path) -> dict:
    """Loads a saved session, or raises UnsupportedSessionFile.

    One exception for every way this can fail - the caller shows the same "this file cannot
    be replayed" either way, and the specifics belong in the log rather than in a dialog.
    """
    try:
        with open(path, encoding="utf-8") as handle:
            data = json.load(handle)
    except (OSError, json.JSONDecodeError) as error:
        log.warning("Could not read the session file: %s", error)
        raise UnsupportedSessionFile("The file could not be read.") from error

    if not isinstance(data, dict) or "format" not in data or "segments" not in data:
        raise UnsupportedSessionFile("That is not a saved session.")
    if data["format"] > FORMAT_VERSION:
        raise UnsupportedSessionFile(
            "That session was saved by a newer version of GoonerApp."
        )

    problems = validate_session(data)
    if problems:
        log.warning("Refused a session file with %d problem(s)", len(problems))
        raise UnsupportedSessionFile(
            "That session cannot be played:\n\n" + "\n".join(f"- {p}" for p in problems)
        )
    return data


def describe(saved: dict) -> str:
    """One line for the saved-sessions list."""
    parts = [saved.get("saved_at", "?"), format_clock(saved.get("duration_sec") or 0)]
    climax = saved.get("climax")
    if climax:
        parts.append(OUTCOME_LABELS.get(climax.get("outcome"), str(climax.get("outcome"))))
    else:
        # Worth calling out, because it replays differently: the recording plays out and
        # the session then carries on and draws a climax of its own (see ClimaxHandler).
        parts.append("Stopped early")
    fakes = len(saved.get("fake_climaxes", []))
    if fakes:
        parts.append(f"{fakes} fake-out{'s' if fakes > 1 else ''}")
    if not has_paths(saved):
        parts.append("no media paths")
    return "  -  ".join(parts)


def write_session_file(path, saved: dict) -> bool:
    """Exports one session to a file the user picked. Returns whether it landed.

    Never raises: the caller is a file dialog's OK button, and a read-only stick or a
    vanished network drive should get a "could not write that" box, not a traceback over
    the app.
    """
    try:
        with open(path, "w", encoding="utf-8") as handle:
            json.dump(saved, handle, ensure_ascii=False, indent=2)
    except OSError as error:
        log.error("Could not write the session file: %s", error)
        return False
    return True


# --- the shelf of saved sessions ---

SAVED_SESSIONS_KEY = "saved_sessions"
# Far below ScoreTracker.MAX_HISTORY_ENTRIES on purpose: a history entry is a handful of
# numbers, a saved session carries every segment and every medium it showed.
MAX_SAVED_SESSIONS = 50


def load_saved_sessions(data_store) -> list:
    """Every saved session, oldest first."""
    stored = data_store.load(SAVED_SESSIONS_KEY, [])
    if not isinstance(stored, list):
        log.warning("The saved sessions file did not contain a list - ignoring it.")
        return []
    return stored


def store_session(data_store, saved: dict) -> bool:
    """Puts one session on the shelf, dropping the oldest once it is full."""
    sessions = load_saved_sessions(data_store)
    sessions.append(saved)
    return data_store.save(SAVED_SESSIONS_KEY, sessions[-MAX_SAVED_SESSIONS:])


def delete_saved_session(data_store, index: int) -> bool:
    """Removes one session from the shelf. Returns whether there was one at that index."""
    sessions = load_saved_sessions(data_store)
    if not 0 <= index < len(sessions):
        return False
    del sessions[index]
    data_store.save(SAVED_SESSIONS_KEY, sessions)
    return True


def recorded_paths(saved: dict) -> list:
    """The media the session showed, in order. Empty when the paths were stripped."""
    return [entry["path"] for entry in saved.get("media", []) if "path" in entry]


def missing_paths(saved: dict) -> list:
    """Recorded media that is no longer where it was, so the caller can offer to replay
    against another library instead of showing a session of empty frames."""
    return [path for path in recorded_paths(saved) if not Path(path).exists()]
