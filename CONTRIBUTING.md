## Contribution

We welcome contributions for our teasing lines component of the program.

---

### Adding New Teasing Phrases (Callouts)

The application uses **JSON files** to manage all spoken teasing phrases (Callouts). You can easily contribute new
phrases without touching the Python code.

#### 1. Locate the Files

All callout data lives in `./res/callouts/`, one folder per language and one file per tone
inside it:

```
res/callouts/
  en/
    flirty.json
    shy.json
    bratty.json
    ...
  de/  ... a subset is fine, see below
  fr/  ... a subset is fine, see below
```

**Language and tone are two independent axes.** The user picks exactly one language and any
number of tones; the app then mixes the ticked tones evenly. That is why the file name carries
the tone and the folder carries the language — the same `{trigger_key: [phrases]}` content,
indexed twice.

#### 2. Understand the Structure

Inside a tone file, the structure is based on **Trigger Keys**. When a specific event happens in
the app (e.g. the beat speeds up), the corresponding key is used to randomly select one phrase
from the array.

```json
{
  "beat_change_general": [
    "Phrase 1 for general beat change.",
    "Phrase 2 for general beat change."
  ],
  "beat_change_faster": [
    "This is a faster beat phrase."
  ]
  // ... and so on for all keys
}
```

The available Trigger Keys at the moment are:

| Trigger Key           | Description                                                                                  |
|:----------------------|:---------------------------------------------------------------------------------------------|
| `beat_change_general` | Fired when the beat changes frequency or rhythm.                                             |
| `beat_change_faster`  | Fired when the beat frequency increases significantly.                                       |
| `beat_change_slower`  | Fired when the beat frequency decreases significantly.                                       |
| `pause_start`         | Fired when the session enters an intentional pause.                                          |
| `pause_end`           | Fired when the session resumes after a pause.                                                |
| `media_skipped`       | Fired when the user skips the currently displayed media.                                     |
| `media_repeated`      | Fired when the user decides to repeat the current media file by pressing the `previous` key. |
| `session_started`     | Fired when the session is started using the `Set gooning folder and start` button.           |
| `climax_real`         | Fired once per session when the climax system decides on a real orgasm outcome.              |
| `climax_ruined`       | Fired once per session when the climax system decides on a ruined orgasm outcome.             |
| `climax_denied`       | Fired once per session when the climax system decides the session ends without an orgasm.     |
| `fake_climax_reveal`  | Fired a few seconds after a fake climax cue (which reuses `climax_real`) to reveal it was a joke. |
| `fake_climax_fell_for` | Fired when the user answers a fake climax cue with "I Came". Must **not** give the joke away - the reveal is still a few seconds out, and that order is the point. |
| `edge_reached`        | Fired when the user presses "I reached my Edge" (or `E`) - the one moment they ask the app for mercy. A pause and a gentler rhythm follow, so answer the admission rather than announcing the pause. |

The tones that ship today:

| Tone         | File            | Voice                                                                            |
|:-------------|:----------------|:---------------------------------------------------------------------------------|
| Flirty       | `flirty.json`   | Playful, teasing, winking. The app's original voice and the default.             |
| Shy          | `shy.json`      | Timid, blushing, hesitant — apologises for being bossy.                           |
| Dominant     | `dominant.json` | Commanding and certain. Imperatives, no negotiation.                              |
| Degrading    | `degrading.json`| Humiliation play: mocks the user's neediness and stamina. **Opt-in, never default.** |
| Degrading (Hard) | `hard_degrading.json` | The same register turned up: small-penis humiliation and inadequacy throughout. **Opt-in, never default.** |
| Bratty       | `bratty.json`   | Petulant and entitled — teases by withholding rather than commanding. |
| Drunk        | `drunk.json`    | Loud, sloppy, uninhibited. All exclamation marks and no filter. |
| Nurturing    | `nurturing.json`| Caring and protective, softer than Girlfriend Experience and less flirtatious. |
| Sadistic     | `sadistic.json` | Enjoys the suffering itself rather than the obedience. **Opt-in, never default.** |
| Clinical     | `clinical.json` | Detached and procedural, like a technician reading instructions. |
| Girlfriend Experience | `girlfriend.json` | Warm, affectionate, present. Ranges from sweet to gently bossy, never degrading. |

