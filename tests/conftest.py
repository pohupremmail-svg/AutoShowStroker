import os

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

import pytest  # noqa: E402
from PyQt6.QtCore import QSettings  # noqa: E402
from PyQt6.QtWidgets import QDialog, QMessageBox  # noqa: E402

from src.BeatHandler import BeatHandler  # noqa: E402
from src.GoonerApp import GoonerApp  # noqa: E402
from src.user_data import UserDataStore  # noqa: E402


class _FakeSoundEffect:
    """Stands in for QSoundEffect so tests don't touch the real audio backend.

    A real QSoundEffect reliably stalls the Qt event loop for every test that
    runs after it in the same session - do not remove this stub.
    """

    def setSource(self, _url):
        pass

    def setVolume(self, _volume):
        pass

    def play(self):
        pass

    def setMuted(self, muted):
        self.muted = muted


@pytest.fixture(autouse=True)
def _no_real_audio(monkeypatch):
    monkeypatch.setattr(
        BeatHandler,
        "init_beat_sound",
        lambda self, _file_path: setattr(self, "sound_effect", _FakeSoundEffect()),
    )


@pytest.fixture(autouse=True)
def _no_modal_dialogs(monkeypatch):
    """QDialog.exec() blocks on a real modal event loop - never let a test hit it."""
    monkeypatch.setattr(QDialog, "exec", lambda self: None)
    # QMessageBox overrides exec() itself rather than inheriting QDialog's - patch it too.
    monkeypatch.setattr(QMessageBox, "exec", lambda self: None)
    # The static shortcuts (information/warning/...) build and run their own box down in
    # C++, so patching exec() above does not reach them: a test that trips one hangs the
    # whole suite on a modal loop with nothing to click. question() answers No, so a test
    # can never silently confirm a destructive action it did not mean to.
    for name, answer in (
        ("information", QMessageBox.StandardButton.Ok),
        ("warning", QMessageBox.StandardButton.Ok),
        ("critical", QMessageBox.StandardButton.Ok),
        ("about", None),
        ("question", QMessageBox.StandardButton.No),
    ):
        monkeypatch.setattr(
            QMessageBox, name, staticmethod(lambda *args, _answer=answer, **kwargs: _answer)
        )


@pytest.fixture
def qsettings(tmp_path):
    return QSettings(str(tmp_path / "settings.ini"), QSettings.Format.IniFormat)


@pytest.fixture
def data_store(tmp_path):
    """Same reasoning as qsettings: never let a test touch the user's real data.

    UserDataStore defaults to QStandardPaths' AppDataLocation, which resolves through the
    QApplication's applicationName - and pytest-qt sets that itself, so an un-injected
    store would write session history into a real directory.
    """
    return UserDataStore(base_dir=tmp_path / "appdata")


@pytest.fixture
def app(qtbot, qsettings, data_store):
    window = GoonerApp(settings=qsettings, data_store=data_store)
    qtbot.addWidget(window)
    return window
