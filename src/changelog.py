"""User-facing changelog, shown in the in-app "What's New" popup.

Every `feat:` (or critical `fix:`) version bump must add an entry here describing what's
new, in the app's own flirty/teasing voice for the end user - not a commit-log dump. Write
entries as a short suggestive lead sentence, then an inline HTML `<ul><li>` bullet list of
the real features, then a one-line closing hook - WhatsNewDialog renders the body as rich
text, so the markup shows up as actual bullets. See CLAUDE.md's "Versioning & Releases"
section.
"""

CHANGELOG = {
    "0.1.0": (
        "GoonerApp's first release is here to edge you properly."
        "<ul>"
        "<li>Randomized slideshow playback that syncs perfectly to a fully configurable "
        "Strokemeter</li>"
        "<li>Multilingual teasing callouts in English, German, and French, whispering you "
        "along the whole way</li>"
        "<li>Difficulty ramping that slowly tightens the screws as your session builds</li>"
        "<li>A full climax announcement system - real, ruined, denied, and the occasional "
        "cruel fake-out</li>"
        "</ul>"
        "Load up your folder and see how long you actually last."
    ),
    "0.2.0": (
        "GoonerApp got dressed up for you - dark, neon, and dripping in pink."
        "<ul>"
        "<li>A full cyber-erotic theme glow-up: deep purple backgrounds, glowing pink "
        "accents, curves everywhere</li>"
        "<li>Settings reorganized into tabs so nothing falls off your screen mid-session</li>"
        "<li>A one-click \"Reset to defaults\" on every tab, no fumbling required</li>"
        "<li>This very \"What's New\" popup, so you never miss what's new to play with</li>"
        "</ul>"
        "Go on, undress the new Settings menu and see for yourself."
    ),
    "0.3.0": (
        "GoonerApp hands you the sticks - build your own rhythm from scratch."
        "<ul>"
        "<li>A brand-new Pattern Editor: drag each step's bar to set how long it lingers, "
        "1 (longest) to 4 (shortest)</li>"
        "<li>A dedicated Beat/Pause button per step, so a pause can tease just as long "
        "(or as short) as you want it to</li>"
        "<li>Live preview playback, so you can hear exactly what you're building before "
        "you commit to it</li>"
        "<li>Your own patterns sit right alongside the built-in ones in the Active Rhythms "
        "list, ready to be picked at random</li>"
        "</ul>"
        "Head to Settings > Beat &amp; Rhythm > Manage Custom Patterns and compose your own edge."
    ),
    "0.4.0": (
        "GoonerApp now remembers every session you've ever survived - and isn't shy about it."
        "<ul>"
        "<li>Break a personal best and the end-of-session recap throws up a glowing "
        "\"New Personal Record\" card for it, right on the spot</li>"
        "<li>A new Statistics menu holds your full history: sessions played, all-time bests, "
        "and a trend chart charting how your stamina climbs over time</li>"
        "<li>Fakeouts now count toward something - survive enough of them and that's a record "
        "too</li>"
        "</ul>"
        "Check the Statistics menu after your next session and watch the line go up."
    ),
    "0.5.0": (
        "GoonerApp finally lets you window-shop before you commit."
        "<ul>"
        "<li>A brand-new folder picker replaces the plain Windows dialog - stack up as many "
        "folders as you want in one go, each weighted equally in the preview</li>"
        "<li>A live thumbnail grid teases a random sample before you hit Start, so you're "
        "never loading in blind again</li>"
        "<li>Gifs already play right in the grid, and videos can too - flip on \"Animate "
        "video clips\" for a looping 5-second peek at each one</li>"
        "<li>Your last-used folders are remembered, ready the next time you open the picker</li>"
        "</ul>"
        "Open the folder picker and see what's waiting for you before you dive in."
    ),
    "0.5.1": (
        "GoonerApp wants to stay close, and finally tells you how it all works."
        "<ul>"
        "<li>A new Socials menu with a one-click Join Discord, so the community is never "
        "more than a click away</li>"
        "<li>A brand-new Guide under Help - a plain-language breakdown of what the beat "
        "pattern numbers actually mean, straight from the source</li>"
        "<li>The Guide also walks you through adding a new callout language, no Python "
        "required</li>"
        "<li>Built to grow - more guide topics will slot in right alongside these as the app "
        "gets bigger</li>"
        "</ul>"
        "Pop open Help &gt; Guide and finally learn what makes this thing tick."
    ),
    "0.6.0": (
        "GoonerApp levels up on three fronts at once - discoverability, control, and trust."
        "<ul>"
        "<li>Every keyboard shortcut is finally documented - a new Keyboard Shortcuts tab in "
        "the Guide, plus the README, list them all in one place</li>"
        "<li>Two shortcuts that were missing outright: Ctrl+O opens/changes your folder, and "
        "F1 pops the Guide open instantly</li>"
        "<li>A new Mute button (or just hit M) silences the beat sound and video audio "
        "together, instantly</li>"
        "<li>Panic Mode - tap Space and the window minimizes and goes silent in one move, no "
        "questions asked, no session lost</li>"
        "<li>A new Privacy tab in the Guide (and right at the top of the README) spells it "
        "out plainly: everything runs 100% locally, nothing ever phones home</li>"
        "</ul>"
        "Pop open Help &gt; Guide - it's got a lot more to say for itself now."
    ),
}


def parse_version(version: str) -> tuple[int, ...]:
    return tuple(int(part) for part in version.split("."))


def entries_since(last_seen_version: str, current_version: str, changelog: dict | None = None) -> dict:
    changelog = CHANGELOG if changelog is None else changelog
    last_seen = parse_version(last_seen_version) if last_seen_version else (0,)
    current = parse_version(current_version)
    return {
        version: text
        for version, text in changelog.items()
        if last_seen < parse_version(version) <= current
    }