#### 3. How to Contribute Phrases

To add a new phrase, append your new text string to the relevant array in the file for the
language **and** tone you are writing for (e.g. `en/dominant.json`):

```json
"beat_change_faster": [
    "The speed is picking up. Can you keep the rhythm?",
    "Go faster! Your endurance is being tested.",
    "Your new exciting phrase goes here!" ⬅️ **ADD IT HERE**
],
```

Keep the phrase in that file's voice. A gentle line in `dominant.json` weakens the tone for
everyone who ticked it precisely because they wanted the harsh one — put it in the tone where it
belongs instead.

---

### Adding a New Language

To add a completely new language (e.g. Spanish), create a folder named with the standard
two-letter language code and give it **one file per shipped tone**:

```
res/callouts/es/flirty.json      <- required
res/callouts/es/shy.json         <- optional
res/callouts/es/dominant.json    <- optional
...
```

1. Copy the key structure from the matching English file — every Trigger Key must be present and
   non-empty, because a tone that is ticked but silent for one event looks like a bug.
2. Write the phrases in that language rather than translating word for word. A callout that reads
   like a translation breaks the mood faster than a missing one.
3. **`flirty.json` is the only required file.** A language may ship any subset of the other tones:
   `CalloutHandler` skips a tone the current language lacks and falls back to the default, and the
   Settings dialog marks such a tone `(not in <lang>)` so the gap is visible rather than silent.
   `flirty` is required precisely because it is what that fallback lands on.
4. No code changes are needed — `CalloutHandler` discovers languages and tones from the folder
   layout at startup, and the Settings dialog builds its controls from what it finds.

### Adding a New Tone

Adding a tone works the same way in the other direction, except you do not have to do every
language at once — one `<tone>.json` in a single language folder already makes the tone appear.
A tone nobody declared a label for still works and is offered with a title-cased name
(`bratty.json` → "Bratty"); adding it to `CalloutHandler.TONE_LABELS` gives it a proper display
name and a fixed position in the list.

Before proposing a new tone, check it is a genuinely distinct voice rather than a few phrases the
existing tones could hold — the user can already tick several tones at once, so a tone that is
just "two existing ones mixed" adds files without adding range.

#### Validate Your Files

A missing or misspelled Trigger Key (e.g. `beat_changed_general` instead of `beat_change_general`)
will not throw an error — the app just silently stays quiet for that event, which is easy to miss
by playing the app alone. Before opening a PR, run the automated schema check:

```bash
python -m pytest tests/test_callout_language_files.py -v
```

It walks every `res/callouts/*/*.json` and verifies: valid JSON, all required Trigger Keys, no
unknown/typo'd keys, every value a list of strings, no empty phrase lists, no duplicate phrases
within a list, no phrase shared between two tones of the same language, every language shipping at
least the default tone, and no stray phrase file outside a language folder. It does **not** check that your writing reads well — for that, still run the app
(`python main.py`), pick your language and tone in Settings, and play through a session.

---

### 🚀 Submitting Your Contribution (Pull Requests)

Once you have added new phrases, created a new language file, or made code changes, please follow these steps to submit
your work:

#### 1. Branching

All contributions must be made from a new feature branch, not directly to the `main` branch.

```bash
git checkout main
git pull
git checkout -b feature/add-french-callouts  # Use a descriptive name
```

#### 2. Commit Messages

Please ensure your commit messages are descriptive and reference the area you are modifying.

#### 3. Run the Checks

Before pushing, run the full test suite and linter to make sure nothing else broke:

```bash
python -m pytest
ruff check .
```

#### 4. Create the Pull Request (PR)

- Push your new branch to your fork.

- Open a Pull Request targeting the main branch of the original repository.

- PR Title: Use a clear, concise title that summarizes your work.

- PR Description: Describe what you added, why it improves the experience, and which files were changed (especially for
  new language files).

- We will review your PR as quickly as possible. Thank you for making the app better!