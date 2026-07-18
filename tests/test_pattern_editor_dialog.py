import pytest
from PyQt6.QtCore import QSettings

from src.BeatHandler import BeatHandler
from src.PatternEditorDialog import DEFAULT_STEPS, MAX_STEPS, PREVIEW_BASE_STEP_MS, PatternEditorDialog


@pytest.fixture
def beat_handler(qtbot, tmp_path):
    settings = QSettings(str(tmp_path / "settings.ini"), QSettings.Format.IniFormat)
    h = BeatHandler(settings=settings)
    qtbot.addWidget(h.beat_meter)
    yield h
    h.stop()


@pytest.fixture
def dialog(beat_handler, qtbot):
    d = PatternEditorDialog(beat_handler)
    qtbot.addWidget(d)
    return d


def test_pattern_list_only_shows_custom_patterns(beat_handler, qtbot):
    beat_handler.add_or_update_custom_pattern("My Pattern", [1, -1])
    d = PatternEditorDialog(beat_handler)
    qtbot.addWidget(d)

    names = [d.pattern_list.item(i).text() for i in range(d.pattern_list.count())]
    assert names == ["My Pattern"]
    assert "Standard Beat" not in names


def test_new_pattern_starts_with_default_steps(dialog):
    assert dialog._current_steps() == DEFAULT_STEPS
    assert dialog.name_edit.text() == ""


def test_save_creates_new_custom_pattern(dialog, beat_handler):
    dialog.name_edit.setText("My Pattern")
    dialog._step_widgets[0].set_value(-2)
    dialog._save()

    assert beat_handler.custom_beat_patterns["My Pattern"][0] == -2
    names = [dialog.pattern_list.item(i).text() for i in range(dialog.pattern_list.count())]
    assert names == ["My Pattern"]


def test_save_with_empty_name_shows_error_and_does_not_persist(dialog, beat_handler):
    dialog.name_edit.setText("")
    dialog._save()

    assert beat_handler.custom_beat_patterns == {}
    assert dialog.error_label.text() != ""


def test_save_with_no_audible_steps_shows_error(dialog, beat_handler):
    dialog.name_edit.setText("All Muted")
    for widget in dialog._step_widgets:
        widget.set_value(-widget.get_value() if widget.get_value() > 0 else widget.get_value())
    dialog._save()

    assert "All Muted" not in beat_handler.custom_beat_patterns
    assert dialog.error_label.text() != ""


def test_save_with_name_colliding_with_builtin_shows_error(dialog, beat_handler):
    dialog.name_edit.setText("Standard Beat")
    dialog._save()

    assert dialog.error_label.text() != ""
    assert beat_handler.available_beat_patterns["Standard Beat"] == [1]


def test_selecting_existing_pattern_loads_it_into_editor(beat_handler, qtbot):
    beat_handler.add_or_update_custom_pattern("My Pattern", [2, -3, 1])
    d = PatternEditorDialog(beat_handler)
    qtbot.addWidget(d)

    d.pattern_list.item(0).setSelected(True)

    assert d.name_edit.text() == "My Pattern"
    assert d._current_steps() == [2, -3, 1]


def test_editing_existing_pattern_updates_in_place(beat_handler, qtbot):
    beat_handler.add_or_update_custom_pattern("My Pattern", [1, 1])
    d = PatternEditorDialog(beat_handler)
    qtbot.addWidget(d)
    d.pattern_list.item(0).setSelected(True)

    d._step_widgets[0].set_value(4)
    d._save()

    assert beat_handler.custom_beat_patterns == {"My Pattern": [4, 1]}
    names = [d.pattern_list.item(i).text() for i in range(d.pattern_list.count())]
    assert names == ["My Pattern"]  # no duplicate entry


def test_renaming_existing_pattern_removes_old_name(beat_handler, qtbot):
    beat_handler.add_or_update_custom_pattern("Old Name", [1, 1])
    d = PatternEditorDialog(beat_handler)
    qtbot.addWidget(d)
    d.pattern_list.item(0).setSelected(True)

    d.name_edit.setText("New Name")
    d._save()

    assert "Old Name" not in beat_handler.custom_beat_patterns
    assert beat_handler.custom_beat_patterns["New Name"] == [1, 1]


def test_delete_removes_selected_pattern(beat_handler, qtbot):
    beat_handler.add_or_update_custom_pattern("My Pattern", [1, 1])
    d = PatternEditorDialog(beat_handler)
    qtbot.addWidget(d)
    d.pattern_list.item(0).setSelected(True)

    d._delete_selected_pattern()

    assert "My Pattern" not in beat_handler.custom_beat_patterns
    assert d.pattern_list.count() == 0


def test_add_step_appends_a_step_widget(dialog):
    initial_count = len(dialog._step_widgets)
    dialog._add_step()
    assert len(dialog._step_widgets) == initial_count + 1


def test_add_step_is_capped_at_max_steps(dialog):
    for _ in range(MAX_STEPS + 5):
        dialog._add_step()
    assert len(dialog._step_widgets) == MAX_STEPS


def test_remove_step_cannot_go_below_one_step(dialog):
    for _ in range(10):
        dialog._remove_step()
    assert len(dialog._step_widgets) == 1


def test_preview_tick_plays_sound_only_for_audible_steps(dialog, beat_handler, monkeypatch):
    dialog._set_steps([1, -1])
    played = []
    monkeypatch.setattr(beat_handler, "play_beat_sound", lambda: played.append(True))

    dialog._preview_position = 0
    dialog._preview_tick()  # step 0 is audible
    dialog._preview_tick()  # step 1 is muted
    dialog._stop_preview()

    assert played == [True]


def test_preview_tick_schedules_interval_inversely_proportional_to_weight(dialog):
    # Weight is an inverse-duration multiplier: a "4" is a quarter as long as a "1".
    # The preview must reflect that instead of ticking at a flat interval.
    dialog._set_steps([1, 4])

    dialog._preview_position = 0
    dialog._preview_tick()  # schedules the interval for the weight-1 (longest) step
    interval_for_weight_1 = dialog._preview_timer.interval()

    dialog._preview_position = 1
    dialog._preview_tick()  # schedules the interval for the weight-4 (shortest) step
    interval_for_weight_4 = dialog._preview_timer.interval()
    dialog._stop_preview()

    assert interval_for_weight_1 == PREVIEW_BASE_STEP_MS
    assert interval_for_weight_4 == PREVIEW_BASE_STEP_MS // 4
    assert interval_for_weight_4 < interval_for_weight_1
