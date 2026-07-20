from src.changelog import CHANGELOG, entries_since, parse_version
from src.utils import get_current_version


def test_parse_version_splits_into_ints():
    assert parse_version("1.2.3") == (1, 2, 3)


def test_parse_version_handles_two_part_versions():
    assert parse_version("0.1") == (0, 1)


def test_entries_since_empty_last_seen_returns_everything_up_to_current():
    changelog = {"0.1.0": "first", "0.2.0": "second", "0.3.0": "third"}
    result = entries_since("", "0.2.0", changelog=changelog)
    assert result == {"0.1.0": "first", "0.2.0": "second"}


def test_entries_since_returns_only_versions_newer_than_last_seen():
    changelog = {"0.1.0": "first", "0.2.0": "second", "0.3.0": "third"}
    result = entries_since("0.1.0", "0.3.0", changelog=changelog)
    assert result == {"0.2.0": "second", "0.3.0": "third"}


def test_entries_since_same_version_returns_nothing():
    changelog = {"0.1.0": "first", "0.2.0": "second"}
    result = entries_since("0.2.0", "0.2.0", changelog=changelog)
    assert result == {}


def test_entries_since_compares_numerically_not_lexically():
    # "0.10.0" > "0.9.0" numerically, even though it sorts before it as a plain string
    changelog = {"0.9.0": "nine", "0.10.0": "ten"}
    result = entries_since("0.9.0", "0.10.0", changelog=changelog)
    assert result == {"0.10.0": "ten"}


def test_entries_since_preserves_changelog_order():
    changelog = {"0.1.0": "first", "0.2.0": "second", "0.3.0": "third"}
    result = entries_since("", "0.3.0", changelog=changelog)
    assert list(result.keys()) == ["0.1.0", "0.2.0", "0.3.0"]


def test_version_file_has_a_matching_changelog_entry():
    # Catches exactly the mistake that happened live in this project once already: a PR
    # bumped VERSION without adding the matching CHANGELOG entry (see CLAUDE.md's
    # Versioning & Releases section). 0.0.0 is the one exempt placeholder, for a fresh
    # checkout with no releases cut yet.
    version = get_current_version()
    if version == "0.0.0":
        return
    assert version in CHANGELOG, (
        f"VERSION is {version!r} but src/changelog.py has no matching CHANGELOG entry for it"
    )
