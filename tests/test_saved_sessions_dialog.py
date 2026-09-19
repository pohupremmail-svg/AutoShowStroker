"""The saved-sessions manager: list them, play one again, hand one on, throw one away.

The two things that would open a real window - the file picker and the with-or-without-paths
question - are seams on the dialog (_choose_export_file / _ask_about_paths) that every test
answers directly. Their wording is checked separately, because what an export gives away is
the whole reason the question exists.
"""
import json
import types

import pytest

from src import session_files
from src.SavedSessionsDialog import SavedSessionsDialog
from src.user_data import UserDataStore


def saved(outcome="real", media=None, saved_at="2026-09-01 20:00"):
    return {
        "format": session_files.FORMAT_VERSION,
        "app_version": "0.12.0",
        "saved_at": saved_at,
        "duration_sec": 120.0,
        "segments": [{"kind": "beat", "pattern": "Standard Beat", "freq": 2.0,
                      "duration_sec": 120.0}],
        "custom_patterns": {},
        "climax": {"at_sec": 110.0, "outcome": outcome},
        "fake_climaxes": [],
        "media": [{"at_sec": 0.0}] if media is None else media,
    }


def with_files(tmp_path, count=2, missing=0):
    entries = []
    for index in range(count):
        path = tmp_path / f"m{index}.png"
        path.write_bytes(b"x")
        entries.append({"at_sec": index * 30.0, "path": str(path)})
    for index in range(missing):
        entries.append({"at_sec": 90.0 + index, "path": str(tmp_path / f"gone{index}.png")})
    return entries


class _FakeApp:
    """Only what the dialog actually reaches for."""

    def __init__(self, base_dir):
        self.data_store = UserDataStore(base_dir=base_dir)
        self.replays = []
        self.replay_result = True

    def replay_session(self, session, ignore_paths=False):
        self.replays.append((session, ignore_paths))
        return self.replay_result


@pytest.fixture
def main_app(tmp_path):
    return _FakeApp(tmp_path / "appdata")


@pytest.fixture
def make_dialog(qtbot, main_app):
    def build(sessions=()):
        for entry in sessions:
            session_files.store_session(main_app.data_store, entry)
        dialog = SavedSessionsDialog(main_app, parent=None)
        qtbot.addWidget(dialog)
        return dialog

    return build


# --- the list ---


def test_the_newest_session_is_at_the_top(make_dialog):
    """A shelf of fifty is scrolled from the top, and the one you want is almost always the
    one you just played."""
    dialog = make_dialog([saved(saved_at="first"), saved(saved_at="second")])

    assert dialog.session_list.count() == 2
    assert "second" in dialog.session_list.item(0).text()
    assert "first" in dialog.session_list.item(1).text()


def test_a_session_is_described_rather_than_numbered(make_dialog):
    dialog = make_dialog([saved(outcome="ruined")])

    assert "Ruined" in dialog.session_list.item(0).text()


def test_an_empty_shelf_says_so_and_offers_nothing_to_press(make_dialog):
    dialog = make_dialog([])

    assert dialog.session_list.count() == 0
    assert dialog.replay_button.isEnabled() is False
    assert dialog.export_button.isEnabled() is False
    assert dialog.delete_button.isEnabled() is False


def test_selecting_a_session_enables_what_can_be_done_with_it(make_dialog):
    dialog = make_dialog([saved()])

    dialog.session_list.setCurrentRow(0)

    assert dialog.replay_button.isEnabled() is True
    assert dialog.export_button.isEnabled() is True
    assert dialog.delete_button.isEnabled() is True


# --- replaying ---


def test_replaying_uses_the_recorded_files_when_they_are_all_still_there(
    make_dialog, main_app, tmp_path
):
    dialog = make_dialog([saved(media=with_files(tmp_path))])
    dialog.session_list.setCurrentRow(0)

    dialog.replay_button.click()

    assert len(main_app.replays) == 1
    assert main_app.replays[0][1] is False


