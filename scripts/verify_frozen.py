#!/usr/bin/env python
"""Open dist/GoonerApp.exe and check that what should be inside it actually is.

"It built" proves very little. The parts most likely to be missing are exactly the ones
import analysis cannot see: the device plugin is looked up by name at runtime, and
QtWebSockets is used only by that plugin. A build has already shipped without both of them,
with nothing to show for it but a line in the log - so this exists to make that loud.

It also compares the bundled VERSION against the repo's, which catches the other easy
mistake: publishing yesterday's executable.

Reads the archive rather than launching anything, so it is quick and needs no display.

Usage:
    .venv\\Scripts\\python.exe scripts/verify_frozen.py [path-to-exe]
"""
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# Modules PyInstaller cannot reach by following imports. Every optional plugin lives here;
# tests/test_verify_frozen.py fails if a file under src/plugins/ is missing from this list,
# because the failure mode is a plugin that quietly is not in the .exe.
REQUIRED_MODULES = (
    "src.plugins",
    "src.plugins.intiface",
    "src.plugins.intiface.beat_sync",
    "src.plugins.intiface.consent",
    "src.plugins.intiface.controller",
    "src.plugins.intiface.plugin",
    "src.plugins.intiface.settings_widget",
    "src.plugins.intiface.status_button",
)

# Binaries no import analysis pulls in, each with what breaks when it is absent.
REQUIRED_BINARIES = (
    ("Qt6WebSockets", "device output cannot connect to anything"),
    ("QtSvg", "the achievement marks cannot be drawn"),
)

# Bundled data, counted against what is on disk so a new language or a new mark cannot be
# left out of the build without anyone noticing.
BUNDLED_TREES = (
    ("res/callouts", "*.json", "callout files"),
    ("res/icons/achievements", "*.svg", "achievement marks"),
    ("res/licenses", "*.txt", "third-party license texts"),
)


def _read_archive(exe_path):
    from PyInstaller.archive.readers import CArchiveReader

    reader = CArchiveReader(str(exe_path))
    names = {name.replace("\\", "/") for name in reader.toc}
    modules = set(reader.open_embedded_archive("PYZ.pyz").toc)
    return reader, names, modules


def _bundled_version(reader, names):
    """The VERSION file as the .exe carries it, or None if it cannot be read."""
    entry = next((name for name in names if name.rsplit("/", 1)[-1] == "VERSION"), None)
    if entry is None:
        return None
    try:
        data = reader.extract(entry)
    except Exception:
        return None
    if isinstance(data, tuple):          # older readers hand back (flag, data)
        data = data[-1]
    try:
        return data.decode("utf-8").strip()
    except (AttributeError, UnicodeDecodeError):
        return None


def check(exe_path) -> list[str]:
    """Every problem found, in plain language. Empty means the build looks shippable."""
    exe_path = Path(exe_path)
    if not exe_path.exists():
        return [f"{exe_path} does not exist - build it first with scripts/build.py"]

    problems = []
    reader, names, modules = _read_archive(exe_path)

    for module in REQUIRED_MODULES:
        if module not in modules:
            problems.append(f"module missing from the .exe: {module}")

    for needle, consequence in REQUIRED_BINARIES:
        if not any(needle.lower() in name.lower() for name in names):
            problems.append(f"{needle} is not bundled - {consequence}")

    expected_version = (ROOT / "VERSION").read_text(encoding="utf-8").strip()
    bundled = _bundled_version(reader, names)
    if bundled is None:
        problems.append("VERSION is not bundled - the What's New popup cannot work")
    elif bundled != expected_version:
        problems.append(
            f"the .exe carries VERSION {bundled} but the repo says {expected_version}"
            " - this is an old build"
        )

    for folder, pattern, label in BUNDLED_TREES:
        on_disk = len(list((ROOT / folder).glob(f"**/{pattern}")))
        bundled_count = sum(
            1 for name in names
            if folder in name and name.endswith(pattern.lstrip("*"))
        )
        if bundled_count != on_disk:
            problems.append(
                f"{label}: {bundled_count} in the .exe, {on_disk} on disk"
            )

    return problems


def main() -> None:
    exe_path = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "dist" / "GoonerApp.exe"
    problems = check(exe_path)
    if problems:
        print(f"{exe_path.name} is NOT ready to ship:")
        for problem in problems:
            print(f"  - {problem}")
        raise SystemExit(1)
    print(f"{exe_path.name} looks complete: plugins, Qt binaries, VERSION and bundled data all present.")


if __name__ == "__main__":
    main()
