import pytest
from PyQt6.QtWidgets import QLabel

from src.WhatsNewDialog import WhatsNewDialog

ENTRIES = {
    "0.1.0": "First release text.",
    "0.2.0": "Second release text.",
}


@pytest.fixture
def dialog(qtbot):
    d = WhatsNewDialog(ENTRIES)
    qtbot.addWidget(d)
    return d


def test_shows_a_label_per_version(dialog):
    text = " ".join(w.text() for w in dialog.findChildren(QLabel))
    assert "0.1.0" in text
    assert "First release text." in text
    assert "0.2.0" in text
    assert "Second release text." in text


def test_entries_appear_newest_first(dialog):
    version_labels = [w.text() for w in dialog.findChildren(QLabel) if w.text().startswith("Version ")]
    assert version_labels == ["Version 0.2.0", "Version 0.1.0"]


def test_got_it_button_accepts_dialog(dialog):
    assert dialog.button.text() == "Got it"
    dialog.button.click()
    assert dialog.result() == WhatsNewDialog.DialogCode.Accepted
