"""User-facing changelog, shown in the in-app "What's New" popup.

Every `feat:` (or critical `fix:`) version bump must add an entry here describing what's
new, in plain language for the end user - not a commit-log dump. See CLAUDE.md's
"Versioning & Releases" section.
"""

CHANGELOG = {
    "0.1.0": (
        "First public build of GoonerApp! Randomized slideshow playback synced to a "
        "configurable Strokemeter, multilingual teasing callouts (English, German, French), "
        "difficulty ramping that builds intensity over the course of a session, and a full "
        "climax announcement system with real, ruined, and denied outcomes - plus the "
        "occasional fake-out."
    ),
    "0.2.0": (
        "Brand new look: a neon cyber-erotic theme across the whole app, designed to match "
        "the logo - dark purple backgrounds, glowing pink accents, rounded corners "
        "everywhere. Settings got reorganized into tabs so nothing falls off your screen "
        "anymore, and every tab now has its own \"Reset to defaults\" button. And you're "
        "reading the result of this update right now: a \"What's New\" popup, shown once per "
        "update and always available from the Help menu."
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
