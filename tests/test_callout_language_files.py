import json

import pytest

from src.CalloutHandler import TRIGGER_KEYS
from src.utils import get_project_root

CALLOUT_DIR = get_project_root() / "res" / "callouts"
LANGUAGE_FILES = sorted(CALLOUT_DIR.glob("*.json"))


def test_at_least_one_language_file_exists():
    assert LANGUAGE_FILES, f"No language files found in {CALLOUT_DIR}"


@pytest.mark.parametrize("path", LANGUAGE_FILES, ids=lambda p: p.stem)
def test_language_file_is_valid_json(path):
    json.loads(path.read_text(encoding="utf-8"))


@pytest.mark.parametrize("path", LANGUAGE_FILES, ids=lambda p: p.stem)
def test_language_file_has_all_required_keys(path):
    data = json.loads(path.read_text(encoding="utf-8"))
    missing = set(TRIGGER_KEYS) - set(data.keys())
    assert not missing, f"{path.name} is missing required trigger keys: {sorted(missing)}"


@pytest.mark.parametrize("path", LANGUAGE_FILES, ids=lambda p: p.stem)
def test_language_file_has_no_unknown_keys(path):
    data = json.loads(path.read_text(encoding="utf-8"))
    unknown = set(data.keys()) - set(TRIGGER_KEYS)
    assert not unknown, f"{path.name} has unknown trigger keys (typo?): {sorted(unknown)}"


@pytest.mark.parametrize("path", LANGUAGE_FILES, ids=lambda p: p.stem)
def test_language_file_values_are_lists_of_strings(path):
    data = json.loads(path.read_text(encoding="utf-8"))
    for key, phrases in data.items():
        assert isinstance(phrases, list), f"{path.name}:{key} must be a list, got {type(phrases).__name__}"
        for i, phrase in enumerate(phrases):
            assert isinstance(phrase, str), f"{path.name}:{key}[{i}] must be a string, got {type(phrase).__name__}"