def test_replaying_closes_the_manager_so_the_session_is_visible(make_dialog, tmp_path, qtbot):
    dialog = make_dialog([saved(media=with_files(tmp_path))])
    dialog.session_list.setCurrentRow(0)
    dialog.show()
    qtbot.waitExposed(dialog)

    dialog.replay_button.click()

    assert dialog.isVisible() is False


def test_a_session_without_paths_replays_against_your_own_library_without_asking(
    make_dialog, main_app, monkeypatch
):
    """There is nothing to ask about: a stripped session has no files to look for."""
    asked = []
    monkeypatch.setattr(SavedSessionsDialog, "_ask_about_missing_files",
                        lambda self, missing, total: asked.append(True) or True)
    dialog = make_dialog([saved()])
    dialog.session_list.setCurrentRow(0)

    dialog.replay_button.click()

    assert asked == []
    assert main_app.replays[0][1] is True


def test_missing_files_offer_your_own_library_rather_than_failing(
    make_dialog, main_app, tmp_path, monkeypatch
):
    asked = []

    def fake_ask(self, missing, total):
        asked.append((missing, total))
        return True

    monkeypatch.setattr(SavedSessionsDialog, "_ask_about_missing_files", fake_ask)
    dialog = make_dialog([saved(media=with_files(tmp_path, count=2, missing=1))])
    dialog.session_list.setCurrentRow(0)

    dialog.replay_button.click()

    assert asked == [(1, 3)]
    assert main_app.replays[0][1] is True


def test_declining_your_own_library_replays_nothing(make_dialog, main_app, tmp_path, monkeypatch):
    monkeypatch.setattr(SavedSessionsDialog, "_ask_about_missing_files", lambda self, m, t: False)
    dialog = make_dialog([saved(media=with_files(tmp_path, count=1, missing=1))])
    dialog.session_list.setCurrentRow(0)

    dialog.replay_button.click()

    assert main_app.replays == []


def test_a_replay_that_cannot_start_says_why_and_leaves_the_manager_open(
    make_dialog, main_app, tmp_path, monkeypatch, qtbot
):
    """The one way this fails: own-library mode with no folder loaded."""
    warned = []
    monkeypatch.setattr("src.SavedSessionsDialog.QMessageBox.warning",
                        lambda parent, title, text: warned.append(text))
    main_app.replay_result = False
    dialog = make_dialog([saved(media=with_files(tmp_path))])
    dialog.session_list.setCurrentRow(0)
    dialog.show()
    qtbot.waitExposed(dialog)

    dialog.replay_button.click()

    assert len(warned) == 1
    assert dialog.isVisible() is True


# --- exporting ---


def test_exporting_asks_about_the_paths_every_single_time(make_dialog, tmp_path, monkeypatch):
    """Not a remembered preference: the answer depends on who is getting the file."""
    asks = []
    monkeypatch.setattr(SavedSessionsDialog, "_ask_about_paths",
                        lambda self: asks.append(True) or "without")
    monkeypatch.setattr(SavedSessionsDialog, "_choose_export_file",
                        lambda self: tmp_path / "out.json")
    dialog = make_dialog([saved(media=with_files(tmp_path))])
    dialog.session_list.setCurrentRow(0)

    dialog.export_button.click()
    dialog.export_button.click()

    assert len(asks) == 2


def test_an_export_without_paths_carries_none_of_them(make_dialog, tmp_path, monkeypatch):
    target = tmp_path / "out.json"
    monkeypatch.setattr(SavedSessionsDialog, "_ask_about_paths", lambda self: "without")
    monkeypatch.setattr(SavedSessionsDialog, "_choose_export_file", lambda self: target)
    dialog = make_dialog([saved(media=with_files(tmp_path))])
    dialog.session_list.setCurrentRow(0)

    dialog.export_button.click()

    written = target.read_text(encoding="utf-8")
    assert "m0.png" not in written
    assert json.loads(written)["media"][0]["at_sec"] == 0.0


