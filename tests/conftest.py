import os

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

import pytest  # noqa: E402

from src.BeatHandler import BeatHandler  # noqa: E402


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


@pytest.fixture(autouse=True)
def _no_real_audio(monkeypatch):
    monkeypatch.setattr(
        BeatHandler,
        "init_beat_sound",
        lambda self, _file_path: setattr(self, "sound_effect", _FakeSoundEffect()),
    )
