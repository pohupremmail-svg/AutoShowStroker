import random
import time
from collections import deque
from typing import NamedTuple

from PyQt6.QtCore import QMutex, QObject, Qt, QTimer, QUrl, pyqtSignal
from PyQt6.QtMultimedia import QSoundEffect

from src.applog import get_logger
from src.utils import get_project_root

log = get_logger(__name__)


class Segment(NamedTuple):
    """One stretch of the session, decided before it happens.

    A session is a sequence of these: a rhythm to follow, a pause to endure, or the
    "finale" run-in that covers the moment the climax is announced. Deciding them ahead
    of time is the whole point - it is what lets the app say anything about its own
    future, from the beat track painting past a pattern change to guaranteeing a fast
    rhythm going into the climax.

    freq/pattern_name are None on a pause. index is a monotonic counter that survives
    replanning, so a consumer can pin something to a specific upcoming boundary.
    """

    kind: str  # "beat" | "pause" | "finale"
    duration_sec: float
    freq: float | None
    pattern_name: str | None
    index: int


class BeatHandler(QObject):
    # The QSettings group these settings persist under. Explicit rather than derived
    # from __class__.__name__ (which SettingsDialog used to do): the read sides all
    # hardcode the same literal, so a class rename would silently orphan every saved
    # value with nothing to grep for.
    SETTINGS_GROUP = "BeatHandler"

    # Hard cap on upcoming_beats() output - at the top of the frequency range with the
    # shortest steps a long horizon would otherwise build a pointlessly huge list.
    MAX_LOOKAHEAD_NOTES = 64

    # How many segments are held ready beyond the running one. Deep enough that the
    # finale is always placed while it is still unstarted (and so still editable), and
    # that upcoming_beats() can see past the current segment; shallow enough that a
    # settings change never invalidates much work.
    PLAN_BUFFER_SEGMENTS = 5

    BEAT_PATTERNS_MAP = {
        # --- Standard & Simple ---
        "Standard Beat": [1],

        # --- Grooves & Swings ---
        "Quick Swing": [1, 2, 2, -1, -1],
        "Simple Bounce": [1, 1, -1, 1, 1, -1],
        "Double Tap": [2, 2, -2, 2, 2, -2],
        "Syncopated 4/4": [1, -1, 1, -1, 1, 1, 1],

        # --- Long Pauses & Gaps ---
        "Slow Pulse": [1, 1, -1, -1, -1, -1, -1, -1],
        "Held Breath": [3, -1, -1, -1, -1, -1, 3],
        "Double Tap Pause": [2, 2, -1, -1, -1, -1],

        # --- Complex & Off-Beat ---
        "Delayed Swing": [1, -2, 2, -2, 1, -2, 2, -2],
        "Triple Quick Tap": [1, 4, 4, -2, -2],
        "Missing Third": [1, 1, -3, 1],

        # --- Accelerating & Decelerating ---
        "Build Up": [1, 2, 3, 4, -4, -4],
        "Slow Down": [4, 3, 2, 1, -2],
        "Speed Change": [1, 1, 1, 1, 2, 2, 2, 2],
        "Suspense Build": [2, -4, -3, -2, -1, 3],
    }

    # Single source of truth: __init__ applies these directly, and the SettingsDialog
    # "Reset to defaults" buttons read the same dict.
    DEFAULTS = {
        "max_beat_dur": 45.0,
        "min_beat_dur": 15,
        "max_beat_freq": 5.0,
        "min_beat_freq": 0.5,
        "min_pause_dur": 5,
        "max_pause_dur": 20,
        "pause_chance": 0.05,
        # "I reached my edge": a pause on demand, and a gentler rhythm behind it.
        "edge_relief_active": True,
        "edge_pause_dur": 20,
        # Without a cooldown, holding the key turns the session into a nap.
        "edge_cooldown_sec": 60,
        "ramping_active": True,
        "min_ramp_duration": 600.0,
        "max_ramp_duration": 1800.0,
        "ramp_window_width": 0.4,
        "beat_loudness": 1.0,
    }

    beat_paused_event = pyqtSignal()
    beat_resumed_event = pyqtSignal()
    beat_change_event = pyqtSignal(float, str)
    beat_event = pyqtSignal()
    # (text, kind) - kind is one of "idle"/"up"/"down"/"new_beat"/"pause". BeatHandler owns no
    # widget (see GoonerApp._update_beat_meter) - it only describes what the meter should show,
    # same pattern CalloutHandler/ClimaxHandler already use for their GoonerApp-owned labels.
    beat_meter_update_event = pyqtSignal(str, str)
    # The session plan, announced to whoever wants to shape or read it.
    # session_planned_event carries the session's start time and the SessionScript this
    # session is replaying (None when it is being drawn), and is emitted before the first
    # segment is planned so a listener can still place a finale into it (see set_finale_at).
    # The script travels with the signal because the planner is not the only one reading
    # it: the climax hears about a session only through here, and had no other way to learn
    # that the times were recorded rather than to be drawn.
    # plan_extended_event carries the newly planned segments;
    # segment_started_event the Segment now on the air - the whole thing rather than its
    # index, so a consumer can record what actually played without reaching back in here.
    session_planned_event = pyqtSignal(float, object)
    plan_extended_event = pyqtSignal(list)
    segment_started_event = pyqtSignal(object)
    # Next audible target: (note identity, UP?, monotonic arrival deadline). Emitted when
    # scheduled, before beat_event, so linear devices can interpolate toward the beat.
    linear_movement_planned = pyqtSignal(int, bool, float)

    def __init__(self, beat_file=None, settings=None, data_store=None):
        super().__init__()
        self.data_store = data_store
        self.beat_changed_counter = 5
        self.just_changed_beat = False
        self.beat_meter_pause_timer = QTimer()
        self.beat_meter_pause_timer.timeout.connect(self.pause_loop)
        self.cur_pause_dur = None
        self.is_red = False
        self._linear_note_id = 0

        self.settings = settings

        # Every DEFAULTS entry is the attribute's starting value - the settings block below
        # then overrides whatever the user has saved. Driven from the dict rather than
        # repeated as literals, which is what the "keep in sync" comment used to ask a
        # reader to do by hand.
        for _key, _value in self.DEFAULTS.items():
            setattr(self, _key, _value)

        self.session_start_time = 0.0
        self.ramp_target_duration = 0.0

        # The plan. _plan holds the segments queued behind the running one;
        # _plan_end_time is the wall clock at which the last of them ends, and is
        # re-anchored to reality every time a segment actually starts.
        self._plan = deque()
        self._plan_end_time = 0.0
        self._next_index = 0
        self._finale_at = None
        self._current_segment = None
        self._current_segment_end = 0.0
        self._holding = False
        self._script = None
        # Rhythms a replayed session brought with it. Kept apart from custom_beat_patterns
        # so borrowing someone else's session never quietly adopts their pattern library.
        self._borrowed_patterns = {}
        # Set by edge_relief(), consumed by the next segment that is actually *drawn*.
        self._relief_next_segment = False

        # Whatever the user has saved wins over the defaults above.
        if self.settings:
            self.max_beat_dur = float(self.settings.value("BeatHandler/max_beat_dur", self.max_beat_dur))
            self.min_beat_dur = float(self.settings.value("BeatHandler/min_beat_dur", self.min_beat_dur))
            self.max_beat_freq = float(self.settings.value("BeatHandler/max_beat_freq", self.max_beat_freq))
            self.min_beat_freq = float(self.settings.value("BeatHandler/min_beat_freq", self.min_beat_freq))
            self.min_pause_dur = int(float(self.settings.value("BeatHandler/min_pause_dur", self.min_pause_dur)))
            self.max_pause_dur = int(float(self.settings.value("BeatHandler/max_pause_dur", self.max_pause_dur)))
            self.pause_chance = float(self.settings.value("BeatHandler/pause_chance", self.pause_chance))
            self.edge_relief_active = bool(
                self.settings.value("BeatHandler/edge_relief_active", self.edge_relief_active, type=bool)
            )
            self.edge_pause_dur = int(float(self.settings.value("BeatHandler/edge_pause_dur", self.edge_pause_dur)))
            self.edge_cooldown_sec = int(
                float(self.settings.value("BeatHandler/edge_cooldown_sec", self.edge_cooldown_sec))
            )
            self.ramping_active = bool(
                self.settings.value("BeatHandler/ramping_active", self.ramping_active, type=bool)
            )
            self.min_ramp_duration = float(
                self.settings.value("BeatHandler/min_ramp_duration", self.min_ramp_duration)
            )
            self.max_ramp_duration = float(
                self.settings.value("BeatHandler/max_ramp_duration", self.max_ramp_duration)
            )
            self.ramp_window_width = float(
                self.settings.value("BeatHandler/ramp_window_width", self.ramp_window_width)
            )
            loaded_patterns = self.settings.value("BeatHandler/selected_beat_patterns")
            if loaded_patterns:
                # What comes back is a list of pattern names
                self.selected_beat_patterns = loaded_patterns
            else:
                # Nothing saved yet: every built-in rhythm starts active
                self.selected_beat_patterns = list(self.BEAT_PATTERNS_MAP.keys())

        else:
            self.selected_beat_patterns = list(self.BEAT_PATTERNS_MAP.keys())

        # Pattern *definitions* are user-authored data, so they live in a JSON file rather
        # than the registry (see src/user_data.py). Which patterns are *selected* stays in
        # QSettings above - that's config, not content.
        self.custom_beat_patterns = self._load_custom_patterns()

        self.beat_meter_timer = QTimer()
        # Qt defaults to CoarseTimer (5% tolerance, and on Windows it rides the ~15.6ms
        # system tick). At the top of the frequency range that is ~16ms of jitter on a
        # 200ms interval - audible slop on the one signal whose whole job is to be exact.
        self.beat_meter_timer.setTimerType(Qt.TimerType.PreciseTimer)
        self.beat_meter_timer.timeout.connect(self.beat)
        self.cur_freq = 0
        self.target_beat_dur = 0
        self.cur_beat_start_time = 0

        self.sound_effect = None
        if self.settings:
            # Read here rather than up in the settings block: init_beat_sound() below
            # applies it, so it has to be resolved before the QSoundEffect is built.
            self.beat_loudness = float(
                self.settings.value("BeatHandler/beat_loudness", self.beat_loudness)
            )
        self.is_muted = False

        if beat_file is None:
            # get_project_root(), not a cwd-relative path - see CalloutHandler.callout_dir.
            beat_file = get_project_root() / "res" / "mixkit-cool-interface-click-tone-2568.wav"
        self.init_beat_sound(str(beat_file.absolute()))

        self.current_beat_pattern = None
        self.current_beat_pattern_name = None
        self.current_beat_position = 0
        self._refresh_available_patterns()
        self.beat_pattern_mutex = QMutex()
        self._pattern_audible_count = 1
        self._pattern_inv_sum = 1.0


    # --- the plan ---

    @property
    def planned_segments(self):
        """The segments queued behind the running one, oldest first. Read-only."""
        return tuple(self._plan)

    @property
    def current_segment(self):
        return self._current_segment

    def hold_final_segment(self):
        """Stops planning: the segment on the air now runs until the session ends.

        Called once the climax has actually been announced. Everything downstream of a
        segment change - a new rhythm, a pause, the beat_change callouts - would otherwise
        carry on talking over the moment the whole session was built towards. Nothing
        further is planned either, so no fake climax can be rolled onto a boundary that
        will never arrive.
        """
        self._holding = True
        self._plan.clear()

    def set_finale_at(self, when):
        """Asks the planner to guarantee a fast rhythm across the given moment.

        This is how the climax reaches the planner without BeatHandler having to know
        what a climax is: it is told "be fast here", not "the orgasm is here". Pass None
        to withdraw it. Replans whatever is already queued, so it takes effect even when
        called mid-session.
        """
        self._finale_at = when
        if self._current_segment is not None:
            self.replan_from_next_segment()

    def replan_from_next_segment(self):
        """Rebuilds the queued plan from the current settings.

        The running segment plays out untouched - a settings save should not cut the beat
        the user is currently following out from under them. The finale stays where it
        was drawn: re-rolling it would turn "open Settings and save" into a lever for a
        different climax.
        """
        if self._current_segment is None or self._holding:
            return  # no session running, or nothing further will be planned
        if self._queue_is_recorded():
            # Never rebuild a replay's queue: refilling it *draws* the replacements and the
            # script's cursor cannot rewind, so the rest of the recorded session would be
            # gone. A replay is not supposed to follow changed settings anyway - it is
            # supposed to follow the recording.
            return
        self._plan.clear()
        self._plan_end_time = self._current_segment_end
        self._extend_plan()

    def _extend_plan(self):
        if self._holding:
            return
        added = []
        while len(self._plan) < self.PLAN_BUFFER_SEGMENTS:
            segment = self._plan_one_segment(self._plan_end_time)
            self._plan.append(segment)
            self._plan_end_time += segment.duration_sec
            self._next_index += 1
            added.append(segment)
        if added:
            self.plan_extended_event.emit(added)

    def _last_planned_kind(self):
        if self._plan:
            return self._plan[-1].kind
        if self._current_segment is not None:
            return self._current_segment.kind
        return None

    def _plan_one_segment(self, start):
        """Draws the segment that begins at wall-clock `start`.

        A session never opens on a pause, and two pauses never follow each other. Neither
        was reachable before either: the first thing start_beat() did was pick a beat, and
        pause_loop() always recalculated a fresh one on the way out.
        """
        index = self._next_index
        # Consumed here whatever happens: a replay hands out its recorded segment instead,
        # and a flag left standing would slow down some unrelated segment much later.
        relief, self._relief_next_segment = self._relief_next_segment, False
        if self._script is not None:
            scripted = self._script.next_segment(index)
            if scripted is not None:
                # No finale rule: a replayed session already has its run-in recorded, and
                # reshaping it would make the replay something other than what was saved.
                return scripted
        if self._last_planned_kind() not in (None, "pause") and random.uniform(0, 1) < self.pause_chance:
            # sorted() because randint - unlike the uniform() calls everywhere else - raises on
            # an inverted range. SettingsDialog refuses to save min above max, but a registry
            # written by an older build still can, and that must not kill a running session.
            low, high = sorted((self.min_pause_dur, self.max_pause_dur))
            candidate = Segment("pause", random.randint(low, high), None, None, index)
        else:
            window_min, window_max = self._current_freq_range(at_time=start)
            candidate = Segment(
                "beat",
                random.uniform(self.min_beat_dur, self.max_beat_dur),
                # Straight to the bottom of the window after an edge, rather than a draw
                # across it. A gap on its own is not relief; what comes back has to be
                # gentler than whatever drove the user to ask for it.
                window_min if relief else random.uniform(window_min, window_max),
                random.choice(self._usable_pattern_names()),
                index,
            )
        return self._apply_finale_rule(candidate, start)

    def edge_relief(self):
        """A pause on demand, with a gentler rhythm behind it. Returns the seconds given, or
        0.0 when there is nothing to relieve.

        This is the one place a segment does not play out as planned - and it is the user's
        own doing, which is exactly the difference. Refused while a pause is already running
        (there would be nothing to interrupt, and it would hand out a free second pause) and
        after the climax has been announced, where nothing further is planned at all.

        Deliberately says nothing about the climax: BeatHandler does not know climaxes exist.
        The caller is the one that pushes it back by the returned amount.
        """
        if self._current_segment is None or self._holding or self.is_paused():
            return 0.0

        seconds = float(self.edge_pause_dur)
        if self._finale_at is not None:
            # The pause displaces everything behind it, so "be fast here" travels with it -
            # otherwise the marker would point at a stretch of the session that has just
            # moved out from under it, and the run-in would be rebuilt around nothing. Still
            # no knowledge of climaxes here: the caller shifts its own by the same amount.
            self._finale_at += seconds
        if self._queue_is_recorded():
            # A replay's queue is the recording itself, already taken off the script - and
            # the script cursor does not rewind, so clearing it would silently skip those
            # segments and leave the planner drawing from there. The break goes in front of
            # them instead, and everything after it plays exactly as recorded, just later.
            self._plan.appendleft(Segment("pause", seconds, None, None, self._next_index))
        else:
            self._relief_next_segment = True
            self._plan.clear()
            self._plan.append(Segment("pause", seconds, None, None, self._next_index))
        self._next_index += 1
        # Cuts the running segment short; _begin_next_segment re-anchors the plan clock to
        # the real start, so the rest of the session simply moves along with it.
        self._begin_next_segment()
        return seconds

    def _queue_is_recorded(self) -> bool:
        """Whether what is queued came off a replayed session rather than being drawn.

        Told by index: start_beat() numbers a session's segments from 0 and hands the script
        those numbers, so anything below the script's length was replayed.
        """
        if self._script is None or not self._plan:
            return False
        return self._plan[0].index < self._script.segment_count

    def _covers_finale(self, start, duration):
        """Whether a segment of `duration` starting at `start` is the run-in to the climax.

        True when less than min_beat_dur would be left between its end and the finale
        moment - which covers both the segment that already spans the moment and the one
        that stops just short of it. Side-effect free, so it can be used to *check* a
        planned segment without re-drawing anything.
        """
        if self._finale_at is None or start >= self._finale_at:
            return False
        return self._finale_at - (start + duration) < self.min_beat_dur

    def _revalidate_finale(self):
        """Fixes a queued segment that drift has moved into or out of the finale window.

        Segments end at the first beat tick past their planned end, so the plan runs a
        little late and a segment planned as ordinary can become the run-in (or the other
        way round). Corrected here, while it is still queued and nothing of it is on
        screen, rather than as it starts - see _begin_next_segment.
        """
        if self._finale_at is None:
            return
        if self._queue_is_recorded():
            # A replay's run-in is recorded where it belongs. Rebuilding the queue here
            # would *draw* the replacements, and the script's cursor cannot rewind - the
            # rest of the recording would be lost for the sake of a correction it does not
            # need.
            return
        start = self._current_segment_end
        kept = deque()
        for segment in self._plan:
            if self._covers_finale(start, segment.duration_sec) != (segment.kind == "finale"):
                self._plan = kept
                self._plan_end_time = start
                return  # _extend_plan() refills from here with the rule applied
            kept.append(segment)
            start += segment.duration_sec

    def _apply_finale_rule(self, candidate, start):
        """Turns the segment leading into the finale moment into the finale itself.

        A segment becomes the finale when less than min_beat_dur would be left between its
        end and the finale moment - which covers both the segment that already spans the
        moment and the one that stops just short of it. It is then stretched to run
        min_beat_dur past the moment, so the fast rhythm is still going when the climax is
        announced rather than handing over to a fresh (possibly slow, possibly silent)
        segment at exactly the wrong instant.

        Extending rather than clipping is deliberate: clipping would leave a stub too short
        to be a rhythm at all right where the rhythm matters most.
        """
        if not self._covers_finale(start, candidate.duration_sec):
            return candidate
        _window_min, window_max = self._current_freq_range(at_time=start)
        return Segment(
            "finale",
            max(candidate.duration_sec, self._finale_at + self.min_beat_dur - start),
            window_max,
            random.choice(self._usable_pattern_names()),
            candidate.index,
        )

    def _begin_next_segment(self):
        """Puts the next planned segment on the air and tops the plan back up."""
        if not self._plan:
            self._extend_plan()
        segment = self._plan.popleft()
        now = time.time()
        # Started exactly as planned, deliberately. Re-deriving it here used to re-draw its
        # pattern and frequency, which the beat track had already painted several notes of
        # - so the whole rhythm visibly jumped the instant the segment began. Drift is
        # handled by revalidating what is still *queued* instead, where nothing is on
        # screen yet.
        self._current_segment = segment
        self.cur_beat_start_time = now
        self._current_segment_end = now + segment.duration_sec
        # Re-anchor the plan clock to the real start, or the drift would accumulate across
        # the whole session.
        self._plan_end_time = self._current_segment_end + sum(s.duration_sec for s in self._plan)
        self._revalidate_finale()
        self._extend_plan()

        if segment.kind == "pause":
            self.start_pause()
        else:
            self._apply_beat_segment(segment)
        self.segment_started_event.emit(segment)

    # --- running the beat ---
    def start_beat(self, script=None):
        self._linear_note_id += 1
        """Starts a session. With a `script` (see SessionScript) the planner replays the
        recorded segments instead of drawing its own, until the script runs out."""
        self._script = script
        self._borrowed_patterns = dict(script.custom_patterns) if script else {}
        self._refresh_available_patterns()
        self.session_start_time = time.time()
        self.ramp_target_duration = random.uniform(self.min_ramp_duration, self.max_ramp_duration)
        self._plan.clear()
        self._next_index = 0
        self._finale_at = None
        self._current_segment = None
        self._holding = False
        self._plan_end_time = self.session_start_time
        # Before the plan is built, so a listener still gets to place a finale into it.
        self.session_planned_event.emit(self.session_start_time, script)
        self._extend_plan()
        self._begin_next_segment()

    def reset_beat_timer(self):
        """Schedules the next note, moving on to the next segment when this one is spent."""
        if self._current_segment is None:
            self._begin_next_segment()
            return
        if not self._holding and time.time() >= self._current_segment_end:
            self._begin_next_segment()
            return
        self._schedule_next_note()

    def _schedule_next_note(self):
        self.beat_pattern_mutex.lock()
        try:
            base_step_sec = self._base_step_sec()
            beat_time_ms = int(base_step_sec * 1000 / abs(self.current_beat_pattern[self.current_beat_position]))
            self.current_beat_position = (self.current_beat_position + 1) % len(self.current_beat_pattern)
        finally:
            self.beat_pattern_mutex.unlock()
        self.beat_meter_timer.start(beat_time_ms)
        movement = self.next_linear_movement()
        if movement is not None:
            self.linear_movement_planned.emit(*movement)

    def next_linear_movement(self):
        """Predicts the next audible target within the running segment.

        Silent pattern steps extend the travel time, without adding endpoint flips.
        Stop at a segment-ending rest: the next segment publishes its own target when
        it starts. The current rhythm stays authoritative during live settings changes.
        """
        if self.is_paused() or not self.beat_meter_timer.isActive() or not self.current_beat_pattern:
            return None
        delay = max(0, self.beat_meter_timer.remainingTime()) / 1000
        ends_in = float("inf") if self._holding else self._current_segment_end - time.time()
        position = self.current_beat_position
        base = self._base_step_sec()
        # A validated pattern has at least one audible step; one cycle suffices.
        for _ in self.current_beat_pattern:
            step = self.current_beat_pattern[position]
            if step > 0:
                # The UI suppresses UP/DOWN for five notes during a pattern-change
                # highlight. Count backwards from its next visible direction so device
                # movement continues through that highlight and matches it afterwards.
                up = self.is_red ^ (self.just_changed_beat and self.beat_changed_counter % 2 == 1)
                return self._linear_note_id, up, time.monotonic() + delay
            if delay >= ends_in:
                return None
            delay += base / abs(step)
            position = (position + 1) % len(self.current_beat_pattern)
        return None

    def _base_step_sec(self):
        """Seconds a weight-1 step lasts at the current frequency and pattern.

        Normalizes so cur_freq means real audible beats/sec regardless of pattern shape:
        a pattern with silent steps has to run its steps faster to keep the same audible
        rate. Single source of truth - reset_beat_timer() schedules from it and
        upcoming_beats() predicts from it, so the two can never drift apart.
        """
        if self._pattern_audible_count > 0:
            return self._pattern_audible_count / (self.cur_freq * self._pattern_inv_sum)
        return 1 / self.cur_freq  # defensive fallback, no current pattern lacks a beat

    @staticmethod
    def _base_step_sec_for(pattern, freq):
        """_base_step_sec() for a pattern that is not the running one - what upcoming_beats()
        needs to predict into a segment that has not started yet."""
        audible = sum(1 for v in pattern if v > 0)
        inv_sum = sum(1 / abs(v) for v in pattern)
        if audible > 0 and inv_sum > 0:
            return audible / (freq * inv_sum)
        return 1 / freq

    def upcoming_beats(self, horizon_sec):
        """Predicted steps landing within the next horizon_sec, as (seconds_from_now,
        is_audible, weight) tuples - what the animated beat track paints each frame.

        Read-only: never touches current_beat_position or the timers. Predicts across
        segment boundaries, because the next segments already exist in the plan before
        they start - a pattern change is no longer an unknowable reset. It also predicts
        *through* a running pause, so the notes of the segment waiting behind it fly in
        towards the moment the beat returns instead of appearing on top of it. A pause
        still ends the prediction when it is the segment being predicted into: there is
        nothing to draw across it.

        The exact boundary can still shift by up to one note, since a segment only ends at
        the first tick after its planned end. That self-corrects on the next frame.

        Mirrors _schedule_next_note()'s indexing exactly: the note landing at t takes its
        audibility from pattern[pos], and the gap to the next note from that same index.
        """
        self.beat_pattern_mutex.lock()
        try:
            start = self._lookahead_start()
            if start is None:
                return []
            offset, pattern, position, base_step_sec, segment_ends_in, queued = start
            upcoming = []
            while offset <= horizon_sec and len(upcoming) < self.MAX_LOOKAHEAD_NOTES:
                step = pattern[position]
                upcoming.append((offset, step > 0, abs(step)))
                if offset < segment_ends_in:
                    offset += base_step_sec / abs(step)
                    position = (position + 1) % len(pattern)
                    continue
                # This note is the one that ends the segment: beat() plays it off the
                # pattern still on the air (hence appending it first) and only then moves
                # on, so the gap after it already belongs to the next segment.
                if not queued:
                    break  # predicted past the plan - nothing further is decided yet
                segment = queued.popleft()
                if segment.kind == "pause":
                    break  # nothing to draw through a pause
                next_pattern = self.available_beat_patterns.get(segment.pattern_name)
                if not next_pattern:
                    break  # pattern deleted since it was planned
                pattern = next_pattern
                segment_ends_in = offset + segment.duration_sec
                base_step_sec = self._base_step_sec_for(pattern, segment.freq)
                # _apply_beat_segment starts the new segment at index 0, whose weight sets
                # the gap to its first note; that note is then read at index 1.
                offset += base_step_sec / abs(pattern[0])
                position = 1 % len(pattern)
            return upcoming
        finally:
            self.beat_pattern_mutex.unlock()

    def _pause_remaining(self):
        """Seconds until the beat comes back, or 0 when no pause is running.

        cur_pause_dur counts whole seconds and the timer ticks them down one at a time, so
        the sub-second part has to come from the timer itself.
        """
        if not self.is_paused():
            return 0.0
        whole_seconds_left = max(0, (self.cur_pause_dur or 0) - 1)
        return whole_seconds_left + max(0, self.beat_meter_pause_timer.remainingTime()) / 1000

    def _lookahead_start(self):
        """Where upcoming_beats() begins: (offset, pattern, position, base_step_sec,
        segment_ends_in, queued), or None when there is nothing to predict.

        Two ways in. Mid-beat it carries on from the note already scheduled. Mid-pause
        nothing is in flight, but the segment waiting behind the pause is already planned,
        so the prediction starts from the moment the beat returns - which is what keeps the
        notes from popping into existence halfway down the track when it does.
        """
        queued = deque(self._plan)
        if self.is_paused():
            if not queued:
                return None
            segment = queued.popleft()
            if segment.kind == "pause":
                return None
            pattern = self.available_beat_patterns.get(segment.pattern_name)
            if not pattern or not segment.freq:
                return None
            base_step_sec = self._base_step_sec_for(pattern, segment.freq)
            resumes_in = self._pause_remaining()
            # Same indexing as a segment boundary: _apply_beat_segment starts at index 0,
            # whose weight sets the gap to the first note, which is then read at index 1.
            return (
                resumes_in + base_step_sec / abs(pattern[0]),
                pattern,
                1 % len(pattern),
                base_step_sec,
                resumes_in + segment.duration_sec,
                queued,
            )

        remaining_ms = self.beat_meter_timer.remainingTime()
        if remaining_ms < 0 or not self.beat_meter_timer.isActive():
            return None  # stopped
        if self.cur_freq <= 0 or not self.current_beat_pattern:
            return None
        if self._current_segment is None or self._holding:
            # A held segment runs past its planned end and has nothing queued behind it -
            # treating that end as a boundary would cut the prediction off at the first
            # note and leave the track all but empty for the rest of the session.
            segment_ends_in = float("inf")
        else:
            segment_ends_in = self._current_segment_end - time.time()
        return (
            remaining_ms / 1000,
            self.current_beat_pattern,
            self.current_beat_position,
            self._base_step_sec(),
            segment_ends_in,
            queued,
        )

    @property
    def ramp_complete_at(self):
        """Wall clock at which the difficulty ramp tops out, or None when there is no ramp
        to wait for - either because the session has not started or because ramping is
        switched off. Returning None for the latter is the point: it is what stops the Ramp
        duration sliders from influencing anything while their own checkbox is unticked.
        """
        if not self.ramping_active or self.ramp_target_duration <= 0:
            return None
        return self.session_start_time + self.ramp_target_duration

    def _ramp_progress(self, at_time=None):
        """Ramp progress at `at_time` (default: now).

        The planner passes a segment's *planned start*, which is what turns ramping from a
        corridor sampled whenever the dice happened to fall into a curve the plan walks
        along.
        """
        if self.ramp_target_duration <= 0:
            return None  # ramping not initialized (start_beat() has not run yet)
        elapsed = (time.time() if at_time is None else at_time) - self.session_start_time
        return min(1.0, max(0.0, elapsed / self.ramp_target_duration))

    def _current_freq_range(self, at_time=None):
        corridor = self.max_beat_freq - self.min_beat_freq
        progress = self._ramp_progress(at_time)
        if not self.ramping_active or progress is None or corridor <= 0:
            return self.min_beat_freq, self.max_beat_freq
        width = corridor * self.ramp_window_width
        window_min = self.min_beat_freq + progress * (corridor - width)
        return window_min, window_min + width

    def _refresh_available_patterns(self):
        """What recalc can actually play: the built-ins, the user's own, and anything a
        replayed session brought with it."""
        self.available_beat_patterns = {
            **self.BEAT_PATTERNS_MAP,
            **self.custom_beat_patterns,
            **self._borrowed_patterns,
        }

    def _usable_pattern_names(self):
        """Selected pattern names that actually have a definition - never empty.

        recalc_beat() has to pick *something*: an empty or stale selection used to raise
        IndexError/KeyError out of a Qt slot, which aborts the process. Three ways to get
        there, all reachable without touching the code: unticking every rhythm in Settings,
        deleting the last selected custom pattern in the editor, and a custom_patterns.json
        that no longer defines a name BeatHandler/selected_beat_patterns still lists.

        Read-only on purpose - a name whose definition is temporarily missing stays selected
        and starts working again on its own once the pattern is back.
        """
        selected = self.selected_beat_patterns
        if isinstance(selected, str):
            # QSettings can hand a one-element QStringList back as a bare str.
            selected = [selected]
        elif not isinstance(selected, list):
            selected = list(selected)
        usable = [name for name in selected if name in self.available_beat_patterns]
        return usable or list(self.BEAT_PATTERNS_MAP.keys())

    def _apply_beat_segment(self, segment):
        """Puts a planned beat segment on the air. Rolls nothing - the segment already
        holds every value that used to be drawn here."""
        self.cur_freq = segment.freq
        self.target_beat_dur = segment.duration_sec
        self.beat_pattern_mutex.lock()
        try:
            self.current_beat_position = 0
            self.current_beat_pattern_name = segment.pattern_name
            pattern = self.available_beat_patterns.get(self.current_beat_pattern_name)
            if pattern is None:
                # The pattern was deleted in the editor after this segment was planned.
                # Planning ahead is what makes that possible at all, and a KeyError here
                # would come straight out of a Qt slot and take the process with it.
                self.current_beat_pattern_name = random.choice(self._usable_pattern_names())
                pattern = self.available_beat_patterns[self.current_beat_pattern_name]
            self.current_beat_pattern = pattern
            self._pattern_audible_count = sum(1 for v in self.current_beat_pattern if v > 0)
            self._pattern_inv_sum = sum(1 / abs(v) for v in self.current_beat_pattern)
        finally:
            self.beat_pattern_mutex.unlock()

        # Mark a new beat or speed with a different color for one beat:
        self.beat_meter_update_event.emit(f"New Beat! {self.current_beat_pattern}", "new_beat")
        self.just_changed_beat = True
        self.beat_changed_counter = 5
        self.beat_change_event.emit(self.cur_freq, self.current_beat_pattern_name)
        self._schedule_next_note()

    def init_beat_sound(self, file_path):
        self.sound_effect = QSoundEffect()
        self.sound_effect.setSource(QUrl.fromLocalFile(file_path))
        self.sound_effect.setVolume(self.beat_loudness)

    def play_beat_sound(self):
        if not self.sound_effect:
            return
        self.sound_effect.play()

    def set_muted(self, muted: bool):
        self.is_muted = muted
        if self.sound_effect:
            self.sound_effect.setMuted(muted)

    def toggle_blink(self):
        if self.is_red:
            self.beat_meter_update_event.emit("UP", "up")
        else:
            self.beat_meter_update_event.emit("DOWN", "down")
        self.is_red = not self.is_red

    def beat(self):
        self.beat_pattern_mutex.lock()
        try:
            play_beat = self.current_beat_pattern[self.current_beat_position] > 0
        finally:
            self.beat_pattern_mutex.unlock()

        if play_beat:
            self._linear_note_id += 1
            self.play_beat_sound()
            if not self.just_changed_beat:
                self.toggle_blink()
            else:
                self.beat_changed_counter -=1
                if self.beat_changed_counter == 0:
                    self.just_changed_beat = False
            self.beat_event.emit()
        self.reset_beat_timer()


    def is_paused(self) -> bool:
        """True while a rhythm pause is counting down (the beat timer is stopped)."""
        return self.beat_meter_pause_timer.isActive()

    def start_pause(self):
        self.beat_meter_timer.stop()
        # Length comes from the planned segment - see _plan_one_segment for the guard
        # against an inverted min/max range written by an older build.
        # Rounded, not truncated: a drawn pause is a whole number of seconds, but a
        # *replayed* one carries the length it was measured at, and int(1.9987) turned every
        # replayed pause into a shorter one than the session it was reproducing. The floor
        # of 1 is for the same reason - a pause of 0 ends in pause_loop()'s first tick.
        if self._current_segment is not None and self._current_segment.kind == "pause":
            self.cur_pause_dur = max(1, round(self._current_segment.duration_sec))
        else:
            low, high = sorted((self.min_pause_dur, self.max_pause_dur))
            self.cur_pause_dur = random.randint(low, high)
        self.beat_meter_pause_timer.start(1000)
        self.beat_meter_update_event.emit(f"Pause: {self.cur_pause_dur} seconds left.", "pause")
        self.beat_paused_event.emit()
        return


    def pause_loop(self):
        self.cur_pause_dur -= 1
        if self.cur_pause_dur <= 0:
            self.beat_resumed_event.emit()
            self.beat_meter_pause_timer.stop()
            self._begin_next_segment()
            return
        self.beat_meter_pause_timer.start(1000)
        self.beat_meter_update_event.emit(f"Pause: {self.cur_pause_dur} seconds left.", "pause")

    def stop(self, message=None):
        """Ends the rhythm. `message` is what the meter reads afterwards - the default says
        the app is idle, which is wrong when a session is still running and only the beat is
        over (a denied climax, see ClimaxHandler)."""
        self.beat_meter_timer.stop()
        self.beat_meter_pause_timer.stop()
        self._plan.clear()
        self._current_segment = None
        self._finale_at = None
        self._holding = False
        self.cur_freq = 0
        self.beat_meter_update_event.emit(message or "Strokemeter appears here.", "idle")

    def register_beat_pause_events(self, pause_start_event, pause_resume_event):
        self.beat_paused_event.connect(pause_start_event)
        self.beat_resumed_event.connect(pause_resume_event)

    def register_beat_event(self, handler):
        self.beat_event.connect(handler)

    def register_beat_change_event(self, handler):
        self.beat_change_event.connect(handler)

    def register_beat_meter_update_event(self, handler):
        self.beat_meter_update_event.connect(handler)

    @staticmethod
    def _validate_pattern_steps(steps):
        if not steps:
            raise ValueError("A pattern needs at least one step.")
        if not all(1 <= abs(v) <= 4 for v in steps):
            raise ValueError("Every step's weight must have a magnitude between 1 and 4.")
        if not any(v > 0 for v in steps):
            raise ValueError("A pattern needs at least one audible step.")

    def add_or_update_custom_pattern(self, name, steps):
        name = name.strip() if name else ""
        if not name:
            raise ValueError("Pattern name must not be empty.")
        if name in self.BEAT_PATTERNS_MAP:
            raise ValueError(f"'{name}' collides with a built-in pattern name.")
        self._validate_pattern_steps(steps)

        log.info("Custom pattern %r saved with %d steps", name, len(steps))
        self.custom_beat_patterns[name] = list(steps)
        self._refresh_available_patterns()
        if name not in self.selected_beat_patterns:
            self.selected_beat_patterns.append(name)
        self._save_custom_patterns()

    def delete_custom_pattern(self, name):
        log.info("Custom pattern %r deleted", name)
        self.custom_beat_patterns.pop(name, None)
        self._refresh_available_patterns()
        if name in self.selected_beat_patterns:
            self.selected_beat_patterns.remove(name)
        self._save_custom_patterns()
        # Queued segments may still name it - drop them rather than let the fallback in
        # _apply_beat_segment quietly substitute something else later.
        self.replan_from_next_segment()

    def clear_custom_patterns(self):
        """Forgets every user-authored pattern. The built-ins are untouched - this is a
        data deletion, not a rhythm reset."""
        log.info("Clearing %d custom pattern(s)", len(self.custom_beat_patterns))
        self.custom_beat_patterns = {}
        self._refresh_available_patterns()
        self.selected_beat_patterns = [
            name for name in self._usable_pattern_names() if name in self.BEAT_PATTERNS_MAP
        ]
        if self.data_store:
            self.data_store.delete("custom_patterns")
        if self.settings:
            self.settings.setValue("BeatHandler/selected_beat_patterns", self.selected_beat_patterns)
        self.replan_from_next_segment()

    def _load_custom_patterns(self) -> dict:
        if not self.data_store:
            return {}
        return self._sanitize_custom_patterns(
            self.data_store.load("custom_patterns", {}, self.settings, "BeatHandler/custom_patterns")
        )

    @classmethod
    def _sanitize_custom_patterns(cls, raw) -> dict:
        """Keep only entries add_or_update_custom_pattern would have accepted.

        UserDataStore guarantees the file parses, not that it holds what we expect - and
        src/user_data.py deliberately advertises these files as inspectable and hand-editable.
        Unchecked, a zero step divides by zero, an empty list indexes out of range and a
        non-numeric step raises TypeError, all inside recalc_beat()'s locked region.

        Bad entries are dropped rather than repaired: a pattern we can't read is not a pattern
        we can guess at, and dropping it keeps the rest of the file usable.
        """
        if not isinstance(raw, dict):
            log.warning("Ignoring custom patterns: expected an object, got %s", type(raw).__name__)
            return {}

        clean = {}
        for name, steps in raw.items():
            if not isinstance(name, str) or not isinstance(steps, list):
                log.warning("Skipping custom pattern %r: not a list of steps", name)
                continue
            # bool is an int subclass - exclude it so True/False can't pose as weights.
            if not all(isinstance(v, (int, float)) and not isinstance(v, bool) for v in steps):
                log.warning("Skipping custom pattern %r: every step must be a number", name)
                continue
            try:
                cls._validate_pattern_steps(steps)
            except ValueError as error:
                log.warning("Skipping custom pattern %r: %s", name, error)
                continue
            clean[name] = steps
        return clean

    def _save_custom_patterns(self):
        if self.data_store:
            self.data_store.save("custom_patterns", self.custom_beat_patterns)
        if self.settings:
            self.settings.setValue("BeatHandler/selected_beat_patterns", self.selected_beat_patterns)
