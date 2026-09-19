"""What the app makes of your sessions, beyond counting them.

Deliberately separate from ScoreTracker for the same reason SessionRecorder is: that class
aggregates numbers, this one judges them. Keeping them apart also means an achievement can
be added, renamed or retuned without touching the code that writes session_history.json.

An achievement is a predicate over two things: the stats of the session that just ended
(ScoreTracker.deliver_infos()) and the whole history behind it, that session included. Both
are plain dicts, so the catalogue below reads as a list of rules rather than as code.

Entries written by older builds simply lack the newer keys - every reader here defaults
rather than indexing, so a history from before outcomes were reported still counts towards
everything it legitimately can.
"""
from collections.abc import Callable
from pathlib import Path
from typing import NamedTuple

from src.applog import get_logger
from src.utils import get_project_root

log = get_logger(__name__)

ICON_DIR = Path("res") / "icons" / "achievements"

# Demanded -> what obeying it looks like when the user reports back. "real" is deliberately
# absent: being told to come and coming is not obedience, it is doing what you were going to
# do anyway, and letting it count would make the whole track free.
OBEDIENT_ANSWER = {"denied": "stopped", "ruined": "ruined"}


class Achievement(NamedTuple):
    id: str
    name: str
    description: str
    icon: str  # res/icons/achievements/<icon>.svg
    check: Callable  # (played, history) -> bool
    # (played, history) -> (current, target), for the ones worth working towards. None where
    # a number would be meaningless ("came anyway") rather than merely absent.
    progress: Callable | None = None
    # Shown as ??? until it fires. For the ones that are more fun found than aimed at.
    secret: bool = False
    # Three levels of the same thing are one achievement with three stages, not three
    # achievements that look alike - `track` is the tile's name and `level` the step within
    # it. None/0 for a standalone one.
    track: str | None = None
    level: int = 0


def icon_path(achievement: Achievement) -> Path:
    return get_project_root() / ICON_DIR / f"{achievement.icon}.svg"


# --- reading the history ---


def _obedient(entry) -> bool:
    demanded = entry.get("climax_outcome")
    reported = entry.get("reported_outcome")
    return reported is not None and OBEDIENT_ANSWER.get(demanded) == reported


def _disobedient(entry) -> bool:
    """Told to stop, or to ruin it, and came anyway."""
    return entry.get("climax_outcome") in OBEDIENT_ANSWER and entry.get("reported_outcome") == "came"


def _count(history, predicate) -> int:
    return sum(1 for entry in history if predicate(entry))


def _total(history, field) -> int:
    return sum(entry.get(field) or 0 for entry in history)


# --- rule builders, so the catalogue stays readable ---


def _session_at_least(field, target):
    def check(played, _history):
        return (played.get(field) or 0) >= target

    def progress(played, _history):
        return min(played.get(field) or 0, target), target

    return check, progress


def _lifetime_at_least(field, target):
    def check(_played, history):
        return _total(history, field) >= target

    def progress(_played, history):
        return min(_total(history, field), target), target

    return check, progress


def _sessions_at_least(target):
    def check(_played, history):
        return len(history) >= target

    def progress(_played, history):
        return min(len(history), target), target

    return check, progress


def _unfooled_by(target):
    """Survived `target` fake cues in one session without acting on a single one.

    Counting the cues alone was the old rule, and it handed "Not Falling For It" to people
    who fell for every one of them. A session the user was fooled in shows no progress
    either - a bar filling to the top on a session that earned nothing is a lie.
    """
    def check(played, _history):
        return (played.get("fakeout_count") or 0) >= target and not played.get(
            "fakeouts_fallen_for"
        )

    def progress(played, _history):
        if played.get("fakeouts_fallen_for"):
            return 0, target
        return min(played.get("fakeout_count") or 0, target), target

    return check, progress


def _history_count_at_least(predicate, target):
    def check(_played, history):
        return _count(history, predicate) >= target

    def progress(_played, history):
        return min(_count(history, predicate), target), target

    return check, progress


