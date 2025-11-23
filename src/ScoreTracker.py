import time


class ScoreTracker:

    def __init__(self):
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

    def session_started(self):
        print("Session Started")
        self.session_start_time = time.time()
        self.number_of_pauses = 0
        self.total_duration_of_pauses = 0
        self.cur_pause_start_time = None
        self.total_run_time = None
        self.average_pause_duration = None

    def session_ended(self):
        print("Session Ended")
        self.total_run_time = time.time() - self.session_start_time
        if self.number_of_pauses > 0:
            self.average_pause_duration = self.total_duration_of_pauses / self.number_of_pauses
        else:
            self.average_pause_duration = None
        self.average_beat_speed = self.beat_count / self.total_run_time
        self.average_beat_speed_active = self.beat_count / (self.total_run_time - self.total_duration_of_pauses)

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
            'most_used_pattern': self._find_fav_pattern()
        }

    def beat_changed(self, _, new_pattern):
        self.number_of_beat_changes += 1
        self.patterns.setdefault(new_pattern, 0)
        self.patterns[new_pattern] += 1


    def beat(self):
        self.beat_count += 1

    def _find_fav_pattern(self):
        max_count = 0
        fav_patttern = None
        for pattern, count in self.patterns.items():
            if count > max_count:
                max_count = count
                fav_patttern = pattern

        return fav_patttern