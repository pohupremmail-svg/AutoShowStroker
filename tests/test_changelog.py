from src.changelog import entries_since, parse_version


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
