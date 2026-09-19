"""The achievement explorer: everything there is, and what you have actually earned."""
import pytest

from src import achievements
from src.achievements import Achievement, AchievementTracker
from src.AchievementsDialog import AchievementsDialog, tinted_mark


def entry(**overrides):
    base = {
        "ended_at": "2026-09-17 20:00",
        "total_dur_sec": 600.0,
        "total_num_beat": 1200,
        "average_beat_speed_active": 2.0,
        "fakeout_count": 0,
        "climax_outcome": None,
        "reported_outcome": None,
        "fakeouts_fallen_for": 0,
        "edge_count": 0,
        "was_replay": False,
    }
    base.update(overrides)
    return base


@pytest.fixture
def tracker():
    return AchievementTracker(data_store=None)


@pytest.fixture
def make_dialog(qtbot, tracker):
    def build(history=()):
        dialog = AchievementsDialog(tracker, list(history), parent=None)
        qtbot.addWidget(dialog)
        return dialog

    return build


# --- what is listed ---


def test_everything_is_listed_whether_you_have_it_or_not(make_dialog):
    """The whole complaint this answers: you could only ever see an achievement by getting
    it."""
    dialog = make_dialog()

    assert len(dialog.cards) == len(achievements.grouped())
    assert len(dialog.cards) < len(achievements.CATALOGUE)  # tracks are folded into one tile


def test_an_earned_achievement_shows_when_you_earned_it(make_dialog, tracker):
    tracker.evaluate(entry(total_dur_sec=45 * 60), [entry(total_dur_sec=45 * 60)])

    dialog = make_dialog([entry(total_dur_sec=45 * 60)])

    card = dialog.card_for("endurance_45")
    assert card.unlocked is True
    assert tracker.unlocked_at("endurance_45") in card.status_text()


def test_a_locked_achievement_says_what_it_wants(make_dialog):
    dialog = make_dialog()

    card = dialog.card_for("endurance_45")
    assert card.unlocked is False
    assert "45 minutes" in card.detail_text()


def test_a_tile_carries_the_track_name_not_the_level_name(make_dialog):
    dialog = make_dialog()

    assert dialog.card_for("endurance_90").title_text() == "Endurance"


def test_a_tile_shows_one_pip_per_level(make_dialog, tracker):
    tracker.unlocked["endurance_45"] = "2026-09-17 21:00"
    dialog = make_dialog()

    card = dialog.card_for("endurance_120")

    assert card.levels_earned == 1
    assert card.level_count == 3


def test_a_tile_points_at_the_next_level_you_have_not_reached(make_dialog, tracker):
    """Naming the one already earned would be telling the user what they know."""
    tracker.unlocked["endurance_45"] = "2026-09-17 21:00"
    dialog = make_dialog([entry(total_dur_sec=50 * 60)])

    card = dialog.card_for("endurance_45")

    assert "90 minutes" in card.detail_text()
    assert card.progress_bar.maximum() == 90 * 60


def test_a_finished_track_stops_asking_for_more(make_dialog, tracker):
    for level in ("endurance_45", "endurance_90", "endurance_120"):
        tracker.unlocked[level] = "2026-09-17 21:00"
    dialog = make_dialog()

    card = dialog.card_for("endurance_45")

    assert card.levels_earned == card.level_count
    assert card.progress_bar is None


def test_a_standalone_achievement_has_no_level_pips(make_dialog):
    dialog = make_dialog()

    assert dialog.card_for("edges_10").level_count == 1


def test_a_locked_achievement_shows_how_far_along_you_are(make_dialog):
    dialog = make_dialog([entry(total_dur_sec=20 * 60)])

    card = dialog.card_for("endurance_45")

    assert card.progress_bar is not None
    assert card.progress_bar.maximum() == 45 * 60
    assert card.progress_bar.value() == 20 * 60


def test_progress_is_read_from_the_most_recent_session(make_dialog):
    """A per-session rule has to be measured against a session, and the last one played is
    the only honest choice."""
    dialog = make_dialog([entry(total_dur_sec=40 * 60), entry(total_dur_sec=5 * 60)])

    assert dialog.card_for("endurance_45").progress_bar.value() == 5 * 60


def test_an_empty_history_still_opens(make_dialog):
    dialog = make_dialog([])

    assert dialog.card_for("endurance_45").progress_bar.value() == 0


# --- secrets ---


def test_a_secret_achievement_keeps_its_name_to_itself(make_dialog):
    secret = next(item for item in achievements.CATALOGUE if item.secret)
    dialog = make_dialog()

    card = dialog.card_for(secret.id)

    assert card.title_text() == AchievementsDialog.SECRET_TITLE
    assert secret.name not in card.title_text()


def test_a_secret_achievement_gives_itself_up_once_earned(make_dialog, tracker):
    secret = next(item for item in achievements.CATALOGUE if item.secret)
    tracker.unlocked[secret.id] = "2026-09-17 21:00"

    dialog = make_dialog()
    card = dialog.card_for(secret.id)

    assert card.title_text() == (secret.track or secret.name)


def test_a_secret_track_hides_how_many_steps_it_has(make_dialog):
    """The pips would give away that there is more of it, which is half of what is secret."""
    secret = next(item for item in achievements.CATALOGUE if item.secret and item.track)
    dialog = make_dialog()

    card = dialog.card_for(secret.id)

    assert card.title_text() == AchievementsDialog.SECRET_TITLE
    assert card.pips is None


def test_a_secret_track_shows_its_steps_once_it_is_out(make_dialog, tracker):
    secret = next(item for item in achievements.CATALOGUE if item.secret and item.track)
    tracker.unlocked[secret.id] = "2026-09-17 21:00"

    card = make_dialog().card_for(secret.id)

    assert card.pips is not None
    assert card.levels_earned == 1
    assert card.level_count == 3


# --- the marks ---


def test_a_mark_lights_up_as_soon_as_any_level_is_earned(make_dialog, tracker):
    tracker.unlocked["endurance_45"] = "2026-09-17 21:00"
    dialog = make_dialog()

    assert dialog.card_for("endurance_45").glow is not None
    assert dialog.card_for("edges_10").glow is None


def test_a_mark_whose_file_is_missing_does_not_stop_the_dialog(make_dialog, tracker, qtbot):
    """A typo in an icon name is invisible until this dialog is opened - it must not be the
    thing that breaks it."""
    broken = Achievement(
        id="typo", name="Typo", description="mark is missing", icon="no_such_mark",
        check=lambda played, history: False,
    )
    tracker.catalogue = (broken,)

    dialog = AchievementsDialog(tracker, [], parent=None)
    qtbot.addWidget(dialog)

    assert dialog.card_for("typo") is not None


def test_a_mark_is_tinted_rather_than_redrawn_per_state():
    """One file serves both states - the grey and the lit version are the same path."""
    item = next(item for item in achievements.CATALOGUE if item.icon == "collar")

    lit = tinted_mark(achievements.icon_path(item), "#ff00bf", 48)
    grey = tinted_mark(achievements.icon_path(item), "#6a6175", 48)

    assert lit.size() == grey.size()
    assert lit.toImage() != grey.toImage()
