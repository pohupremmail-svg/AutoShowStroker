from pathlib import Path

import pytest

from src.PrivacyDataDialog import PrivacyDataDialog


@pytest.fixture
def dialog(app, qtbot):
    d = PrivacyDataDialog(app, parent=app)
    qtbot.addWidget(d)
    return d


@pytest.fixture
def confirmed(dialog, monkeypatch):
    """Auto-confirms the deletion prompt and records what it was asked about."""
    asked = {}
    monkeypatch.setattr(dialog, "_confirm_deletion", lambda keys: asked.setdefault("keys", keys) or True)
    return asked


def _populate(app, tmp_path):
    app.score_tracker.history = [{"total_dur_sec": 60}, {"total_dur_sec": 30}]
    app.data_store.save("session_history", app.score_tracker.history)
    app.beat_handler.add_or_update_custom_pattern("Mine", [1, -1])
    app.data_store.save("last_selected_folders", [str(tmp_path)])


# --- disclosure ---


def test_dialog_names_the_real_data_directory(app, dialog):
    assert str(app.data_store.base_dir) in dialog.locations_text()


def test_dialog_names_where_settings_live(app, dialog):
    assert app.settings.fileName() in dialog.locations_text()


def test_open_data_folder_opens_the_store_directory(app, dialog, monkeypatch):
    opened = {}
    monkeypatch.setattr(dialog, "_open_url", lambda url: opened.setdefault("url", url))

    dialog.btn_open_folder.click()

    # QUrl.toLocalFile() normalises to forward slashes on Windows - compare as paths.
    assert Path(opened["url"].toLocalFile()) == app.data_store.base_dir


# --- counts ---


def test_counts_reflect_what_is_actually_stored(app, dialog, tmp_path):
    _populate(app, tmp_path)
    dialog.refresh_counts()

    counts = dialog.category_counts()
    assert counts["session_history"] == 2
    assert counts["custom_patterns"] == 1
    assert counts["last_selected_folders"] == 1


def test_counts_are_zero_on_a_clean_profile(dialog):
    assert all(count == 0 for key, count in dialog.category_counts().items() if key != "settings")


# --- deleting ---


def test_clearing_session_history_empties_it_in_memory_and_on_disk(app, dialog, tmp_path):
    _populate(app, tmp_path)

    dialog.clear_categories(["session_history"])

    assert app.score_tracker.get_history() == []
    assert not app.data_store.path_for("session_history").exists()


def test_clearing_custom_patterns_removes_them_everywhere(app, dialog, tmp_path):
    _populate(app, tmp_path)

    dialog.clear_categories(["custom_patterns"])

    assert "Mine" not in app.beat_handler.available_beat_patterns
    assert "Mine" not in app.beat_handler.selected_beat_patterns
    assert not app.data_store.path_for("custom_patterns").exists()
    # The built-ins must survive - clearing custom data is not a rhythm reset.
    assert "Standard Beat" in app.beat_handler.available_beat_patterns


def test_clearing_custom_phrase_files_forgets_them(app, dialog, tmp_path):
    phrase_file = tmp_path / "extra.json"
    phrase_file.write_text('{"session_start": ["hello"]}', encoding="utf-8")
    app.callout_handler.load_custom_file(str(phrase_file), "en")
    assert app.callout_handler.custom_phrase_files

    dialog.clear_categories(["custom_phrase_files"])

    assert app.callout_handler.custom_phrase_files == []
    assert not app.data_store.path_for("custom_phrase_files").exists()
    assert "hello" not in app.callout_handler.callout_data["en"].get("session_start", [])


def test_clearing_media_folders_removes_the_stored_paths(app, dialog, tmp_path):
    _populate(app, tmp_path)

    dialog.clear_categories(["last_selected_folders"])

    assert not app.data_store.path_for("last_selected_folders").exists()
    assert app.data_store.load("last_selected_folders", []) == []


def test_clearing_settings_wipes_them_but_keeps_the_data_files(app, dialog, tmp_path):
    _populate(app, tmp_path)
    app.settings.setValue("GoonerApp/min_dur", 2.5)

    dialog.clear_categories(["settings"])

    assert app.settings.value("GoonerApp/min_dur") is None
    assert app.data_store.path_for("session_history").exists()


