import pytest
from PyQt6.QtWidgets import QLabel

from src.HelpDialog import HelpDialog


@pytest.fixture
def dialog(qtbot):
    d = HelpDialog()
    qtbot.addWidget(d)
    return d


def test_has_a_tab_per_help_topic(dialog):
    titles = [dialog.tabs.tabText(i) for i in range(dialog.tabs.count())]
    assert any("Beats" in t and "Rhythm" in t for t in titles)
    assert any("Language" in t for t in titles)


def test_beats_tab_explains_pattern_number_meaning(dialog):
    beats_tab = dialog.tabs.widget(0)
    text = " ".join(w.text() for w in beats_tab.findChildren(QLabel))
    assert "audible beat" in text
    assert "silent step" in text
    assert "1 is the longest" in text
    assert "4 is the shortest" in text


def test_languages_tab_explains_adding_a_language(dialog):
    languages_tab = dialog.tabs.widget(1)
    text = " ".join(w.text() for w in languages_tab.findChildren(QLabel))
    assert "res/callouts/" in text
    assert "Trigger Key" in text


def test_shortcuts_tab_lists_every_shortcut(dialog):
    titles = [dialog.tabs.tabText(i) for i in range(dialog.tabs.count())]
    assert "Keyboard Shortcuts" in titles
    shortcuts_tab = dialog.tabs.widget(titles.index("Keyboard Shortcuts"))
    text = " ".join(w.text() for w in shortcuts_tab.findChildren(QLabel))
    assert "Ctrl+O" in text
    assert "Right Arrow" in text
    assert "Left Arrow" in text
    assert "Ctrl+Space" in text
    assert "F11" in text
    assert "Escape" in text
    assert "Ctrl+S" in text
    assert "F1" in text
    assert "Ctrl+Q" in text


def test_close_button_accepts_dialog(dialog):
    assert dialog.button.text() == "Close"
    dialog.button.click()
    assert dialog.result() == HelpDialog.DialogCode.Accepted