def test_an_export_with_paths_keeps_them(make_dialog, tmp_path, monkeypatch):
    target = tmp_path / "out.json"
    monkeypatch.setattr(SavedSessionsDialog, "_ask_about_paths", lambda self: "with")
    monkeypatch.setattr(SavedSessionsDialog, "_choose_export_file", lambda self: target)
    dialog = make_dialog([saved(media=with_files(tmp_path))])
    dialog.session_list.setCurrentRow(0)

    dialog.export_button.click()

    assert "m0.png" in target.read_text(encoding="utf-8")


def test_cancelling_the_question_writes_nothing(make_dialog, tmp_path, monkeypatch):
    chosen = []
    monkeypatch.setattr(SavedSessionsDialog, "_ask_about_paths", lambda self: None)
    monkeypatch.setattr(SavedSessionsDialog, "_choose_export_file",
                        lambda self: chosen.append(True) or tmp_path / "out.json")
    dialog = make_dialog([saved(media=with_files(tmp_path))])
    dialog.session_list.setCurrentRow(0)

    dialog.export_button.click()

    assert chosen == []  # the question comes first - nothing to pick a file for
    assert not (tmp_path / "out.json").exists()


def test_cancelling_the_file_picker_writes_nothing(make_dialog, tmp_path, monkeypatch):
    monkeypatch.setattr(SavedSessionsDialog, "_ask_about_paths", lambda self: "with")
    monkeypatch.setattr(SavedSessionsDialog, "_choose_export_file", lambda self: None)
    dialog = make_dialog([saved(media=with_files(tmp_path))])
    dialog.session_list.setCurrentRow(0)

    dialog.export_button.click()

    assert list(tmp_path.glob("*.json")) == []


def test_an_export_that_fails_to_write_says_so(make_dialog, tmp_path, monkeypatch):
    warned = []
    monkeypatch.setattr("src.SavedSessionsDialog.QMessageBox.warning",
                        lambda parent, title, text: warned.append(text))
    monkeypatch.setattr(SavedSessionsDialog, "_ask_about_paths", lambda self: "with")
    monkeypatch.setattr(SavedSessionsDialog, "_choose_export_file",
                        lambda self: tmp_path / "no" / "such" / "dir" / "out.json")
    dialog = make_dialog([saved(media=with_files(tmp_path))])
    dialog.session_list.setCurrentRow(0)

    dialog.export_button.click()

    assert len(warned) == 1


def test_the_question_spells_out_what_a_session_with_paths_gives_away():
    """Someone deciding this needs to know it is not only file names - it is their account
    name and how their collection is laid out."""
    text = SavedSessionsDialog.PATHS_WARNING.lower()

    assert "user name" in text or "username" in text
    assert "folder" in text
    assert "file name" in text or "filename" in text


# --- importing ---


def test_a_session_file_can_be_brought_in_and_shows_up_on_the_shelf(
    make_dialog, main_app, tmp_path, monkeypatch
):
    incoming = tmp_path / "shared.json"
    session_files.write_session_file(incoming, saved(saved_at="from a friend"))
    monkeypatch.setattr(SavedSessionsDialog, "_choose_import_file", lambda self: incoming)
    dialog = make_dialog([])

    dialog.import_button.click()

    assert len(session_files.load_saved_sessions(main_app.data_store)) == 1
    assert "from a friend" in dialog.session_list.item(0).text()


def test_a_file_that_is_not_a_session_is_refused_with_a_word_rather_than_a_crash(
    make_dialog, main_app, tmp_path, monkeypatch
):
    broken = tmp_path / "broken.json"
    broken.write_text("{not json", encoding="utf-8")
    warned = []
    monkeypatch.setattr("src.SavedSessionsDialog.QMessageBox.warning",
                        lambda parent, title, text: warned.append(text))
    monkeypatch.setattr(SavedSessionsDialog, "_choose_import_file", lambda self: broken)
    dialog = make_dialog([])

    dialog.import_button.click()

    assert len(warned) == 1
    assert session_files.load_saved_sessions(main_app.data_store) == []


