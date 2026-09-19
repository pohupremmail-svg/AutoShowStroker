# GoonerApp session file format

A saved GoonerApp session is a single JSON file. The app writes them from sessions you have
played, but nothing stops you writing one by hand — or having an LLM compose one for you and
importing it through **Sessions > Saved Sessions > Import...**.

This document is written to be handed to an LLM wholesale. It is everything needed to
produce a file the app will accept.

---

## The shape

```json
{
  "format": 1,
  "duration_sec": 1800,
  "segments": [
    { "kind": "beat",   "pattern": "Slow Pulse",  "freq": 1.2, "duration_sec": 240 },
    { "kind": "pause",  "pattern": null,          "freq": null, "duration_sec": 20 },
    { "kind": "beat",   "pattern": "Quick Swing", "freq": 2.4, "duration_sec": 300 },
    { "kind": "finale", "pattern": "Build Up",    "freq": 4.6, "duration_sec": 180 }
  ],
  "custom_patterns": {},
  "climax": { "at_sec": 1700, "outcome": "ruined" },
  "fake_climaxes": [480, 1100],
  "media": [ { "at_sec": 0 }, { "at_sec": 14 }, { "at_sec": 31 } ]
}
```

That file is complete and playable. `format`, `segments` and `duration_sec` are required;
everything else may be omitted or empty.

**Every time in the file is seconds from the start of the session.** Never a clock time,
never a date. A replay starts whenever the user starts it.

---

## Fields

| Field | What it is |
|:--|:--|
| `format` | Always `1`. The app refuses a file from a newer format rather than half-reading it. |
| `duration_sec` | How long the whole session runs. Every other moment must fall inside it. |
| `segments` | The session in order. Each one plays for its `duration_sec`. |
| `custom_patterns` | Rhythms you invented, `{"name": [steps]}`. Empty when you only use built-ins. |
| `climax` | `{"at_sec": …, "outcome": …}`, or `null` for a session that never finishes. |
| `fake_climaxes` | Moments the app pretends the climax has arrived, then admits it was a joke. |
| `media` | When the picture on screen changes. See **Media** below — this is the part people get wrong. |
| `app_version`, `saved_at` | Written by the app, ignored on import. Leave them out. |

### Segments

| Key | Notes |
|:--|:--|
| `kind` | `"beat"`, `"pause"` or `"finale"`. |
| `duration_sec` | Positive. |
| `pattern` | A rhythm name (see below). `null` on a pause. |
| `freq` | Beats per second, positive. `null` on a pause. |

`"finale"` is the segment the climax happens during. It behaves exactly like a beat; the
name only records that this is the run-in. Put the climax inside it and make it fast.

A `"pause"` is a full break — the Strokemeter counts down and nothing plays. Its length is
rounded to whole seconds.

### Frequencies

`freq` is beats per second, so `1.0` is one stroke a second and `4.0` is four.

- `0.5 – 1.2` — slow, teasing, hard to stay patient through
- `1.5 – 2.5` — the comfortable middle of most sessions
- `3.0 – 4.0` — demanding
- `4.5 – 6.0` — a run-in, not a stretch anyone holds for long

Anything above roughly `6.0` stops being followable.

### Built-in rhythm names

Use these verbatim. A name that is neither built in nor defined in `custom_patterns` is
rejected on import, naming the offender — it is the single most common mistake.

```
Standard Beat      Quick Swing        Simple Bounce      Double Tap
Syncopated 4/4     Slow Pulse         Held Breath        Double Tap Pause
Delayed Swing      Triple Quick Tap   Missing Third      Build Up
Slow Down          Speed Change       Suspense Build
```

### Inventing a rhythm

```json
"custom_patterns": { "Cruel Stutter": [1, 4, 4, -2, -2, 1] }
```

Each step is a number from 1 to 4. **Positive is an audible beat, negative is silence of the
same length, and the size is inverted: 1 is the longest step, 4 the shortest.** Never `0`.
So `[1, 4, 4, -2, -2, 1]` reads as: one long beat, two quick ones, a rest twice as long as
those quick beats, then a long beat again.

### The climax

`outcome` is one of:

| Outcome | What happens |
|:--|:--|
| `"real"` | She tells them to come, and the rhythm carries them through it. |
| `"ruined"` | She tells them to ruin it. |
| `"denied"` | She tells them no — and the Strokemeter stops dead on the spot. |

`at_sec` must be inside the session, and should sit inside a `"finale"` segment so the
rhythm is fast when it lands.

`"climax": null` is legal and means the session was stopped before one. The app then plays
what you wrote and carries on afterwards with a climax of its own, so a file like that is a
warm-up rather than a whole session.

### Media

This is the part worth reading twice.

- **You never have to name a file.** A session with `"media": []` replays perfectly against
  whatever collection the person importing it has, at their own picture speed.
- `[{"at_sec": 0}, {"at_sec": 14}, {"at_sec": 31}]` sets the *pacing* without naming
  anything: the picture changes at 0s, 14s and 31s, from their own library. This is how you
  choreograph "slow, lingering images through the build, then rapid-fire into the finale".
- The moments must run forwards and stay inside the session.
- Sessions recorded by the app also carry a `"path"` on each entry. **Do not invent paths.**
  A file with paths only works on the machine those files live on.

---

## Writing one worth playing

The format will accept a flat wall of identical segments. Nobody wants that. What makes a
session good:

- **Build.** Start slow and long, finish fast and short. A 30-minute session might open with
  4-minute segments around `1.2` and end with 90-second segments around `4.5`.
- **Break the climb.** A pause, or a segment that drops back down, is what makes the next
  rise land. A session that only ever accelerates flattens out emotionally.
- **Use the fake-outs.** Two or three across a long session, none of them near the real one.
  They are the cruellest thing in the app and cost nothing to place.
- **Match the media pacing to the beat.** Long, slow images while the rhythm is slow;
  quicker changes as it tightens. This is half of what a session feels like.
- **Put the climax inside the finale**, with the finale fast and running a little past it.

---

## Importing

**Sessions > Saved Sessions > Import...**, pick the file. If anything is wrong the app lists
every problem it found rather than refusing flatly, so a generated file can be corrected in
one pass.
