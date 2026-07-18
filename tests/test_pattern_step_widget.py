from PyQt6.QtCore import QEvent, QPointF, Qt
from PyQt6.QtGui import QMouseEvent

from src.PatternStepWidget import MAX_WEIGHT, MIN_WEIGHT, PatternStepWidget


def _mouse_event(event_type, y, x=20, buttons=Qt.MouseButton.LeftButton):
    return QMouseEvent(
        event_type,
        QPointF(x, y),
        Qt.MouseButton.LeftButton,
        buttons,
        Qt.KeyboardModifier.NoModifier,
    )


def _drag_bar(bar, *ys):
    bar.mousePressEvent(_mouse_event(QEvent.Type.MouseButtonPress, ys[0]))
    for y in ys[1:]:
        bar.mouseMoveEvent(_mouse_event(QEvent.Type.MouseMove, y))
    bar.mouseReleaseEvent(_mouse_event(QEvent.Type.MouseButtonRelease, ys[-1], buttons=Qt.MouseButton.NoButton))


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


def test_pause_button_reflects_initial_muted_state(qtbot):
    audible_widget = PatternStepWidget(2)
    qtbot.addWidget(audible_widget)
    assert audible_widget._pause_button.isChecked() is False

    muted_widget = PatternStepWidget(-2)
    qtbot.addWidget(muted_widget)
    assert muted_widget._pause_button.isChecked() is True


def test_clicking_pause_button_toggles_muted_state_and_preserves_weight(qtbot):
    widget = PatternStepWidget(2)
    qtbot.addWidget(widget)

    qtbot.mouseClick(widget._pause_button, Qt.MouseButton.LeftButton)
    assert widget.get_value() == -2

    qtbot.mouseClick(widget._pause_button, Qt.MouseButton.LeftButton)
    assert widget.get_value() == 2


def test_pause_button_text_reflects_state(qtbot):
    widget = PatternStepWidget(1)
    qtbot.addWidget(widget)
    assert widget._pause_button.text() == "Beat"

    qtbot.mouseClick(widget._pause_button, Qt.MouseButton.LeftButton)
    assert widget._pause_button.text() == "Pause"


def test_set_value_updates_pause_button_state(qtbot):
    widget = PatternStepWidget(1)
    qtbot.addWidget(widget)
    widget.set_value(-1)
    assert widget._pause_button.isChecked() is True
    assert widget._pause_button.text() == "Pause"


def test_dragging_bar_sets_weight_without_changing_muted_state(qtbot):
    # This is the core fix: adjusting a paused step's length must not flip it back
    # to audible just because the bar was touched.
    widget = PatternStepWidget(-3)
    qtbot.addWidget(widget)

    _drag_bar(widget._bar, 115, 2)  # drag from bottom to top -> longest weight

    assert widget.get_value() == -MIN_WEIGHT


def test_drag_near_top_of_bar_sets_min_weight_ie_longest_step(qtbot):
    widget = PatternStepWidget(2)
    qtbot.addWidget(widget)
    _drag_bar(widget._bar, 115, 2)
    assert widget.get_value() == MIN_WEIGHT


def test_drag_near_bottom_of_bar_sets_max_weight_ie_shortest_step(qtbot):
    widget = PatternStepWidget(1)
    qtbot.addWidget(widget)
    _drag_bar(widget._bar, 2, 118)
    assert widget.get_value() == MAX_WEIGHT


def test_click_on_bar_alone_sets_weight_like_a_slider(qtbot):
    # A single click (no movement) on the bar should also just set the weight to
    # that position - there's no more click-vs-drag ambiguity since the bar no
    # longer toggles anything.
    widget = PatternStepWidget(1)
    qtbot.addWidget(widget)
    _drag_bar(widget._bar, 118)
    assert widget.get_value() == MAX_WEIGHT


def test_set_highlighted_updates_bar_state(qtbot):
    widget = PatternStepWidget(1)
    qtbot.addWidget(widget)
    widget.set_highlighted(True)
    assert widget._bar._highlighted is True
    widget.set_highlighted(False)
    assert widget._bar._highlighted is False


def test_bar_tooltip_explains_duration_semantics(qtbot):
    widget = PatternStepWidget(1)
    qtbot.addWidget(widget)
    assert "duration" in widget._bar.toolTip().lower()