def test_clearing_one_category_leaves_the_others_alone(app, dialog, tmp_path):
    _populate(app, tmp_path)

    dialog.clear_categories(["session_history"])

    assert app.data_store.path_for("custom_patterns").exists()
    assert app.data_store.path_for("last_selected_folders").exists()


def test_delete_button_clears_every_ticked_category(app, dialog, confirmed, tmp_path):
    _populate(app, tmp_path)
    dialog.refresh_counts()
    dialog.checkboxes["session_history"].setChecked(True)
    dialog.checkboxes["last_selected_folders"].setChecked(True)

    dialog.btn_delete.click()

    assert set(confirmed["keys"]) == {"session_history", "last_selected_folders"}
    assert not app.data_store.path_for("session_history").exists()
    assert not app.data_store.path_for("last_selected_folders").exists()
    assert app.data_store.path_for("custom_patterns").exists()


def test_delete_button_does_nothing_when_the_prompt_is_declined(app, dialog, monkeypatch, tmp_path):
    _populate(app, tmp_path)
    monkeypatch.setattr(dialog, "_confirm_deletion", lambda _keys: False)
    dialog.checkboxes["session_history"].setChecked(True)

    dialog.btn_delete.click()

    assert app.data_store.path_for("session_history").exists()


def test_delete_button_is_disabled_until_something_is_ticked(dialog):
    assert dialog.btn_delete.isEnabled() is False

    dialog.checkboxes["session_history"].setChecked(True)
    assert dialog.btn_delete.isEnabled() is True

    dialog.checkboxes["session_history"].setChecked(False)
    assert dialog.btn_delete.isEnabled() is False


def test_counts_refresh_after_a_deletion(app, dialog, confirmed, tmp_path):
    _populate(app, tmp_path)
    dialog.refresh_counts()
    dialog.checkboxes["session_history"].setChecked(True)

    dialog.btn_delete.click()

    assert dialog.category_counts()["session_history"] == 0


def test_deleting_also_removes_quarantined_and_temp_leftovers(app, dialog):
    """A .corrupt or .tmp sibling holds the same data - leaving it behind would make
    'delete my data' a lie."""
    app.data_store.save("session_history", [{"total_dur_sec": 1}])
    path = app.data_store.path_for("session_history")
    path.with_suffix(".json.corrupt").write_text("[]", encoding="utf-8")
    path.with_suffix(".json.tmp").write_text("[]", encoding="utf-8")

    dialog.clear_categories(["session_history"])

    assert not path.exists()
    assert not path.with_suffix(".json.corrupt").exists()
    assert not path.with_suffix(".json.tmp").exists()


# --- the diagnostic log is user data too ---


def test_the_log_is_offered_as_its_own_category(dialog):
    assert "diagnostic_log" in dialog.checkboxes


def test_the_log_count_reflects_the_files_on_disk(app, dialog):
    from src import applog

    app.set_diagnostic_log(True)
    applog.get_logger("src.Test").info("a line")
    dialog.refresh_counts()

    assert dialog.category_counts()["diagnostic_log"] == 1
    app.set_diagnostic_log(False)


def test_clearing_the_log_removes_it(app, dialog):
    from src import applog

    app.set_diagnostic_log(True)
    applog.get_logger("src.Test").info("a line")

    dialog.clear_categories(["diagnostic_log"])

    assert applog.log_file_paths(app.data_store.base_dir) == []
    app.set_diagnostic_log(False)


def test_clearing_the_log_leaves_logging_working(app, dialog):
    from src import applog

    app.set_diagnostic_log(True)
    applog.get_logger("src.Test").info("before")

    dialog.clear_categories(["diagnostic_log"])
    applog.get_logger("src.Test").info("after")

    contents = applog.log_file_path(app.data_store.base_dir).read_text(encoding="utf-8")
    assert "after" in contents
    assert "before" not in contents
    app.set_diagnostic_log(False)


# --- the log is controlled here, next to where it is disclosed and deleted ---


def test_the_log_toggle_lives_in_this_dialog(app, dialog):
    assert dialog.diagnostic_log_checkbox.isChecked() == app.diagnostic_log


