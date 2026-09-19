---
name: add-callout-language
description: Add or extend the localized teasing-line callouts in res/callouts/<lang>/<tone>.json - a new language, a new tone, or new phrases in an existing file - following the project's CONTRIBUTING.md structure. Use when the user asks to add a callout language, a callout tone, or new teasing phrases.
---

Follow this to add or extend phrase files for `CalloutHandler` (`src/CalloutHandler.py`).

## The layout

`res/callouts/<lang>/<tone>.json` — the folder is the language, the file is the tone. Language
and tone are two independent axes: the user picks exactly one language and any number of tones,
and the handler mixes the ticked tones evenly (it draws a tone first, then a line from it).

Shipped today: `en` has `flirty`, `shy`, `bratty`, `drunk`, `girlfriend`, `nurturing`, `dominant`,
`sadistic`, `degrading`, `hard_degrading`, `clinical`; `de` and `fr` have the first six of those
minus `bratty`/`drunk`/`nurturing`/`sadistic`/`clinical`. **A language may ship a subset** — only
`flirty` (the `DEFAULT_TONE`) is required everywhere, because it is the fallback.

Every file holds the same schema, `{trigger_key: [phrases]}`, with all of these keys present and
non-empty — a missing or typo'd key does not raise, the app just goes silent for that event:

- `beat_change_general`
- `beat_change_faster`
- `beat_change_slower`
- `pause_start`
- `pause_end`
- `media_skipped`
- `media_repeated`
- `session_started`
- `climax_real`
- `climax_ruined`
- `climax_denied`
- `fake_climax_reveal`
- `fake_climax_fell_for` - the user just admitted they came at a fake cue. Write the reaction
  to the confession *without* revealing the trick: `fake_climax_reveal` still fires seconds
  later and is what lands it.
- `edge_reached` - the user pressed "I reached my Edge". They are asking for mercy, and a pause
  plus a slower beat is already on its way, so answer the admission rather than narrating the
  pause (`pause_start` does that).

## Adding phrases to an existing file

1. Open `res/callouts/<lang>/<tone>.json`.
2. Append the new string(s) to the array under the relevant trigger key.
3. Match that file's **voice**, not just its language. The tones are deliberately distinct:
   - `flirty` — playful, teasing, winking (the app's original voice and the default)
   - `shy` — timid, hesitant, apologetic about being bossy
   - `dominant` — commanding, imperative, no negotiation
   - `degrading` — humiliation play, mocking the user's neediness and stamina
   - `hard_degrading` — the same register at full strength: small-penis humiliation and
     inadequacy running through every line
   - `girlfriend` — warm, affectionate, present; sweet to gently bossy, never degrading

   A soft line dropped into `dominant.json` weakens the tone for exactly the people who ticked it
   on purpose. Put the phrase in the tone it belongs to instead.
4. This is explicit adult content by design — that is expected in these files.

## Adding a brand-new language

1. Create `res/callouts/<code>/` using the standard two-letter language code (e.g. `es/`).
2. Add at least `flirty.json`; everything else is optional. A tone the language lacks is skipped at
   draw time and marked `(not in <lang>)` in Settings, so a partial language is honest, not broken.
3. Write in the target language rather than translating the English line for line; a callout that
   reads like a translation breaks the mood faster than a missing one would.
4. No code changes are needed — `CalloutHandler._load_available_languages()` discovers both
   languages and tones from the folder layout at startup, and the Settings dialog builds its
   language combo and tone checkboxes from what it finds.

## Adding a brand-new tone

1. Add `<tone>.json` to at least one language folder. Doing every language is nicer but not required.
2. Add the tone to `CalloutHandler.TONE_LABELS` to give it a display name and a fixed position.
   An undeclared tone still works (it is offered with a title-cased name), but the shipped set is
   curated and `tests/test_callout_language_files.py` asserts it matches `TONE_LABELS` — that
   assertion is what catches a typo'd file name.
3. Ask the user before inventing a tone: since several tones can already be ticked at once, a tone
   that is just "two existing ones blended" costs one file per language and adds no range.
4. `CalloutHandler.DEFAULT_TONE` (`flirty`) is both the default selection and the fallback for an
   unusable one. Do not widen that fallback to "every available tone" — it is what keeps the
   harsher `degrading`/`hard_degrading` tones from ever switching themselves on.

## Verification

`tests/test_callout_language_files.py` discovers every `res/callouts/*/*.json` and checks: valid
JSON, all required trigger keys, no unknown/typo'd keys, values are lists of strings, no empty
phrase list, no duplicate phrase inside a list, no phrase shared between two tones of one
language, every language shipping at least the default tone, no stray phrase file left outside a
language folder, and that every shipped tone has a declared label.

```bash
python -m pytest tests/test_callout_language_files.py -v
```

This only validates structure, not content — it cannot tell you a translation reads awkwardly or
that a phrase is in the wrong tone. After the automated check passes, still ask the user to run
the app (`python main.py`), pick the new/changed language and tone in Settings > Callouts, and
play through a session before claiming the work is fully verified.
