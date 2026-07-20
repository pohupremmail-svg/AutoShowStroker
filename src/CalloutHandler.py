import json
import os
import random
import sys
from pathlib import Path

from PyQt6.QtCore import QObject, QTimer, pyqtSignal

TRIGGER_KEYS = [
    "beat_change_general",
    "beat_change_faster",
    "beat_change_slower",
    "pause_start",
    "pause_end",
    "media_skipped",
    "media_repeated",
    "session_started",
    "climax_real",
    "climax_ruined",
    "climax_denied",
    "fake_climax_reveal",
]


def get_resource_path(relative_path):
    """ Liefert den absoluten Pfad zur Ressource, passend für Entwicklung und PyInstaller-EXE """
    if hasattr(sys, '_MEIPASS'):
        return os.path.join(sys._MEIPASS, relative_path)
    return os.path.abspath(relative_path)


class CalloutHandler(QObject):

    new_tease_event = pyqtSignal(str)
    hide_tease_event = pyqtSignal()

    # Keep in sync with the literal defaults set in __init__ below - single source of truth
    # for the SettingsDialog "Reset to defaults" button.
    DEFAULTS = {
        "active_callout": False,
        "talking_chance": 0.5,
        "lang": "en",
    }

    def __init__(self, settings=None):
        super().__init__()
        self.settings = settings
        self.tease_active_timer = QTimer()
        self.tease_active_timer.timeout.connect(self._tease_timer_handler)
        self.tease_time = 7000
        self.lang = "en"
        self.callout_dir = Path(get_resource_path("res/callouts"))

        self.is_teasing = False

        self.available_languages: list[str] = []
        self.callout_data: dict[str, dict] = {}
        self.custom_phrase_files: list[dict] = []

        self._load_available_languages()
        self.active_callout = False
        self.talking_chance = 0.5
        self.cur_freq = 0

        if settings is not None:
            self.active_callout = bool(self.settings.value('CalloutHandler/active_callout', type=bool))
            self.set_lang(str(self.settings.value("CalloutHandler/selected_lang")))
            self.talking_chance = float(self.settings.value("CalloutHandler/talking_chance", type=float))
            raw_custom_files = self.settings.value("CalloutHandler/custom_phrase_files")
            self.custom_phrase_files = json.loads(raw_custom_files) if raw_custom_files else []
            self._apply_stored_custom_files()

    def _load_available_languages(self):
        assert self.callout_dir.is_dir()

        self.available_languages = []
        self.callout_data = {}

        for json_file in self.callout_dir.glob("*.json"):
            lang_code = json_file.stem

            self.available_languages.append(lang_code)

            try:
                with open(json_file, encoding='utf-8') as f:
                    self.callout_data[lang_code] = json.load(f)
            except Exception as e:
                print(f"Error loading the callout file {json_file}: {e}")

        if self.available_languages and (
            self.lang not in self.callout_data or self.lang not in self.available_languages
        ):
            self.set_lang(self.available_languages[0])


    def set_lang(self, lang):
        if lang in self.callout_data and self.lang in self.available_languages:
            self.lang = lang
        else:
            print(f"Tried setting {lang}. That language is not available.")

    def _read_custom_file(self, path: str) -> dict:
        try:
            with open(path, encoding="utf-8") as f:
                data = json.load(f)
        except OSError as e:
            raise ValueError(f"Couldn't read {path}: {e}") from e
        except json.JSONDecodeError as e:
            raise ValueError(f"{path} isn't valid JSON: {e}") from e
        if not isinstance(data, dict):
            raise ValueError(f"{path} must contain a JSON object of trigger key -> phrase list.")
        return data

    def _merge_phrases(self, lang: str, data: dict):
        # Tolerant like _load_available_languages: skip anything malformed rather than
        # hard-fail - an unknown/typo'd trigger key silently doesn't contribute, same
        # philosophy CONTRIBUTING.md already documents for the shipped files.
        for trigger_key, phrases in data.items():
            if trigger_key not in TRIGGER_KEYS:
                continue
            if not isinstance(phrases, list) or not all(isinstance(p, str) for p in phrases):
                continue
            self.callout_data.setdefault(lang, {}).setdefault(trigger_key, []).extend(phrases)
        if lang not in self.available_languages:
            self.available_languages.append(lang)

    def _apply_stored_custom_files(self):
        for entry in self.custom_phrase_files:
            try:
                self._merge_phrases(entry["lang"], self._read_custom_file(entry["path"]))
            except ValueError as e:
                print(f"Skipping custom callout file: {e}")

    def load_custom_file(self, path: str, lang: str):
        if lang not in self.available_languages:
            raise ValueError(f"Unknown language: {lang!r}")
        if any(entry["path"] == path for entry in self.custom_phrase_files):
            raise ValueError("That file is already loaded.")
        data = self._read_custom_file(path)  # raises before any mutation happens
        self._merge_phrases(lang, data)
        self.custom_phrase_files.append({"path": path, "lang": lang})
        self._save_custom_phrase_files()

    def unload_custom_file(self, path: str):
        self.custom_phrase_files = [e for e in self.custom_phrase_files if e["path"] != path]
        self._load_available_languages()  # reset to shipped-only state
        self._apply_stored_custom_files()  # reapply whatever custom files remain
        self._save_custom_phrase_files()

    def _save_custom_phrase_files(self):
        if not self.settings:
            return
        self.settings.setValue("CalloutHandler/custom_phrase_files", json.dumps(self.custom_phrase_files))

    def session_started(self):
        self.select_and_output_sentence("session_started")
    def media_skipped(self):
        self.select_and_output_sentence("media_skipped")
    def media_repeated(self):
        self.select_and_output_sentence("media_repeated")
    def pause_ended(self):
        self.select_and_output_sentence("pause_end")
    def pause_started(self):
        self.select_and_output_sentence("pause_start")
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
        except (KeyError, IndexError):
            print(f"Category {category} is empty or not present.")

    def force_output_sentence(self, category):
        """Emits a phrase from `category` unconditionally, skipping the active_callout/
        talking_chance/is_teasing gates that select_and_output_sentence uses. For scripted
        narrative beats (climax outcome, fake-climax reveal) that must always display,
        not ambient flavor text."""
        try:
            tease = random.choice(self.callout_data[self.lang][category])
        except (KeyError, IndexError):
            print(f"Category {category} is empty or not present.")
            return
        self.new_tease_event.emit(tease)
        self.is_teasing = True
        self.tease_active_timer.start(self.tease_time)

    def _tease_timer_handler(self):
        self.hide_tease_event.emit()
        self.is_teasing = False

    def register_new_tease_event(self, show_handler, hide_handler):
        self.new_tease_event.connect(show_handler)
        self.hide_tease_event.connect(hide_handler)