def test_ticking_the_toggle_starts_logging_immediately(app, dialog):
    """No Save button in this dialog - Open folder and Delete act at once, so this does too."""
    from src import applog

    dialog.diagnostic_log_checkbox.setChecked(True)

    applog.get_logger("src.Test").info("recorded right away")

    assert app.diagnostic_log is True
    assert "recorded right away" in applog.log_file_path(app.data_store.base_dir).read_text(encoding="utf-8")
    app.set_diagnostic_log(False)


def test_unticking_the_toggle_stops_logging_immediately(app, dialog):
    from src import applog

    dialog.diagnostic_log_checkbox.setChecked(True)
    dialog.diagnostic_log_checkbox.setChecked(False)

    applog.get_logger("src.Test").info("must not be recorded")

    assert app.diagnostic_log is False
    # The file stays - switching off stops writing, it does not delete what was already
    # recorded (that is what the Delete section is for).
    contents = applog.log_file_path(app.data_store.base_dir).read_text(encoding="utf-8")
    assert "must not be recorded" not in contents


def test_the_level_dropdown_offers_exactly_the_supported_levels(dialog):
    from src import applog

    items = [dialog.diagnostic_log_level.itemData(i) for i in range(dialog.diagnostic_log_level.count())]

    assert tuple(items) == applog.LEVELS


def test_the_level_dropdown_starts_on_the_saved_level(app, qtbot):
    from src.PrivacyDataDialog import PrivacyDataDialog

    app.set_diagnostic_log(app.diagnostic_log, level="ERROR")
    reopened = PrivacyDataDialog(app, parent=app)
    qtbot.addWidget(reopened)

    assert reopened.diagnostic_log_level.currentData() == "ERROR"
    app.set_diagnostic_log(False, level="INFO")


def test_choosing_a_level_applies_and_persists_it(app, dialog):
    from src import applog

    dialog.diagnostic_log_checkbox.setChecked(True)
    index = dialog.diagnostic_log_level.findData("WARNING")
    dialog.diagnostic_log_level.setCurrentIndex(index)

    applog.get_logger("src.Test").info("below the threshold")
    applog.get_logger("src.Test").warning("at the threshold")

    contents = applog.log_file_path(app.data_store.base_dir).read_text(encoding="utf-8")
    assert "below the threshold" not in contents
    assert "at the threshold" in contents
    assert app.settings.value("GoonerApp/diagnostic_log_level") == "WARNING"
    app.set_diagnostic_log(False, level="INFO")


def test_the_level_dropdown_is_disabled_while_logging_is_off(dialog):
    assert dialog.diagnostic_log_checkbox.isChecked() is False
    assert dialog.diagnostic_log_level.isEnabled() is False

    dialog.diagnostic_log_checkbox.setChecked(True)
    assert dialog.diagnostic_log_level.isEnabled() is True


# --- saved sessions ---


def test_saved_sessions_are_counted(app, dialog):
    from src import session_files

    session_files.store_session(app.data_store, {"format": 1, "segments": [], "media": []})
    dialog.refresh_counts()

    assert dialog.category_counts()["saved_sessions"] == 1


def test_saved_sessions_can_be_deleted(app, dialog):
    from src import session_files

    session_files.store_session(app.data_store, {"format": 1, "segments": [], "media": []})

    dialog.clear_categories(["saved_sessions"])

    assert session_files.load_saved_sessions(app.data_store) == []


def test_the_saved_sessions_entry_says_it_holds_media_paths(dialog):
    """This is the one category that carries paths into the data directory, and a user
    deciding what to wipe before handing the laptop over needs to know which one that is."""
    description = next(
        text for key, _label, text in dialog.CATEGORIES if key == "saved_sessions"
    )

    assert "path" in description.lower()


def test_achievements_are_counted_and_deletable(app, dialog):
    app.achievement_tracker.unlocked["endurance_45"] = "2026-09-17 21:00"
    app.achievement_tracker._save()
    dialog.refresh_counts()
    assert dialog.category_counts()["achievements"] == 1

    dialog.clear_categories(["achievements"])

    assert app.achievement_tracker.unlocked == {}
    assert dialog.category_counts()["achievements"] == 0