def _tiers(prefix, track, name_for, description_for, icon, builder, targets, id_for=None,
           secret=False):
    """The levels of one track, in order. They share a name, a mark and a rule.

    id_for keeps the stored id readable where the raw target is not: a 45 minute tier is
    "endurance_45", not "endurance_2k" seconds. Ids end up in the user's data file, so
    they have to stay stable and legible.
    """
    id_for = id_for or _suffix
    entries = []
    for level, target in enumerate(targets, start=1):
        check, progress = builder(target)
        entries.append(
            Achievement(
                id=f"{prefix}_{id_for(target)}",
                name=name_for(target),
                description=description_for(target),
                icon=icon,
                check=check,
                progress=progress,
                track=track,
                level=level,
                secret=secret,
            )
        )
    return entries


def grouped(catalogue=None) -> list:
    """The catalogue as tiles: a whole track is one entry, everything else is its own.

    Order is preserved, and nothing is dropped - the tiles are just how the same list is
    drawn.
    """
    tiles, current = [], []
    for achievement in (CATALOGUE if catalogue is None else catalogue):
        if achievement.track and current and current[-1].track == achievement.track:
            current.append(achievement)
            continue
        if current:
            tiles.append(tuple(current))
        current = [achievement]
    if current:
        tiles.append(tuple(current))
    return tiles


def _rule(pair) -> dict:
    """Spreads a (check, progress) pair into Achievement's keyword arguments."""
    check, progress = pair
    return {"check": check, "progress": progress}


def _suffix(target) -> str:
    if target >= 1_000_000:
        return f"{target // 1_000_000}m"
    if target >= 1000:
        return f"{target // 1000}k"
    return str(int(target))


