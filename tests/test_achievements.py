"""The achievement catalogue and the tracker that judges it.

Qt-free: an achievement is a predicate over a session's stats and the history behind it.
"""
import pytest

from src import achievements
from src.achievements import Achievement, AchievementTracker
from src.user_data import UserDataStore


def entry(**overrides):
    """One history entry, as ScoreTracker writes it."""
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
    }
    base.update(overrides)
    return base


def stats(**overrides):
    """What ScoreTracker.deliver_infos() hands over for the session just ended."""
    return entry(**overrides)


@pytest.fixture
def tracker(tmp_path):
    return AchievementTracker(data_store=UserDataStore(base_dir=tmp_path / "data"))


# --- the catalogue itself ---


def test_every_achievement_has_a_unique_id():
    ids = [item.id for item in achievements.CATALOGUE]
    assert len(ids) == len(set(ids))


def test_every_achievement_names_an_icon_file_that_exists():
    """A typo here is invisible until the dialog is opened, and there is no compiler to
    catch it."""
    for item in achievements.CATALOGUE:
        assert achievements.icon_path(item).exists(), f"{item.id} -> {item.icon}.svg"


def test_every_achievement_says_what_it_wants_unless_it_is_secret():
    for item in achievements.CATALOGUE:
        assert item.name
        if not item.secret:
            assert item.description


def test_the_catalogue_has_secret_entries_and_they_are_the_minority():
    secret = [item for item in achievements.CATALOGUE if item.secret]
    assert secret
    assert len(secret) < len(achievements.CATALOGUE) / 2


# --- unlocking ---


def test_a_long_session_unlocks_the_endurance_tier(tracker):
    history = [entry(total_dur_sec=45 * 60)]

    unlocked = tracker.evaluate(stats(total_dur_sec=45 * 60), history)

    assert "endurance_45" in [item.id for item in unlocked]


def test_a_short_session_unlocks_nothing_it_has_not_earned(tracker):
    history = [entry(total_dur_sec=60.0, total_num_beat=100)]

    unlocked = tracker.evaluate(stats(total_dur_sec=60.0, total_num_beat=100), history)

    assert [item.id for item in unlocked] == []


def test_an_achievement_is_only_ever_announced_once(tracker):
    history = [entry(total_dur_sec=45 * 60)]
    tracker.evaluate(stats(total_dur_sec=45 * 60), history)

    again = tracker.evaluate(stats(total_dur_sec=45 * 60), history + [entry()])

    assert "endurance_45" not in [item.id for item in again]


def test_unlocking_is_remembered_across_restarts(tmp_path):
    store = UserDataStore(base_dir=tmp_path / "data")
    AchievementTracker(data_store=store).evaluate(
        stats(total_dur_sec=45 * 60), [entry(total_dur_sec=45 * 60)]
    )

    reopened = AchievementTracker(data_store=store)

    assert reopened.is_unlocked("endurance_45") is True
    assert reopened.unlocked_at("endurance_45")


def test_lifetime_beats_add_up_across_every_session(tracker):
    history = [entry(total_num_beat=20_000) for _ in range(5)]

    unlocked = tracker.evaluate(stats(total_num_beat=20_000), history)

    assert "lifetime_beats_100k" in [item.id for item in unlocked]


def test_coming_back_is_its_own_achievement(tracker):
    history = [entry() for _ in range(5)]

    unlocked = tracker.evaluate(stats(), history)

    assert "returner_5" in [item.id for item in unlocked]


# --- obedience is only obedience when it cost something ---


def test_obeying_a_denial_counts(tracker):
    history = [entry(climax_outcome="denied", reported_outcome="stopped")]

    unlocked = tracker.evaluate(stats(), history)

    assert "obedient_1" in [item.id for item in unlocked]


def test_obeying_a_ruin_counts(tracker):
    history = [entry(climax_outcome="ruined", reported_outcome="ruined")]

    assert "obedient_1" in [item.id for item in tracker.evaluate(stats(), history)]


def test_being_told_to_come_and_coming_is_not_obedience(tracker):
    """It is doing what you were going to do anyway - letting it count would make the whole
    track free."""
    history = [entry(climax_outcome="real", reported_outcome="came") for _ in range(20)]

    unlocked = tracker.evaluate(stats(), history)

    assert "obedient_1" not in [item.id for item in unlocked]


