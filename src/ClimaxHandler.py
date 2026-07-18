import random

from PyQt6.QtCore import QObject, QTimer, pyqtSignal


class ClimaxHandler(QObject):

    outcome_decided_event = pyqtSignal(str)  # "real" | "ruined" | "denied"
    status_changed_event = pyqtSignal(str)  # "cum" | "ruined" | "denied" | "neutral" - for UI display

    def __init__(self, beat_handler, callout_handler, settings=None):
        super().__init__()
        self.beat_handler = beat_handler
        self.callout_handler = callout_handler
        self.settings = settings

        self.climax_active = True
        self.climax_chance = 0.15

        self.ruined_orgasm_active = False
        self.ruined_orgasm_chance = 0.5

        self.denied_orgasm_active = False
        self.denied_orgasm_chance = 0.5

        self.fake_climax_active = True
        self.fake_climax_chance = 0.05
        self.min_fake_climax_delay = 3.0
        self.max_fake_climax_delay = 8.0

        if self.settings:
            self.climax_active = bool(
                self.settings.value("ClimaxHandler/climax_active", self.climax_active, type=bool)
            )
            self.climax_chance = float(self.settings.value("ClimaxHandler/climax_chance", self.climax_chance))
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

        self._fake_climax_timer = QTimer()
        self._fake_climax_timer.setSingleShot(True)
        self._fake_climax_timer.timeout.connect(self._reveal_fake_climax)

    def session_started(self):
        self.climax_triggered = False
        self._fake_climax_pending = False
        self._fake_climax_timer.stop()

    def on_beat_change(self, _freq, _pattern_str):
        if self.climax_triggered or self._fake_climax_pending:
            return
        if self.fake_climax_active and random.uniform(0, 1) < self.fake_climax_chance:
            self._trigger_fake_climax()
            return
        if self.climax_active and self.beat_handler.is_ramp_complete():
            if random.uniform(0, 1) < self.climax_chance:
                self._trigger_real_climax()

    def _trigger_fake_climax(self):
        self._fake_climax_pending = True
        # Reuses the real-climax phrasing on purpose - the fake-out only works if it's
        # indistinguishable from the real thing until the reveal.
        self.callout_handler.force_output_sentence("climax_real")
        self.status_changed_event.emit("cum")  # mirrors a real climax - the fake-out must stay convincing
        delay_ms = int(random.uniform(self.min_fake_climax_delay, self.max_fake_climax_delay) * 1000)
        self._fake_climax_timer.start(delay_ms)

    def _reveal_fake_climax(self):
        self._fake_climax_pending = False
        self.callout_handler.force_output_sentence("fake_climax_reveal")
        self.status_changed_event.emit("neutral")

    def _trigger_real_climax(self):
        self.climax_triggered = True
        outcome = self._resolve_outcome()
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
