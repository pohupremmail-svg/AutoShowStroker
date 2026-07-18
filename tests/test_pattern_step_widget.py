from PyQt6.QtCore import QEvent, QPointF, Qt
from PyQt6.QtGui import QMouseEvent
from PyQt6.QtWidgets import QApplication

from src.PatternStepWidget import MAX_WEIGHT, MIN_WEIGHT, PatternStepWidget


def _mouse_event(event_type, y, x=20):
    return QMouseEvent(
        event_type,
        QPointF(x, y),
        Qt.MouseButton.LeftButton,
        Qt.MouseButton.LeftButton,
        Qt.KeyboardModifier.NoModifier,
    )


def _click(widget, y=60):
    widget.mousePressEvent(_mouse_event(QEvent.Type.MouseButtonPress, y))
    widget.mouseReleaseEvent(_mouse_event(QEvent.Type.MouseButtonRelease, y))


def _drag(widget, start_y, end_y):
    widget.mousePressEvent(_mouse_event(QEvent.Type.MouseButtonPress, start_y))
    widget.mouseMoveEvent(_mouse_event(QEvent.Type.MouseMove, end_y))
    widget.mouseReleaseEvent(_mouse_event(QEvent.Type.MouseButtonRelease, end_y))


def test_default_value_roundtrips(qtbot):
    widget = PatternStepWidget(2)
    qtbot.addWidget(widget)
    assert widget.get_value() == 2


def test_set_value_updates_audible_and_weight(qtbot):
    widget = PatternStepWidget(1)
    qtbot.addWidget(widget)
    widget.set_value(-3)
    assert widget.get_value() == -3


def test_set_value_clamps_weight_to_valid_range(qtbot):
    widget = PatternStepWidget(1)
    qtbot.addWidget(widget)
    widget.set_value(9)
    assert widget.get_value() == MAX_WEIGHT
    widget.set_value(-9)
    assert widget.get_value() == -MAX_WEIGHT


def test_click_toggles_audible_state_and_preserves_weight(qtbot):
    widget = PatternStepWidget(2)
    qtbot.addWidget(widget)
    _click(widget)
    assert widget.get_value() == -2
    _click(widget)
    assert widget.get_value() == 2


def test_click_emits_changed_signal(qtbot):
    widget = PatternStepWidget(1)
    qtbot.addWidget(widget)
    with qtbot.waitSignal(widget.changed, timeout=1000):
        _click(widget)


def test_small_movement_is_still_treated_as_click(qtbot):
    widget = PatternStepWidget(1)
    qtbot.addWidget(widget)
    _drag(widget, start_y=60, end_y=61)  # 1px movement, below drag threshold
    assert widget.get_value() == -1


def test_real_mouse_jitter_within_platform_click_threshold_still_toggles(qtbot):
    # A real mouse/trackpad "click" almost always registers a few pixels of movement.
    # Anything under the OS's own click/drag distance must still count as a click.
    widget = PatternStepWidget(1)
    qtbot.addWidget(widget)
    jitter = QApplication.startDragDistance() - 1
    _drag(widget, start_y=60, end_y=60 + jitter)
    assert widget.get_value() == -1


def test_drag_near_top_sets_min_weight_ie_longest_step_and_activates_muted_step(qtbot):
    # Magnitude is an inverse-duration multiplier (BeatHandler divides the base step
    # time by it), so the *longest* step is weight 1, drawn as the tallest bar (top).
    widget = PatternStepWidget(-3)
    qtbot.addWidget(widget)
    _drag(widget, start_y=115, end_y=2)
    assert widget.get_value() == MIN_WEIGHT


def test_drag_near_bottom_sets_max_weight_ie_shortest_step(qtbot):
    widget = PatternStepWidget(1)
    qtbot.addWidget(widget)
    _drag(widget, start_y=2, end_y=118)
    assert widget.get_value() == MAX_WEIGHT


def test_tooltip_explains_duration_semantics(qtbot):
    widget = PatternStepWidget(1)
    qtbot.addWidget(widget)
    assert "duration" in widget.toolTip().lower()


def test_set_highlighted_updates_state(qtbot):
    widget = PatternStepWidget(1)
    qtbot.addWidget(widget)
    widget.set_highlighted(True)
    assert widget._highlighted is True
    widget.set_highlighted(False)
    assert widget._highlighted is False
