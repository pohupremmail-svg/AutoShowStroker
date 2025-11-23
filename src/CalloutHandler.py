import json
import random
from pathlib import Path

from PyQt6.QtCore import pyqtSignal, QTimer, QObject


class CalloutHandler(QObject):

    new_tease_event = pyqtSignal(str)
    hide_tease_event = pyqtSignal()

    def __init__(self, settings=None):
        super().__init__()
        self.tease_active_timer = QTimer()
        self.tease_active_timer.timeout.connect(self._tease_timer_handler)
        self.tease_time = 7000
        self.lang = "en"
        self.callout_dir = Path("./res/callouts")

        self.is_teasing = False

        self.available_languages: list[str] = []
        self.callout_data: dict[str, dict] = {}

        self._load_available_languages()
        self.active_callout = False
        self.talking_chance = 0.5
        self.cur_freq = 0

        if settings is not None:
            self.settings = settings
            self.active_callout = bool(self.settings.value('CalloutHandler/active_callout', type=bool))
            self.set_lang(str(self.settings.value("CalloutHandler/selected_lang")))
            self.talking_chance = float(self.settings.value("CalloutHandler/talking_chance", type=float))

    def _load_available_languages(self):
        assert self.callout_dir.is_dir()

        for json_file in self.callout_dir.glob("*.json"):
            lang_code = json_file.stem

            self.available_languages.append(lang_code)

            try:
                with open(json_file, 'r', encoding='utf-8') as f:
                    self.callout_data[lang_code] = json.load(f)
            except Exception as e:
                print(f"Error loading the callout file {json_file}: {e}")

        if self.lang not in self.callout_data or self.lang not in self.available_languages:
            self.set_lang(self.available_languages[0])


    def set_lang(self, lang):
        if lang in self.callout_data and self.lang in self.available_languages:
            self.lang = lang
        else:
            print(f"Tried setting {lang}. That language is not available.")

    def session_started(self):
        self.select_and_output_sentence("session_started")
    def media_skipped(self):
        self.select_and_output_sentence("media_skipped")
    def media_repeated(self):
        self.select_and_output_sentence("media_repeated")
    def pause_ended(self):
        self.select_and_output_sentence("pause_ended")
    def pause_started(self):
        self.select_and_output_sentence("pause_started")
    def beat_change_slower(self, freq, pattern):
        self.select_and_output_sentence("beat_change_slower")
    def beat_change_faster(self, freq, pattern):
        self.select_and_output_sentence("beat_change_faster")
    def beat_change_general(self, freq, pattern):
        if random.uniform(0, 1) < 0.5:
            self.select_and_output_sentence("beat_change_general")
        else:
            if self.cur_freq > freq:
                self.beat_change_slower(freq, pattern)
            elif self.cur_freq < freq:
                self.beat_change_faster(freq, pattern)
            else:
                self.select_and_output_sentence("beat_change_general")

        self.cur_freq = freq

    def select_and_output_sentence(self, category):
        if not self.active_callout:
            return
        if self.is_teasing:
            return
        if random.uniform(0, 1) > self.talking_chance:
            return
        try:
            tease = random.choice(self.callout_data[self.lang][category])
            self.new_tease_event.emit(tease)
            self.is_teasing = True
            self.tease_active_timer.start(self.tease_time)
        except:
            print(f"Category {category} is empty or not present.")

    def _tease_timer_handler(self):
        self.hide_tease_event.emit()
        self.is_teasing = False

    def register_new_tease_event(self, show_handler, hide_handler):
        self.new_tease_event.connect(show_handler)
        self.hide_tease_event.connect(hide_handler)