def _minutes(seconds) -> int:
    return int(seconds // 60)


# --- the catalogue ---

CATALOGUE = (
    *_tiers(
        "endurance",
        "Endurance",
        lambda t: f"{_minutes(t)} minutes",
        lambda t: f"Last {_minutes(t)} minutes in a single session.",
        "candle",
        lambda t: _session_at_least("total_dur_sec", t),
        (45 * 60, 90 * 60, 120 * 60),
        id_for=lambda t: str(_minutes(t)),
    ),
    *_tiers(
        "beats",
        "Strokes In One Session",
        lambda t: f"{t:,}".replace(",", " "),
        lambda t: f"Take {t:,} beats in a single session.".replace(",", " "),
        "note_run",
        lambda t: _session_at_least("total_num_beat", t),
        (5_000, 12_000, 20_000),
    ),
    *_tiers(
        "lifetime_beats",
        "Strokes All Told",
        lambda t: _suffix(t).upper(),
        lambda t: f"Take {t:,} beats across every session you have ever played.".replace(",", " "),
        "endless_loop",
        lambda t: _lifetime_at_least("total_num_beat", t),
        (100_000, 500_000, 2_000_000),
    ),
    *_tiers(
        "returner",
        "Back For More",
        lambda t: f"{t} sessions",
        lambda t: f"Come back and play {t} sessions.",
        "hooked",
        _sessions_at_least,
        (5, 25, 100),
    ),
    *_tiers(
        "obedient",
        "Good Boy",
        lambda t: f"{t} times" if t > 1 else "once",
        lambda t: (
            f"Do as you are told {t} times when you are denied or told to ruin it. "
            "Being told to come and coming does not count."
            if t > 1 else
            "Do as you are told when you are denied or told to ruin it. Being told to come "
            "and coming does not count."
        ),
        "collar",
        lambda t: _history_count_at_least(_obedient, t),
        (1, 10, 50),
    ),
    *_tiers(
        "disobedient",
        "Couldn't Help It",
        lambda t: f"{t} times" if t > 1 else "once",
        lambda t: f"Come anyway after being denied, {t} times." if t > 1 else
        "Come anyway after being denied.",
        "snapped_leash",
        lambda t: _history_count_at_least(_disobedient, t),
        (1, 10, 50),
        # Never advertised. Listing "come anyway after being denied" as a goal is an
        # invitation rather than a record of one - it is found, not aimed at.
        secret=True,
    ),
    *_tiers(
        "fakeouts",
        "Not Falling For It",
        lambda t: f"{t} in one session" if t > 1 else "one",
        lambda t: (
            f"Let {t} fake climax cues pass in a single session without acting on one of them."
            if t > 1 else
            "Let a fake climax cue pass without acting on it."
        ),
        "silent_bell",
        _unfooled_by,
        (1, 3, 5),
        # Secret for a different reason than the disobedience track: this one's condition
        # gives the mechanic away. Listed as a goal it tells the user that fake cues exist
        # and that the winning move is to hesitate at every climax announcement - and a
        # fake-out only works while it is indistinguishable from the real thing.
        secret=True,
    ),
    Achievement(
        id="edges_10",
        name="Ten Times Close",
        description="Reach your edge 10 times in one session and say so every time.",
        icon="gauge_max",
        **_rule(_session_at_least("edge_count", 10)),
    ),
    Achievement(
        id="edges_none",
        name="Never Asked",
        description="Last 45 minutes without once reaching for the edge button.",
        icon="hourglass",
        check=lambda played, _history: (
            (played.get("total_dur_sec") or 0) >= 45 * 60 and not played.get("edge_count")
        ),
    ),
    Achievement(
        id="replayed_a_session",
        name="Again, From The Top",
        description="",
        icon="endless_loop",
        secret=True,
        check=lambda played, _history: bool(played.get("was_replay")),
    ),
)


class AchievementTracker:
    """Holds what has been unlocked, and decides what the session just played adds to it.

    data_store is optional for the same reason ScoreTracker's is: a tracker without one
    simply keeps nothing, rather than refusing to work.
    """

    STORE_KEY = "achievements"

    def __init__(self, data_store=None, catalogue=CATALOGUE):
        self.data_store = data_store
        self.catalogue = catalogue
        self.unlocked = self._load()

    # --- reading ---

    def is_unlocked(self, achievement_id: str) -> bool:
        return achievement_id in self.unlocked

    def unlocked_at(self, achievement_id: str):
        return self.unlocked.get(achievement_id)

    def progress_for(self, achievement: Achievement, played: dict, history: list):
        """(current, target) for a locked achievement, or None when it has no meaningful
        number to show."""
        if achievement.progress is None:
            return None
        try:
            return achievement.progress(played, history)
        except Exception:
            log.exception("Could not read progress for achievement %s", achievement.id)
            return None

    # --- judging ---

    def evaluate(self, played: dict, history: list) -> list:
        """Everything the session just played has newly earned, in catalogue order.

        Called after the session is already in the history, so a rule can count it.
        """
        newly = []
        for achievement in self.catalogue:
            if self.is_unlocked(achievement.id):
                continue
            try:
                earned = achievement.check(played, history)
            except Exception:
                # One bad predicate must not cost the user every other unlock in the same
                # session - and an achievement is never worth taking the app down for.
                log.exception("Achievement %s could not be evaluated", achievement.id)
                continue
            if earned:
                self.unlocked[achievement.id] = _now()
                newly.append(achievement)
        if newly:
            log.info("Achievements unlocked: %s", ", ".join(item.id for item in newly))
            self._save()
        return newly

    def clear(self):
        self.unlocked = {}
        if self.data_store:
            self.data_store.delete(self.STORE_KEY)

    # --- storage ---

    def _load(self) -> dict:
        if not self.data_store:
            return {}
        stored = self.data_store.load(self.STORE_KEY, {})
        if not isinstance(stored, dict):
            log.warning("The achievements file did not contain a mapping - ignoring it.")
            return {}
        return stored

    def _save(self):
        if self.data_store:
            self.data_store.save(self.STORE_KEY, self.unlocked)


def _now() -> str:
    import time

    return time.strftime("%Y-%m-%d %H:%M", time.localtime())
