import json

import pytest
from PyQt6.QtCore import QSettings
from PyQt6.QtWidgets import QFileDialog

from src.CalloutHandler import CalloutHandler
from src.CustomPhraseFilesDialog import CustomPhraseFilesDialog


@pytest.fixture
def callout_dir(tmp_path):
    from src.CalloutHandler import TRIGGER_KEYS
    en = {key: [f"en {key} phrase"] for key in TRIGGER_KEYS}
    (tmp_path / "en.json").write_text(json.dumps(en), encoding="utf-8")
    return tmp_path


@pytest.fixture
def callout_handler(qapp, callout_dir, monkeypatch, tmp_path):
    import src.CalloutHandler as callout_module
    monkeypatch.setattr(callout_module, "get_resource_path", lambda _relative_path: str(callout_dir))
    settings = QSettings(str(tmp_path / "settings.ini"), QSettings.Format.IniFormat)
    return CalloutHandler(settings=settings)


@pytest.fixture
def dialog(callout_handler, qtbot):
    d = CustomPhraseFilesDialog(callout_handler)
    qtbot.addWidget(d)
    return d


def _write_custom_file(tmp_path, name, data):
    path = tmp_path / name
    path.write_text(json.dumps(data), encoding="utf-8")
    return str(path)


def test_starts_with_empty_list(dialog):
    assert dialog.file_list.count() == 0


def test_add_file_loads_it_and_updates_list(dialog, callout_handler, tmp_path, monkeypatch):
    custom_path = _write_custom_file(tmp_path, "custom.json", {"session_started": ["custom phrase"]})
    monkeypatch.setattr(QFileDialog, "getOpenFileName", lambda *a, **k: (custom_path, "JSON Files (*.json)"))
    dialog.lang_combo.setCurrentText("en")

    dialog._on_add_file()

    assert callout_handler.callout_data["en"]["session_started"] == ["en session_started phrase", "custom phrase"]
    assert dialog.file_list.count() == 1
    assert "en" in dialog.file_list.item(0).text()
    assert custom_path in dialog.file_list.item(0).text()


def test_add_file_cancelled_is_noop(dialog, callout_handler, monkeypatch):
    monkeypatch.setattr(QFileDialog, "getOpenFileName", lambda *a, **k: ("", ""))

    dialog._on_add_file()

    assert dialog.file_list.count() == 0
    assert callout_handler.custom_phrase_files == []


def test_add_file_invalid_shows_error_and_does_not_add(dialog, callout_handler, tmp_path, monkeypatch):
    bad_path = tmp_path / "bad.json"
    bad_path.write_text("{not valid json", encoding="utf-8")
    monkeypatch.setattr(QFileDialog, "getOpenFileName", lambda *a, **k: (str(bad_path), "JSON Files (*.json)"))
    dialog.lang_combo.setCurrentText("en")

    dialog._on_add_file()

    assert dialog.error_label.text() != ""
    assert dialog.file_list.count() == 0
    assert callout_handler.custom_phrase_files == []


def test_remove_selected_unloads_file(dialog, callout_handler, tmp_path, monkeypatch):
    custom_path = _write_custom_file(tmp_path, "custom.json", {"session_started": ["custom phrase"]})
    monkeypatch.setattr(QFileDialog, "getOpenFileName", lambda *a, **k: (custom_path, "JSON Files (*.json)"))
    dialog.lang_combo.setCurrentText("en")
    dialog._on_add_file()
    dialog.file_list.setCurrentRow(0)

    dialog._on_remove_selected()

    assert dialog.file_list.count() == 0
    assert callout_handler.custom_phrase_files == []
    assert callout_handler.callout_data["en"]["session_started"] == ["en session_started phrase"]


def test_close_button_accepts_dialog(dialog):
    assert dialog.close_button.text() == "Close"
    dialog.close_button.click()
    assert dialog.result() == CustomPhraseFilesDialog.DialogCode.Accepted
