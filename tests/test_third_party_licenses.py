"""The third-party notices that ship with the build, and what keeps them honest.

GoonerApp.exe carries other people's software - Python itself, Qt, the FFmpeg Qt plays video
with, PyQt6, numpy, pyqtgraph - and several of their licenses only allow that when the license
text and the notices travel with the binary. res/licenses/ is bundled like the rest of res/, and
Help > Third-Party Licenses shows it, because a --onefile build has no folder anyone could open.

The notices are checked against the build's own inputs, so a version bump or a new dependency
fails here until the notices say so - the same reasoning as test_session_format_doc.py.
"""
import re
from pathlib import Path

import pytest

from scripts import verify_frozen
from src.utils import get_project_root

ROOT = get_project_root()
LICENSES = ROOT / "res" / "licenses"
NOTICES = LICENSES / "THIRD_PARTY_NOTICES.txt"


def _notices():
    return NOTICES.read_text(encoding="utf-8")


def _pinned_requirements():
    pins = []
    for line in (ROOT / "requirements.txt").read_text(encoding="utf-8").splitlines():
        line = line.split("#", 1)[0].strip()
        if line:
            pins.append(line)
    return pins


def _license_files_named():
    return {
        name.strip()
        for line in _notices().splitlines() if line.startswith("License text:")
        for name in line.removeprefix("License text:").split(",")
    }


@pytest.mark.parametrize("pin", _pinned_requirements())
def test_every_pinned_requirement_is_named_at_its_version(pin):
    """A bump of a pin has to bump the notice with it, or it describes a different build."""
    assert f"Package: {pin}" in _notices(), f"{pin} is bundled but not in {NOTICES.name}"


def test_every_license_text_named_is_there():
    missing = [name for name in _license_files_named() if not (LICENSES / name).is_file()]

    assert not missing, f"named in the notices but not in res/licenses: {missing}"


def test_no_license_text_is_left_unnamed():
    unnamed = {path.name for path in LICENSES.glob("*.txt")} - {NOTICES.name} - _license_files_named()

    assert not unnamed, f"in res/licenses but named by no notice: {unnamed}"


def _qt_ffmpeg_libraries():
    import PyQt6

    qt_bin = Path(PyQt6.__file__).parent / "Qt6" / "bin"
    return sorted(qt_bin.glob("av*.dll")) + sorted(qt_bin.glob("sw*.dll"))


def test_the_ffmpeg_qt_bundles_is_named_library_by_library():
    """Qt Multimedia plays video through FFmpeg, and PyInstaller bundles it from the Qt wheel.
    Named by file, so a Qt update that moves to another FFmpeg shows up here."""
    libraries = _qt_ffmpeg_libraries()

    assert libraries, "the Qt wheel no longer ships FFmpeg - the notice should go"
    for library in libraries:
        assert library.name in _notices(), f"{library.name} is bundled but not named"


def test_the_ffmpeg_qt_bundles_is_an_lgpl_build():
    """What makes shipping it fine: an LGPL build. FFmpeg built with GPL parts would oblige more,
    and a "nonfree" build may not be passed on at all - a Qt update must not slip that past."""
    avcodec = next(lib for lib in _qt_ffmpeg_libraries() if lib.name.startswith("avcodec"))
    license_strings = set(re.findall(
        rb"(?:LGPL|GPL) version [0-9.]+ or later|nonfree and unredistributable", avcodec.read_bytes()
    ))

    assert license_strings == {b"LGPL version 2.1 or later"}


def test_the_frozen_build_is_checked_for_the_license_texts():
    """Left out of the build, the notices would exist only in the repo - not where they are owed."""
    assert ("res/licenses", "*.txt") in [(folder, pattern) for folder, pattern, _label in verify_frozen.BUNDLED_TREES]


# --- the dialog ---


@pytest.fixture
def dialog(qtbot):
    from src.ThirdPartyLicensesDialog import ThirdPartyLicensesDialog

    d = ThirdPartyLicensesDialog()
    qtbot.addWidget(d)
    return d


def test_the_dialog_opens_on_the_notices(dialog):
    assert dialog.text_view.toPlainText() == _notices()


def test_every_license_text_can_be_shown(dialog):
    for index in range(1, dialog.chooser.count()):
        dialog.chooser.setCurrentIndex(index)
        expected = (LICENSES / dialog.chooser.itemText(index)).read_text(encoding="utf-8")
        assert dialog.text_view.toPlainText() == expected


def test_the_dialog_lists_every_license_text(dialog):
    listed = {dialog.chooser.itemText(i) for i in range(1, dialog.chooser.count())}

    assert listed == {path.name for path in LICENSES.glob("*.txt")} - {NOTICES.name}


def test_the_text_cannot_be_edited(dialog):
    assert dialog.text_view.isReadOnly()


def test_the_help_menu_opens_the_third_party_licenses(app, monkeypatch):
    from PyQt6.QtWidgets import QMenu

    shown = []

    class FakeDialog:
        def __init__(self, parent=None):
            shown.append(True)

        def exec(self):
            pass

        def deleteLater(self):
            pass

    monkeypatch.setattr("src.ThirdPartyLicensesDialog.ThirdPartyLicensesDialog", FakeDialog)
    help_menu = next(m for m in app.menuBar().findChildren(QMenu) if m.title() == "Help")
    action = next(a for a in help_menu.actions() if a.text() == "Third-Party Licenses...")

    action.trigger()

    assert shown == [True]
