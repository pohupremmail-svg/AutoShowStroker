import json
import random
from pathlib import Path

from PyQt6.QtCore import QObject, QTimer, pyqtSignal

from src.applog import get_logger
from src.utils import get_project_root

log = get_logger(__name__)

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
    # The user pressing "I Came" at a fake climax. Deliberately does *not* give the joke
    # away - the reveal is still on its own timer and lands seconds later, which is the
    # whole shape of the moment.
    "fake_climax_fell_for",
    # The user pressing "I reached my Edge" - the one moment they ask the app for mercy
    # rather than the other way round.
    "edge_reached",
]


class CalloutHandler(QObject):
    SETTINGS_GROUP = "CalloutHandler"  # see BeatHandler.SETTINGS_GROUP

    new_tease_event = pyqtSignal(str)
    hide_tease_event = pyqtSignal()

    # Tone is the second axis alongside language: res/callouts/<lang>/<tone>.json. The
    # labels only decide what the Settings checkboxes read and in which order they appear -
    # the tones themselves are discovered from the files, so dropping a new <tone>.json
    # into a language folder is enough to offer it (it then gets a title-cased label).
    # Ordered soft to harsh, with the detached one last - this is the order the
    # Settings checkboxes appear in.
    TONE_LABELS = {
        "flirty": "Flirty",
        "shy": "Shy",
        "bratty": "Bratty",
        "drunk": "Drunk",
        "girlfriend": "Girlfriend Experience",
        "nurturing": "Nurturing",
        "dominant": "Dominant",
        "sadistic": "Sadistic",
        "degrading": "Degrading",
        "hard_degrading": "Degrading (Hard)",
        "clinical": "Clinical",
    }
    # What the app used to say with no tone axis at all, so it stays the default for
    # everyone who upgrades - and the fallback whenever a selection turns out to be
    # unusable, which is what keeps "degrading" strictly opt-in.
    DEFAULT_TONE = "flirty"

    # Single source of truth: __init__ reads these as its fallbacks, and the
    # SettingsDialog "Reset to defaults" buttons read the same dict.
    DEFAULTS = {
        "active_callout": False,
        "talking_chance": 0.5,
        "lang": "en",
        "selected_tones": [DEFAULT_TONE],
    }

    def __init__(self, settings=None, data_store=None, callout_dir=None):
        super().__init__()
        self.settings = settings
        self.data_store = data_store
        self.tease_active_timer = QTimer()
        self.tease_active_timer.timeout.connect(self._tease_timer_handler)
        self.tease_time = 7000
        self.lang = self.DEFAULTS["lang"]
        # get_project_root() rather than a cwd-relative path (CLAUDE.md requires it for
        # every res/ read): this module used to carry its own copy of get_resource_path
        # built on os.path.abspath, so launching from any other working directory pointed
        # at a res/callouts that doesn't exist. Injectable for tests.
        self.callout_dir = Path(callout_dir) if callout_dir else get_project_root() / "res" / "callouts"

        self.is_teasing = False

        self.available_languages: list[str] = []
        # lang -> tone -> trigger key -> phrases
        self.callout_data: dict[str, dict[str, dict]] = {}
        self.custom_phrase_files: list[dict] = []

        self._load_available_languages()
        self.active_callout = self.DEFAULTS["active_callout"]
        self.talking_chance = self.DEFAULTS["talking_chance"]
        self.selected_tones = list(self.DEFAULTS["selected_tones"])
        self.cur_freq = 0

        if settings is not None:
            # Pass the current value as the default for each: QSettings.value() with a
            # type= but no default returns a default-constructed value for a missing key,
            # so a fresh profile used to load talking_chance as 0.0 - gating every ambient
            # callout behind a 0% chance - and selected_lang as the literal string "None".
            self.active_callout = bool(
                self.settings.value("CalloutHandler/active_callout", self.active_callout, type=bool)
            )
            self.set_lang(str(self.settings.value("CalloutHandler/selected_lang", self.lang)))
            self.talking_chance = float(
                self.settings.value("CalloutHandler/talking_chance", self.talking_chance)
            )
            self.set_tones(self.settings.value("CalloutHandler/selected_tones", self.selected_tones))

        # Which phrase files the user added is their own data, so it lives in a JSON file
        # rather than the registry (see src/user_data.py).
        if self.data_store is not None:
            self.custom_phrase_files = self.data_store.load(
                "custom_phrase_files", [], self.settings, "CalloutHandler/custom_phrase_files"
            )
            self._apply_stored_custom_files()

    # --- loading ---

    def _load_available_languages(self):
        self.available_languages = []
        self.callout_data = {}

        if not self.callout_dir.is_dir():
            # Was an `assert`, which killed startup with no window and no message - and got
            # stripped entirely under python -O, silently booting into this same state
            # instead. Degrading here matches how an *empty* callout dir already behaved,
            # and how user_data.py treats unreadable data: never stop the app from starting.
            log.error("No callout directory at %s - callouts are disabled", self.callout_dir)
            return

        for lang_dir in sorted(path for path in self.callout_dir.iterdir() if path.is_dir()):
            lang_code = lang_dir.name
            tones: dict[str, dict] = {}

            for json_file in sorted(lang_dir.glob("*.json")):
                try:
                    with open(json_file, encoding='utf-8') as f:
                        tones[json_file.stem] = json.load(f)
                except Exception as e:
                    # One broken tone must not cost the user the whole language.
                    log.warning("Could not load the callout file %s/%s: %s", lang_code, json_file.name, e)

            self.available_languages.append(lang_code)
            self.callout_data[lang_code] = tones

        if self.available_languages and (
            self.lang not in self.callout_data or self.lang not in self.available_languages
        ):
            self.set_lang(self.available_languages[0])

    def set_lang(self, lang):
        # Both clauses check the *incoming* language. The second used to read self.lang,
        # i.e. the value being replaced, which meant _load_available_languages' fallback
        # could never fire in exactly the case it exists for: the configured language
        # having no file, so self.lang is not in available_languages and every assignment
        # gets rejected, pinning the handler to a language it has no data for.
        if lang in self.callout_data and lang in self.available_languages:
            self.lang = lang
        else:
            log.warning("Language %r is not available", lang)

    # --- tones ---

    @classmethod
    def _ordered_tones(cls, tones) -> list[str]:
        """Declared tones first, in TONE_LABELS order, then anything else alphabetically."""
        declared = [tone for tone in cls.TONE_LABELS if tone in tones]
        return declared + sorted(tone for tone in tones if tone not in cls.TONE_LABELS)

    @classmethod
    def tone_label(cls, tone: str) -> str:
        return cls.TONE_LABELS.get(tone, tone.replace("_", " ").title())

    @property
    def available_tones(self) -> list[str]:
        """Every tone any installed language ships - the set the Settings dialog offers."""
        return self._ordered_tones({tone for tones in self.callout_data.values() for tone in tones})

    def tones_for(self, lang: str) -> list[str]:
        return self._ordered_tones(self.callout_data.get(lang, {}))

    def set_tones(self, tones):
        """Replaces the active tone mix, dropping anything no language ships.

        QSettings hands a one-element string list back as a bare str, same trap
        BeatHandler.selected_beat_patterns has to defuse.
        """
        if isinstance(tones, str):
            tones = [tones]
        elif tones is None:
            tones = []
        available = self.available_tones
        kept = [tone for tone in tones if tone in available]
        dropped = [tone for tone in tones if tone not in available]
        if dropped:
            log.warning("Ignoring unavailable callout tones: %s", ", ".join(map(str, dropped)))
        self.selected_tones = kept

    def _usable_tones(self) -> list[str]:
        """The selected tones that the current language actually has files for.

        Falls back to the default tone rather than to "everything on disk": an empty or
        stale selection must never quietly switch a harsher tone on.
        """
        available = self.tones_for(self.lang)
        usable = [tone for tone in self.selected_tones if tone in available]
        if usable:
            return usable
        if self.DEFAULT_TONE in available:
            return [self.DEFAULT_TONE]
        return available

    # --- custom phrase files ---

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

    def _merge_phrases(self, lang: str, tone: str, data: dict):
        # Tolerant like _load_available_languages: skip anything malformed rather than
        # hard-fail - an unknown/typo'd trigger key silently doesn't contribute, same
        # philosophy CONTRIBUTING.md already documents for the shipped files.
        for trigger_key, phrases in data.items():
            if trigger_key not in TRIGGER_KEYS:
                continue
            if not isinstance(phrases, list) or not all(isinstance(p, str) for p in phrases):
                continue
            self.callout_data.setdefault(lang, {}).setdefault(tone, {}).setdefault(trigger_key, []).extend(phrases)
        if lang not in self.available_languages:
            self.available_languages.append(lang)

    def _merge_custom_phrases(self, lang: str, tone, data: dict):
        """tone=None means "any tone": the file contributes whichever tones are active,
        which is also how phrase files added before tones existed have to keep behaving."""
        targets = [tone] if tone else (self.tones_for(lang) or [self.DEFAULT_TONE])
        for target in targets:
            self._merge_phrases(lang, target, data)

    def _apply_stored_custom_files(self):
        for entry in self.custom_phrase_files:
            try:
                self._merge_custom_phrases(entry["lang"], entry.get("tone"), self._read_custom_file(entry["path"]))
            except ValueError as e:
                log.warning("Skipping a custom callout file: %s", e)

    def load_custom_file(self, path: str, lang: str, tone: str | None = None):
        if lang not in self.available_languages:
            raise ValueError(f"Unknown language: {lang!r}")
        if tone is not None and tone not in self.available_tones:
            raise ValueError(f"Unknown tone: {tone!r}")
        if any(entry["path"] == path for entry in self.custom_phrase_files):
            raise ValueError("That file is already loaded.")
        data = self._read_custom_file(path)  # raises before any mutation happens
        self._merge_custom_phrases(lang, tone, data)
        self.custom_phrase_files.append({"path": path, "lang": lang, "tone": tone})
        self._save_custom_phrase_files()

    def unload_custom_file(self, path: str):
        self.custom_phrase_files = [e for e in self.custom_phrase_files if e["path"] != path]
        self._load_available_languages()  # reset to shipped-only state
        self._apply_stored_custom_files()  # reapply whatever custom files remain
        self._save_custom_phrase_files()

    def clear_custom_phrase_files(self):
        """Forgets every added phrase file, including the phrases already merged in - a
        reload back to the shipped-only state, same as unload_custom_file does."""
        self.custom_phrase_files = []
        self._load_available_languages()
        if self.data_store:
            self.data_store.delete("custom_phrase_files")

    def _save_custom_phrase_files(self):
        if not self.data_store:
            return
        self.data_store.save("custom_phrase_files", self.custom_phrase_files)

    # --- triggers ---

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

    def pick_phrase(self, category) -> str | None:
        """One random phrase for `category` out of the active tone mix, or None.

        A tone is drawn first and only then a line from it, so every ticked tone gets an
        equal voice - pooling the lists instead would let a 250-line tone drown out a
        100-line one purely because someone wrote more of it.
        """
        tones = self.callout_data.get(self.lang, {})
        pools = []
        for tone in self._usable_tones():
            phrases = tones.get(tone, {}).get(category)
            if isinstance(phrases, list) and phrases:
                pools.append(phrases)

        if not pools:
            log.warning("No %r phrases available for language %r", category, self.lang)
            return None
        return random.choice(random.choice(pools))

    def _show_tease(self, tease: str):
        self.new_tease_event.emit(tease)
        self.is_teasing = True
        self.tease_active_timer.start(self.tease_time)

    def select_and_output_sentence(self, category):
        if not self.active_callout:
            return
        if self.is_teasing:
            return
        if random.uniform(0, 1) > self.talking_chance:
            return
        tease = self.pick_phrase(category)
        if tease is None:
            return
        self._show_tease(tease)

    def force_output_sentence(self, category):
        """Emits a phrase from `category` unconditionally, skipping the active_callout/
        talking_chance/is_teasing gates that select_and_output_sentence uses. For scripted
        narrative beats (climax outcome, fake-climax reveal) that must always display,
        not ambient flavor text."""
        tease = self.pick_phrase(category)
        if tease is None:
            return
        self._show_tease(tease)

    def _tease_timer_handler(self):
        self.hide_tease_event.emit()
        self.is_teasing = False

    def register_new_tease_event(self, show_handler, hide_handler):
        self.new_tease_event.connect(show_handler)
        self.hide_tease_event.connect(hide_handler)
