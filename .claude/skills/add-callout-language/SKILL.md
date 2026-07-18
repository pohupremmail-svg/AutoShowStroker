---
name: add-callout-language
description: Add a new localized language file for teasing-line callouts (res/callouts/<lang>.json), or add new phrases to an existing language file, following the project's CONTRIBUTING.md structure. Use when the user asks to add a new callout language or new teasing phrases.
---

Follow this to add or extend a language file for `CalloutHandler` (`src/CalloutHandler.py`).

## Adding phrases to an existing language

1. Open `res/callouts/<lang>.json`.
2. Append the new phrase string(s) to the array under the relevant trigger key.
3. Keep tone/style consistent with existing phrases in that array — this is explicit adult content by design, matching the app's theme.
4. Validate the file is still valid JSON (e.g. `python -m json.tool res/callouts/<lang>.json > /dev/null`).

## Adding a brand-new language

1. Create `res/callouts/<code>.json` using the standard two-letter language code (e.g. `fr.json` for French).
2. Copy the full key structure from `res/callouts/en.json` — every trigger key below must be present, even with an empty array, or `CalloutHandler.select_and_output_sentence` will silently no-op for that category:
   - `beat_change_general`
   - `beat_change_faster`
   - `beat_change_slower`
   - `pause_start`
   - `pause_end`
   - `media_skipped`
   - `media_repeated`
   - `session_started`
3. Translate (or write new) phrases into each array.
4. No code changes are needed — `CalloutHandler._load_available_languages()` globs `res/callouts/*.json` at startup and the Settings dialog auto-detects the new language from the file's stem (e.g. `fr.json` → `fr`).
5. Validate the file is valid JSON before finishing.

## Verification

There's no automated test for this. After editing, ask the user to run the app (`python main.py`), select the new/changed language in Settings, and confirm phrases appear during a session — don't claim this is verified without that manual check.
