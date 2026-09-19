---
name: gooner-session-builder
description: Compose a GoonerApp session file - a JSON choreography of rhythms, pauses, fake-outs and a climax that the app replays beat for beat. Use when someone asks for a GoonerApp session, a stroking/edging session file, a .json session to import into GoonerApp, or wants a session built to a particular length, difficulty or ending.
---

# Building a GoonerApp session

GoonerApp replays a saved session exactly as it was written: the same rhythms at the same
moments, the same pauses, the same climax. That file is plain JSON, so it can be composed
rather than recorded - which is what this skill is for.

The full field reference is in `SESSION_FORMAT.md` next to this file. **Read it before
writing any JSON.** What follows is how to decide what goes in it.

## Ask first

A session is 20 minutes to two hours of someone's evening. Four questions, and do not skip
them - guessing produces a flat file:

1. **How long?** This sets everything else.
2. **How hard?** Gentle and drawn out, or punishing. This is the frequency range and how
   much pause there is.
3. **How should it end?** Allowed, ruined, or denied. Denied is the cruellest and the app
   stops the rhythm dead when it lands.
4. **Cruel touches?** Fake climaxes - the app announces the climax, they act on it, and then
   it admits it was lying. Two or three in a long session.

If they only say "make me a session", ask anyway.

If they would rather not decide, **decide for them and say what you decided** - "an hour,
building hard, and it denies you at the end; tell me if you want it gentler". Commit to a
shape rather than reaching for a safe middle: the app already draws its own session at random
every time somebody presses start, so a deliberately average composed one is the single thing
here with no reason to exist. Use whatever you know about them - the hour, how the
conversation has gone, what they asked for last time - and make it specific. Two people who
both shrug should not get the same file.

## Shape the arc, do not fill a table

The format accepts a flat wall of identical segments and the result is lifeless. A session
that works has a shape:

- **Open slow and long.** Four-minute segments around `0.9-1.3` beats per second. The
  opening is about patience, not effort.
- **Climb in steps, not a ramp.** Each segment a little faster and a little shorter than the
  last. Roughly: 25% of the session settling in, 50% climbing, 25% at the top.
- **Break the climb deliberately.** A pause, or a segment that drops back down, two or three
  times. This is what makes the next rise land - a session that only accelerates flattens
  out emotionally about two thirds through.
- **Finish short and fast.** 60-120 second segments at `4.0-5.5`.
- **The finale segment holds the climax.** Mark it `"kind": "finale"`, make it the fastest
  thing in the file, and have it run a little past `climax.at_sec` so the rhythm is still
  going when the announcement lands.

Vary the rhythm names as you go. Reusing one for the whole session wastes fourteen others,
and the patterns feel genuinely different - `Held Breath` is nothing like `Triple Quick Tap`.

## Choreograph the pictures too

Half of what a session feels like is how fast the images change, and you can set that
without naming a single file:

```json
"media": [ {"at_sec": 0}, {"at_sec": 22}, {"at_sec": 44} ]
```

Long, lingering changes while the rhythm is slow - 20-40 seconds apart. Tighter as it
climbs. Two or three seconds apart through the finale is genuinely disorienting, in the way
the finale should be.

**Never invent file paths.** A session with no paths replays against whatever collection the
person importing it already has, which is the whole point.

## Before you hand it over

- Every `pattern` is either from the built-in list or defined in `custom_patterns`. This is
  the mistake that gets made; the app rejects the file by name when it happens.
- Every moment - `climax.at_sec`, each fake-out, each media change - is inside
  `duration_sec`.
- The segment durations add up to roughly `duration_sec`.
- No fake-out sits within a couple of minutes of the real climax; the reveal would land on
  top of the announcement.
- Tell them what you built: the arc, where the cruelty is, and how to import it
  (**Sessions > Saved Sessions > Import...**).

## The content

GoonerApp is an adult application and its sessions are explicitly erotic by design. The file
itself holds no text - only rhythms and timings - so nothing you write here is explicit. The
teasing voice comes from the app's own callouts, which are never part of a saved session:
they stay random, so the same session still surprises on the second run.
