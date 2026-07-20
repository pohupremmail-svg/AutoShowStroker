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
    "0.6.1": (
        "GoonerApp finally makes an entrance instead of just barging in."
        "<ul>"
        "<li>A glowing logo fade-in greets you on launch now - the neon mark easing in, "
        "holding for a beat, then dissolving away right before your session starts</li>"
        "<li>Not in the mood for a tease before the tease? A new \"Show startup splash "
        "animation\" toggle in Settings &gt; Playback turns it off instantly</li>"
        "</ul>"
        "Watch it once, then decide if you want the intro every time."
    ),
    "0.6.2": (
        "GoonerApp now taunts you the moment a record is actually within reach."
        "<ul>"
        "<li>A glowing badge flashes up in the corner once you're closing in on a personal "
        "best - total beats, duration, active speed, or fakeouts survived, whichever you're "
        "nearest to breaking</li>"
        "<li>Cross the line and it flips straight to \"New Record!\" without waiting for the "
        "recap screen</li>"
        "<li>Stays out of your way otherwise - no badge, no clutter, until you're actually "
        "close</li>"
        "<li>A new \"Show live personal-record chase\" toggle in Settings &gt; Playback turns "
        "it off if you'd rather be surprised at the end</li>"
        "</ul>"
        "Keep playing - it'll let you know when it's time to get excited."
    ),
    "0.6.3": (
        "GoonerApp can finally tell you when it's grown - if you ask nicely first."
        "<ul>"
        "<li>A new \"Check for Updates...\" action under Help asks GitHub, once, whether a "
        "newer version exists</li>"
        "<li>Always asks permission first - a clear warning pops up every single time before "
        "anything gets sent, no exceptions</li>"
        "<li>Nothing automatic, nothing silent - no background checks, no Settings toggle to "
        "forget about, just a button you press when you're curious</li>"
        "<li>Finds one? A one-click link straight to the Releases page</li>"
        "</ul>"
        "Pop open Help when you're curious - it'll only ever speak up if you ask."
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