def test_importing_nothing_changes_nothing(make_dialog, main_app, monkeypatch):
    monkeypatch.setattr(SavedSessionsDialog, "_choose_import_file", lambda self: None)
    dialog = make_dialog([saved()])

    dialog.import_button.click()

    assert len(session_files.load_saved_sessions(main_app.data_store)) == 1


# --- deleting ---


def test_deleting_takes_the_session_off_the_shelf(make_dialog, main_app, monkeypatch):
    monkeypatch.setattr(SavedSessionsDialog, "_confirm_deletion", lambda self: True)
    dialog = make_dialog([saved(saved_at="keep me"), saved(saved_at="drop me")])
    dialog.session_list.setCurrentRow(0)  # the newest, "drop me"

    dialog.delete_button.click()

    remaining = session_files.load_saved_sessions(main_app.data_store)
    assert [entry["saved_at"] for entry in remaining] == ["keep me"]
    assert dialog.session_list.count() == 1


def test_a_declined_deletion_keeps_everything(make_dialog, main_app, monkeypatch):
    monkeypatch.setattr(SavedSessionsDialog, "_confirm_deletion", lambda self: False)
    dialog = make_dialog([saved()])
    dialog.session_list.setCurrentRow(0)

    dialog.delete_button.click()

    assert len(session_files.load_saved_sessions(main_app.data_store)) == 1


def test_the_shelf_is_re_read_from_disk_rather_than_trusted_in_memory(make_dialog, main_app):
    """The list is built from what the store actually holds, so a deletion in Privacy & Data
    cannot leave this dialog showing sessions that are gone."""
    dialog = make_dialog([saved()])
    main_app.data_store.delete(session_files.SAVED_SESSIONS_KEY)

    dialog.refresh()

    assert dialog.session_list.count() == 0


def test_the_dialog_needs_nothing_from_the_app_but_the_store_and_the_replay(tmp_path, qtbot):
    """Guards the seam: the manager is handed the app, but must not start reaching into its
    widgets - that is what keeps it testable without a window."""
    store = UserDataStore(base_dir=tmp_path / "appdata")
    session_files.store_session(store, saved())
    bare = types.SimpleNamespace(data_store=store, replay_session=lambda *a, **k: True)

    dialog = SavedSessionsDialog(bare, parent=None)
    qtbot.addWidget(dialog)

    assert dialog.session_list.count() == 1


def test_a_session_that_never_climaxed_is_marked_in_the_list(make_dialog):
    """It replays differently - the recording plays out and then it carries on as a normal
    session - so the list has to say which kind it is."""
    incomplete = saved()
    incomplete["climax"] = None
    dialog = make_dialog([incomplete])

    assert "stopped early" in dialog.session_list.item(0).text().lower()


def test_the_manager_explains_what_replaying_an_unfinished_session_does(make_dialog):
    dialog = make_dialog([])

    assert "stopped early" in dialog.intro_label.text().lower()


def test_importing_a_broken_session_names_what_is_wrong_with_it(
    make_dialog, tmp_path, monkeypatch
):
    """Somebody iterating on a generated file needs the list, not "could not import"."""
    broken = saved()
    broken["segments"][0]["pattern"] = "Furious Wiggle"
    incoming = tmp_path / "generated.json"
    session_files.write_session_file(incoming, broken)
    warned = []
    monkeypatch.setattr("src.SavedSessionsDialog.QMessageBox.warning",
                        lambda parent, title, text: warned.append(text))
    monkeypatch.setattr(SavedSessionsDialog, "_choose_import_file", lambda self: incoming)
    dialog = make_dialog([])

    dialog.import_button.click()

    assert len(warned) == 1
    assert "Furious Wiggle" in warned[0]
