import json
import time

from src import utils


class ScoreTracker:

    # Metrics tracked as personal records across sessions, and their display labels -
    # single source of truth for StatisticsDialog's "New Record!" cards and
    # LongTermStatisticsDialog's all-time-bests summary.
    PR_METRICS = ("total_dur_sec", "total_num_beat", "average_beat_speed_active", "fakeout_count")
    PR_METRIC_LABELS = {
        "total_dur_sec": "Total Duration",
        "total_num_beat": "Total Beats",
        "average_beat_speed_active": "Active Beat Speed",
        "fakeout_count": "Fakeouts Survived",
    }
    PR_METRIC_UNITS = {
        "total_dur_sec": "s",
        "total_num_beat": "beats",
        "average_beat_speed_active": "beats/sec",
        "fakeout_count": "fakeouts",
    }
    # Bounds the size of the JSON blob persisted in QSettings (the Windows registry has no
    # practical need to grow this without limit).
    MAX_HISTORY_ENTRIES = 200
    # Live record-chase only reveals itself once a metric is at least this close (current/best)
    # to its personal best - an anticipation/payoff moment, not a permanent stats HUD.
    CHASE_REVEAL_THRESHOLD = 0.8
    # average_beat_speed_active is excluded from the live chase (unlike PR_METRICS as a whole,
    # which still tracks it for the end-of-session recap): early in a session its small
    # active_time denominator makes instantaneous speed spike well above the eventual session
    # average, so a live ratio for it is not a meaningful "closing in" signal.
    LIVE_CHASE_METRICS = tuple(m for m in PR_METRICS if m != "average_beat_speed_active")

    @staticmethod
    def format_metric_value(metric: str, value) -> str:
        if metric == "total_dur_sec":
            return utils.format_duration(value)
        if metric == "average_beat_speed_active":
            return f"{value:.2f} beats/sec"
        return f"{value}"

    def __init__(self, settings=None):
        self.settings = settings
        self.number_of_pauses = 0
        self.total_duration_of_pauses = 0
        self.beat_count = 0
        self.number_of_beat_changes = 0
        self.cur_pause_start_time = None
        self.session_start_time = None
        self.total_run_time = None
        self.average_pause_duration = None
        self.average_beat_speed = None
        self.average_beat_speed_active = None
        self.patterns = {}
        self.skips = 0
        self.repeats = 0
        self.climax_outcome = None
        self.fakeout_count = 0
        self.history = self._load_history()
        self.last_session_new_records = {}

    def climax_decided(self, outcome):
        self.climax_outcome = outcome

    def beat_paused(self):
        print("Beat Paused")
        self.number_of_pauses += 1
        if not self.cur_pause_start_time:
            self.cur_pause_start_time = time.time()
        else:
            print("ERROR! Pause Start Time not Resetted!")

    def beat_resumed(self):
        print("Resume!")
        self.total_duration_of_pauses += time.time() - self.cur_pause_start_time
        self.cur_pause_start_time = None

    def media_skipped(self):
        self.skips += 1

    def media_repeated(self):
        self.repeats += 1

    def session_started(self):
        print("Session Started")
        self.session_start_time = time.time()
        self.number_of_pauses = 0
        self.total_duration_of_pauses = 0
        self.cur_pause_start_time = None
        self.total_run_time = None
        self.average_pause_duration = None
        self.skips = 0
        self.repeats = 0
        self.beat_count = 0
        self.number_of_beat_changes = 0
        self.patterns = {}
        self.climax_outcome = None
        self.fakeout_count = 0

    def session_ended(self):
        print("Session Ended")
        self.total_run_time = time.time() - self.session_start_time
        if self.number_of_pauses > 0:
            self.average_pause_duration = self.total_duration_of_pauses / self.number_of_pauses
        else:
            self.average_pause_duration = None

        if self.total_run_time > 0:
            self.average_beat_speed = self.beat_count / self.total_run_time
        else:
            self.average_beat_speed = 0

        active_time = self.total_run_time - self.total_duration_of_pauses
        if active_time > 0:
            self.average_beat_speed_active = self.beat_count / active_time
        else:
            self.average_beat_speed_active = 0

        self._record_history_and_detect_prs()

    def deliver_infos(self) -> dict:
        return {
            'total_dur_sec': self.total_run_time,
            'pause_dur_sec': self.total_duration_of_pauses,
            'average_pause_dur_sec': self.average_pause_duration,
            'total_num_pauses': self.number_of_pauses,
            'total_num_beat': self.beat_count,
            'total_num_beat_change': self.number_of_beat_changes,
            'average_beat_speed': self.average_beat_speed,
            'average_beat_speed_active': self.average_beat_speed_active,
            'most_used_pattern': self._find_fav_pattern(),
            'skips': self.skips,
            'repeats': self.repeats,
            'climax_outcome': self.climax_outcome,
            'fakeout_count': self.fakeout_count,
        }

    def beat_changed(self, _, new_pattern):
        self.number_of_beat_changes += 1
        self.patterns.setdefault(new_pattern, 0)
        self.patterns[new_pattern] += 1

    def beat(self):
        self.beat_count += 1

    def fake_climax_triggered(self):
        self.fakeout_count += 1

    def _find_fav_pattern(self):
        max_count = 0
        fav_patttern = None
        for pattern, count in self.patterns.items():
            if count > max_count:
                max_count = count
                fav_patttern = pattern

        return fav_patttern

    def live_metrics(self) -> dict:
        if self.session_start_time is None:
            return {}
        elapsed = time.time() - self.session_start_time
        active_time = elapsed - self.total_duration_of_pauses
        return {
            "total_dur_sec": elapsed,
            "total_num_beat": self.beat_count,
            "average_beat_speed_active": (self.beat_count / active_time) if active_time > 0 else 0,
            "fakeout_count": self.fakeout_count,
        }

    def record_chase_status(self, best_values: dict):
        live = self.live_metrics()
        if not live:
            return None
        best_match = None
        for metric in self.LIVE_CHASE_METRICS:
            best = best_values.get(metric)
            if not best or best <= 0:
                continue
            current = live[metric]
            progress = current / best
            if best_match is None or progress > best_match[3]:
                best_match = (metric, current, best, progress)
        if best_match is None or best_match[3] < self.CHASE_REVEAL_THRESHOLD:
            return None
        metric, current, best, _ = best_match
        return metric, current, best

    def get_history(self) -> list:
        return list(self.history)

    def get_all_time_bests(self) -> dict:
        return self._compute_bests(self.history)

    def _compute_bests(self, history: list) -> dict:
        bests = {}
        for entry in history:
            for metric in self.PR_METRICS:
                value = entry.get(metric)
                if value is None:
                    continue
                if metric not in bests or value > bests[metric]:
                    bests[metric] = value
        return bests

    def _record_history_and_detect_prs(self):
        info = self.deliver_infos()
        current = {metric: info[metric] for metric in self.PR_METRICS}
        previous_bests = self._compute_bests(self.history)

        self.last_session_new_records = {
            metric: previous_bests[metric]
            for metric in self.PR_METRICS
            if metric in previous_bests and current[metric] > previous_bests[metric]
        }

        entry = {"ended_at": time.strftime("%Y-%m-%d %H:%M", time.localtime())}
        entry.update(current)
        self.history.append(entry)
        self.history = self.history[-self.MAX_HISTORY_ENTRIES:]
        self._save_history()

    def _load_history(self) -> list:
        if not self.settings:
            return []
        raw_history = self.settings.value("ScoreTracker/session_history")
        return json.loads(raw_history) if raw_history else []

    def _save_history(self):
        if not self.settings:
            return
        self.settings.setValue("ScoreTracker/session_history", json.dumps(self.history))
