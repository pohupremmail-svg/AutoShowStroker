import random
import time

from PyQt6.QtCore import QObject, QTimer, pyqtSignal


class ClimaxHandler(QObject):
    """Decides when the session peaks, and how, before it gets there.

    Nothing here is rolled at the moment it happens any more. The climax is placed onto a
    wall-clock time the moment the session is planned, and its outcome is resolved in the
    same breath - which is what lets BeatHandler shape a fast run-in towards it (see
    set_finale_at). Fake climaxes are pinned to planned segment boundaries a few segments
    ahead.

    BeatHandler is told "be fast across this moment", never "the orgasm is here": it has
    no idea what a climax is, and this class is the only one that does.
    """

    SETTINGS_GROUP = "ClimaxHandler"  # see BeatHandler.SETTINGS_GROUP


    outcome_decided_event = pyqtSignal(str)  # "real" | "ruined" | "denied"
    status_changed_event = pyqtSignal(str)  # "cum" | "ruined" | "denied" | "neutral" - for UI display
    fake_climax_triggered_event = pyqtSignal()

    # The moment the joke is admitted. Anything that reacted to the fake as if it were
    # real - the outcome buttons above all - has to stand down again here.
    fake_climax_revealed_event = pyqtSignal()
    # Single source of truth: __init__ applies these directly, and the SettingsDialog
    # "Reset to defaults" buttons read the same dict.
    DEFAULTS = {
        "climax_active": True,
        # Seconds into the session. Measured from the start, deliberately not from the end
        # of the difficulty ramp: the ramp is a difficulty curve the user can switch off,
        # and hanging the climax off it meant the Ramp duration sliders silently decided
        # when the orgasm came even with ramping unticked. Independent also means the
        # climax can be set to land while the ramp is still climbing.
        # A range rather than a single value, so the session is not the same length twice.
        "min_climax_after": 720.0,
        "max_climax_after": 2100.0,
        # Hold the climax until the difficulty ramp has topped out, however early the draw
        # above came out. On by default because that is the classic arc - build, peak,
        # finish - and what the app did for its whole life before the two were separated.
        # It only ever pushes the climax later, never earlier, and does nothing at all when
        # ramping is switched off (BeatHandler.ramp_complete_at is then None).
        "climax_only_after_ramp": True,
        "ruined_orgasm_active": False,
        "ruined_orgasm_chance": 0.5,
        "denied_orgasm_active": False,
        "denied_orgasm_chance": 0.5,
        "fake_climax_active": True,
        "fake_climax_chance": 0.05,
        "min_fake_climax_delay": 3.0,
        "max_fake_climax_delay": 8.0,
    }

    def __init__(self, beat_handler, callout_handler, settings=None):
        super().__init__()
        self.beat_handler = beat_handler
        self.callout_handler = callout_handler
        self.settings = settings

        # Every DEFAULTS entry is the attribute's starting value - the settings block
        # below then overrides whatever the user has saved. Driven from the dict rather
        # than repeated as literals, which is what the "keep in sync" comment used to ask
        # a reader to do by hand.
        for _key, _value in self.DEFAULTS.items():
            setattr(self, _key, _value)

        if self.settings:
            self.climax_active = bool(
                self.settings.value("ClimaxHandler/climax_active", self.climax_active, type=bool)
            )
            self.min_climax_after = float(
                self.settings.value("ClimaxHandler/min_climax_after", self.min_climax_after)
            )
            self.max_climax_after = float(
                self.settings.value("ClimaxHandler/max_climax_after", self.max_climax_after)
            )
            self.climax_only_after_ramp = bool(
                self.settings.value(
                    "ClimaxHandler/climax_only_after_ramp", self.climax_only_after_ramp, type=bool
                )
            )
            self.ruined_orgasm_active = bool(
                self.settings.value("ClimaxHandler/ruined_orgasm_active", self.ruined_orgasm_active, type=bool)
            )
            self.ruined_orgasm_chance = float(
                self.settings.value("ClimaxHandler/ruined_orgasm_chance", self.ruined_orgasm_chance)
            )
            self.denied_orgasm_active = bool(
                self.settings.value("ClimaxHandler/denied_orgasm_active", self.denied_orgasm_active, type=bool)
            )
            self.denied_orgasm_chance = float(
                self.settings.value("ClimaxHandler/denied_orgasm_chance", self.denied_orgasm_chance)
            )
            self.fake_climax_active = bool(
                self.settings.value("ClimaxHandler/fake_climax_active", self.fake_climax_active, type=bool)
            )
            self.fake_climax_chance = float(
                self.settings.value("ClimaxHandler/fake_climax_chance", self.fake_climax_chance)
            )
            self.min_fake_climax_delay = float(
                self.settings.value("ClimaxHandler/min_fake_climax_delay", self.min_fake_climax_delay)
            )
            self.max_fake_climax_delay = float(
                self.settings.value("ClimaxHandler/max_fake_climax_delay", self.max_fake_climax_delay)
            )

        self.climax_triggered = False
        self._fake_climax_pending = False
        # When the climax is due, what it will be, and which planned segment boundaries
        # carry a fake one. All three are decided ahead of the moment they matter.
        self.finale_at = None
        self.outcome = None
        self._planned_fakes = set()
        # The moment the draw produced, before climax_only_after_ramp clamps it. Kept so
        # unticking the box mid-session gives that moment back instead of re-rolling.
        self._drawn_finale_at = None
        # Set while replaying a saved session: the climax time, its outcome and the
        # fake-outs all come from the file instead of from the dice.
        self._script = None
        self._scripted_fake_timers = []

        self._fake_climax_timer = QTimer()
        self._fake_climax_timer.setSingleShot(True)
        self._fake_climax_timer.timeout.connect(self._reveal_fake_climax)

        # The climax runs off its own clock rather than off a segment boundary: segments
        # end at the first beat tick past their planned end, so hanging the climax on one
        # would let it drift later and later over a long session.
        self._climax_timer = QTimer()
        self._climax_timer.setSingleShot(True)
        self._climax_timer.timeout.connect(self._on_climax_due)

    @property
    def planned_fake_boundaries(self):
        """Segment indices still carrying an unfired fake climax.

        Deliberately not read by anything that paints: a fake only works while it is
        indistinguishable from the real thing.
        """
        return set(self._planned_fakes)

    def session_started(self):
        self.climax_triggered = False
        self._fake_climax_pending = False
        self.finale_at = None
        self._drawn_finale_at = None
        self.outcome = None
        self._planned_fakes = set()
        self._script = None
        self._cancel_scripted_fakes()
        self._fake_climax_timer.stop()
        self._climax_timer.stop()

    @property
    def scripted_fake_count(self) -> int:
        return len(self._scripted_fake_timers)

    def on_session_planned(self, session_start_time, script=None):
        """Places this session's climax, the moment BeatHandler starts planning.

        With a `script` the whole thing is read off the saved session instead: the recorded
        time, the recorded outcome, and one timer per recorded fake-out.
        """
        self._script = script
        self._cancel_scripted_fakes()
        if script is not None:
            self._replay_session_planned(session_start_time, script)
            return
        self._draw_climax(session_start_time)

    def _draw_climax(self, session_start_time, not_before=None):
        """Places a climax the ordinary way: drawn into the window, outcome resolved now.

        not_before holds it back past the end of a recording that had no climax of its own
        - see _replay_session_planned.
        """
        if not self.climax_active:
            self._disarm()
            return
        low, high = sorted((self.min_climax_after, self.max_climax_after))
        drawn = session_start_time + random.uniform(low, high)
        self._drawn_finale_at = drawn if not_before is None else max(drawn, not_before)
        self.finale_at = self._clamped_finale_at()
        self.outcome = self._resolve_outcome()
        self._arm_climax_timer()
        self.beat_handler.set_finale_at(self.finale_at)

    def _disarm(self):
        """No climax this session - nothing armed, and the planner told not to build a
        run-in for one."""
        self.finale_at = None
        self._drawn_finale_at = None
        self.outcome = None
        self._climax_timer.stop()
        self.beat_handler.set_finale_at(None)

    def _clamped_finale_at(self):
        """The drawn moment, held back to the end of the ramp if the user asked for that.

        Only ever later, never earlier - and never at all when ramping is off, since there
        is no ramp to wait for and the Ramp duration sliders have no business reaching the
        climax while their own checkbox is unticked.
        """
        if not self.climax_only_after_ramp:
            return self._drawn_finale_at
        ramp_complete_at = self.beat_handler.ramp_complete_at
        if ramp_complete_at is None:
            return self._drawn_finale_at
        return max(self._drawn_finale_at, ramp_complete_at)

    def _replay_session_planned(self, session_start_time, script):
        """Everything from the file. No draw, and deliberately no climax_only_after_ramp
        clamp: the time was recorded, not negotiated, and holding it back would replay a
        different session than the one that was saved."""
        for offset in script.fake_offsets:
            timer = QTimer(self)
            timer.setSingleShot(True)
            timer.timeout.connect(self._on_scripted_fake_due)
            timer.start(max(0, int((session_start_time + offset - time.time()) * 1000)))
            self._scripted_fake_timers.append(timer)

        if script.climax_offset is None:
            # The recording was stopped before any climax - almost always because the user
            # did not last that long, which is exactly the session someone saves to try
            # again. So it is not replayed as a session that can never finish: the recorded
            # stretch plays as recorded, and from its end on this is an ordinary session,
            # climax and all. Held past that end rather than drawn freely, or the retry
            # could finish sooner than the run it is retrying - and the run-in cannot be
            # built into segments that come out of the file anyway.
            self._draw_climax(session_start_time, not_before=session_start_time + script.duration)
            return
        self.finale_at = session_start_time + script.climax_offset
        self._drawn_finale_at = self.finale_at
        self.outcome = script.climax_outcome
        self._arm_climax_timer()
        self.beat_handler.set_finale_at(self.finale_at)

    def postpone(self, seconds):
        """Pushes the climax, and a replay's recorded fake-outs, back by `seconds`.

        Called when the user takes an edge break. The climax sits on an absolute clock, so
        without this a pause would not buy them time - it would quietly spend the rhythm
        that was leading up to the climax, and in the worst case leave the run-in with
        nothing to run in over. Thematically it is also the right answer: you edged, so you
        wait longer for it.
        """
        if seconds <= 0 or self.climax_triggered or self.finale_at is None:
            return
        self.finale_at += seconds
        # The pre-clamp moment moves with it, or the next settings save would re-clamp from
        # the old value and snap the climax back to before the break.
        if self._drawn_finale_at is not None:
            self._drawn_finale_at += seconds
        self._arm_climax_timer()
        self.beat_handler.set_finale_at(self.finale_at)
        # Live fake-outs need nothing - they are pinned to segment indices and move with the
        # plan by themselves. A replay's are on absolute timers, like the climax.
        for timer in self._scripted_fake_timers:
            if timer.isActive():
                timer.start(timer.remainingTime() + int(seconds * 1000))

    def _on_scripted_fake_due(self):
        if self.climax_triggered or self._fake_climax_pending:
            return
        self._trigger_fake_climax()

    def _cancel_scripted_fakes(self):
        for timer in self._scripted_fake_timers:
            timer.stop()
        self._scripted_fake_timers = []

    def _arm_climax_timer(self):
        self._climax_timer.start(max(0, int((self.finale_at - time.time()) * 1000)))

    def on_plan_extended(self, segments):
        """Rolls the fake climaxes for boundaries that have just been planned."""
        if not self.fake_climax_active:
            return
        for segment in segments:
            # Never on the finale: a fake at the exact moment the real one is due would
            # put the "only joking" reveal on top of the real announcement.
            if segment.kind == "finale":
                continue
            # A recorded segment's fake-outs are recorded too and already have their own
            # timers. Only what the planner drew *past* the end of the recording is rolled
            # for - that stretch is an ordinary session and should feel like one.
            if self._script is not None and segment.index < self._script.segment_count:
                continue
            if random.uniform(0, 1) < self.fake_climax_chance:
                self._planned_fakes.add(segment.index)

    def on_segment_started(self, segment):
        if segment.index not in self._planned_fakes:
            return
        self._planned_fakes.discard(segment.index)
        if self.climax_triggered or self._fake_climax_pending:
            return
        self._trigger_fake_climax()

    def _on_climax_due(self):
        if self.climax_triggered:
            return
        # A fake still waiting to be revealed would otherwise say "only joking" seconds
        # after the real thing.
        self._fake_climax_timer.stop()
        self._fake_climax_pending = False
        self._trigger_real_climax()

    def settings_changed(self):
        """Re-derives what is still undecided after a mid-session settings save.

        The climax time is deliberately kept: re-drawing it would turn "open Settings and
        save" into a lever for a different climax. The outcome is re-resolved, because
        switching ruined/denied on mid-session has to actually do something.
        """
        if self.climax_triggered:
            return
        if not self.climax_active:
            self.finale_at = None
            self.outcome = None
            self._climax_timer.stop()
            self.beat_handler.set_finale_at(None)
            return
        if self.finale_at is None:
            # Switched on mid-session: place one against the session already running. It
            # can therefore land in the past, which the timer treats as "due now".
            self.on_session_planned(self.beat_handler.session_start_time)
            return
        self.outcome = self._resolve_outcome()
        # The drawn moment stands - re-drawing it would make saving a re-roll lever - but
        # the ramp clamp is re-applied, so ticking or unticking that box takes effect.
        moved_to = self._clamped_finale_at()
        if moved_to != self.finale_at:
            self.finale_at = moved_to
            self._arm_climax_timer()
            self.beat_handler.set_finale_at(self.finale_at)

    def _trigger_fake_climax(self):
        self._fake_climax_pending = True
        self.fake_climax_triggered_event.emit()
        # Reuses the real-climax phrasing on purpose - the fake-out only works if it's
        # indistinguishable from the real thing until the reveal.
        self.callout_handler.force_output_sentence("climax_real")
        self.status_changed_event.emit("cum")  # mirrors a real climax - the fake-out must stay convincing
        delay_ms = int(random.uniform(self.min_fake_climax_delay, self.max_fake_climax_delay) * 1000)
        self._fake_climax_timer.start(delay_ms)

    def _reveal_fake_climax(self):
        self._fake_climax_pending = False
        self.fake_climax_revealed_event.emit()
        self.callout_handler.force_output_sentence("fake_climax_reveal")
        self.status_changed_event.emit("neutral")

    def _trigger_real_climax(self):
        self.climax_triggered = True
        outcome = self.outcome or self._resolve_outcome()
        if outcome == "denied":
            # No rhythm to ride out a denial. Stopping it here is what makes disobedience
            # mean anything: whatever happens next is the user's own doing rather than the
            # app still driving them through it.
            self.beat_handler.stop("Hands off.")
        else:
            # The rhythm she said it over is the one that stays. Without this a new beat, a
            # pause or a beat-change callout would land on top of the climax.
            self.beat_handler.hold_final_segment()
        category = {"real": "climax_real", "ruined": "climax_ruined", "denied": "climax_denied"}[outcome]
        status = {"real": "cum", "ruined": "ruined", "denied": "denied"}[outcome]
        self.callout_handler.force_output_sentence(category)
        self.status_changed_event.emit(status)
        self.outcome_decided_event.emit(outcome)

    def _resolve_outcome(self):
        if not self.ruined_orgasm_active and not self.denied_orgasm_active:
            return "real"
        ruined_chance = self.ruined_orgasm_chance if self.ruined_orgasm_active else 0.0
        denied_chance = self.denied_orgasm_chance if self.denied_orgasm_active else 0.0
        total_extra = ruined_chance + denied_chance
        if total_extra > 1.0:
            # Ruined + denied chances always take priority over "real" if they alone exceed 100%;
            # scale them down proportionally so all three still sum to exactly 1.0.
            ruined_chance /= total_extra
            denied_chance /= total_extra
        real_chance = max(0.0, 1.0 - ruined_chance - denied_chance)
        return random.choices(["real", "ruined", "denied"], weights=[real_chance, ruined_chance, denied_chance], k=1)[
            0
        ]

    def register_outcome_event(self, handler):
        self.outcome_decided_event.connect(handler)

    def register_status_event(self, handler):
        self.status_changed_event.connect(handler)

    def register_fake_climax_event(self, handler):
        self.fake_climax_triggered_event.connect(handler)