def test_coming_after_a_denial_is_disobedience(tracker):
    history = [entry(climax_outcome="denied", reported_outcome="came")]

    assert "disobedient_1" in [item.id for item in tracker.evaluate(stats(), history)]


def test_a_session_nobody_answered_for_counts_as_neither(tracker):
    history = [entry(climax_outcome="denied", reported_outcome=None) for _ in range(20)]

    ids = [item.id for item in tracker.evaluate(stats(), history)]

    assert "obedient_1" not in ids
    assert "disobedient_1" not in ids


# --- fake-outs and edges ---


def test_surviving_three_fake_outs_in_one_session_counts(tracker):
    assert "fakeouts_3" in [
        item.id for item in tracker.evaluate(stats(fakeout_count=3), [entry(fakeout_count=3)])
    ]






def test_edging_your_way_through_a_session_counts(tracker):
    assert "edges_10" in [
        item.id for item in tracker.evaluate(stats(edge_count=10), [entry(edge_count=10)])
    ]


def test_a_long_session_without_a_single_edge_counts(tracker):
    played = stats(total_dur_sec=45 * 60, edge_count=0)

    assert "edges_none" in [item.id for item in tracker.evaluate(played, [entry(**played)])]


# --- progress on the ones still locked ---


def test_a_locked_achievement_reports_how_far_along_you_are(tracker):
    endurance = next(item for item in achievements.CATALOGUE if item.id == "endurance_45")

    current, target = endurance.progress(stats(total_dur_sec=20 * 60), [entry()])

    assert target == 45 * 60
    assert current == pytest.approx(20 * 60)


def test_progress_never_reads_past_its_target(tracker):
    """A progress bar at 300% is a bug, not a flourish."""
    for item in achievements.CATALOGUE:
        if item.progress is None:
            continue
        huge = stats(total_dur_sec=10**6, total_num_beat=10**7, fakeout_count=500,
                     edge_count=500)
        current, target = item.progress(huge, [huge] * 500)
        assert current <= target, item.id


# --- clearing it all ---


def test_everything_can_be_deleted(tracker):
    tracker.evaluate(stats(total_dur_sec=45 * 60), [entry(total_dur_sec=45 * 60)])

    tracker.clear()

    assert tracker.unlocked == {}
    assert tracker.is_unlocked("endurance_45") is False


def test_a_tracker_without_a_store_still_works(tmp_path):
    """Same shape as ScoreTracker: no store means nothing is persisted, not a crash."""
    bare = AchievementTracker(data_store=None)

    unlocked = bare.evaluate(stats(total_dur_sec=45 * 60), [entry(total_dur_sec=45 * 60)])

    # A 45 minute session with no edges earns the endurance tier and "Never Asked" both.
    assert "endurance_45" in [item.id for item in unlocked]
    assert bare.is_unlocked("endurance_45") is True


def test_a_corrupt_unlock_file_does_not_stop_the_app(tmp_path):
    store = UserDataStore(base_dir=tmp_path / "data")
    store.save("achievements", ["not", "a", "mapping"])

    assert AchievementTracker(data_store=store).unlocked == {}


def test_an_achievement_whose_check_explodes_is_skipped(tracker):
    """One bad predicate must not cost the user every other unlock in the same session."""
    broken = Achievement(
        id="boom", name="Boom", description="explodes", icon="tip",
        check=lambda played, history: 1 / 0,
    )
    tracker.catalogue = (broken, *achievements.CATALOGUE)

    unlocked = tracker.evaluate(stats(total_dur_sec=45 * 60), [entry(total_dur_sec=45 * 60)])

    assert "endurance_45" in [item.id for item in unlocked]
    assert "boom" not in [item.id for item in unlocked]


# --- tiers belong to one another ---


def test_a_tiered_achievement_knows_its_track_and_level():
    endurance = [item for item in achievements.CATALOGUE if item.track == "Endurance"]

    assert [item.level for item in endurance] == [1, 2, 3]
    assert [item.id for item in endurance] == ["endurance_45", "endurance_90", "endurance_120"]


def test_a_standalone_achievement_has_no_track():
    edges = next(item for item in achievements.CATALOGUE if item.id == "edges_10")

    assert edges.track is None
    assert edges.level == 0


def test_grouping_puts_a_whole_track_in_one_tile():
    """Three levels of the same thing are one achievement with three stages, not three
    achievements that happen to look alike."""
    groups = achievements.grouped()

    endurance = next(g for g in groups if g[0].id == "endurance_45")
    assert [item.level for item in endurance] == [1, 2, 3]


def test_grouping_leaves_standalone_achievements_alone():
    groups = achievements.grouped()

    single = next(g for g in groups if g[0].id == "edges_10")
    assert len(single) == 1


def test_grouping_loses_nothing_and_keeps_the_order():
    flat = [item for group in achievements.grouped() for item in group]

    assert flat == list(achievements.CATALOGUE)


def test_a_tier_is_named_for_its_step_not_for_the_whole_track():
    """The tile carries the track name, so the level only has to say which step it is."""
    endurance = [item for item in achievements.CATALOGUE if item.track == "Endurance"]

    assert [item.name for item in endurance] == ["45 minutes", "90 minutes", "120 minutes"]


# --- fake-outs are a track, and surviving means surviving ---


def test_the_fake_out_track_has_three_steps():
    fakes = [item for item in achievements.CATALOGUE if item.track == "Not Falling For It"]

    assert [item.level for item in fakes] == [1, 2, 3]
    assert [item.id for item in fakes] == ["fakeouts_1", "fakeouts_3", "fakeouts_5"]


def test_surviving_fake_outs_means_not_falling_for_them(tracker):
    """The rule used to count the cues and ignore what you did about them, so "Not Falling
    For It" was handed out to people who fell for every single one."""
    fell_for_all = stats(fakeout_count=3, fakeouts_fallen_for=3)

    unlocked = tracker.evaluate(fell_for_all, [entry(**fell_for_all)])

    assert [item.id for item in unlocked if item.id.startswith("fakeouts")] == []


def test_one_survived_fake_out_is_already_worth_something(tracker):
    played = stats(fakeout_count=1, fakeouts_fallen_for=0)

    unlocked = [item.id for item in tracker.evaluate(played, [entry(**played)])]

    assert "fakeouts_1" in unlocked
    assert "fakeouts_3" not in unlocked


def test_five_survived_fake_outs_take_the_whole_track(tracker):
    played = stats(fakeout_count=5, fakeouts_fallen_for=0)

    unlocked = [item.id for item in tracker.evaluate(played, [entry(**played)])]

    assert {"fakeouts_1", "fakeouts_3", "fakeouts_5"} <= set(unlocked)


def test_a_session_you_were_fooled_in_makes_no_progress_at_all(tracker):
    """A bar filling to the top on a session that earned nothing would be a lie."""
    fakes_1 = next(item for item in achievements.CATALOGUE if item.id == "fakeouts_1")

    current, _target = fakes_1.progress(stats(fakeout_count=4, fakeouts_fallen_for=1), [])

    assert current == 0


# --- disobedience is discovered, never advertised ---


def test_the_disobedience_track_is_secret(tracker):
    """Listing "come anyway after being denied" as a goal is an invitation, not a record."""
    disobedient = [item for item in achievements.CATALOGUE if item.track == "Couldn't Help It"]

    assert disobedient
    assert all(item.secret for item in disobedient)


def test_obedience_is_not_secret():
    obedient = [item for item in achievements.CATALOGUE if item.track == "Good Boy"]

    assert obedient
    assert not any(item.secret for item in obedient)


def test_a_secret_track_still_unlocks_normally(tracker):
    disobeyed = entry(climax_outcome="denied", reported_outcome="came")

    unlocked = [item.id for item in tracker.evaluate(stats(), [disobeyed])]

    assert "disobedient_1" in unlocked


def test_no_mark_is_shipped_that_nothing_uses():
    """An unused SVG is dead weight in the bundle and, worse, a thing somebody redraws for
    nothing."""
    from src.utils import get_project_root

    shipped = {p.stem for p in (get_project_root() / achievements.ICON_DIR).glob("*.svg")}
    used = {item.icon for item in achievements.CATALOGUE}

    assert shipped == used


def test_the_fake_out_track_is_secret_too():
    """Its condition gives the mechanic away: a goal that reads "let fake cues pass without
    acting on one" tells the user fakes exist and that they should hesitate at every climax
    announcement - which is the one thing a fake-out cannot survive."""
    fakes = [item for item in achievements.CATALOGUE if item.track == "Not Falling For It"]

    assert fakes
    assert all(item.secret for item in fakes)
