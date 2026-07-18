import sys

import pytest

from src import utils
from src.utils import format_duration, get_current_version, get_project_root


def test_get_project_root_finds_repo_root_from_script():
    root = get_project_root()
    assert (root / "main.py").exists()


def test_get_project_root_uses_meipass_when_frozen(monkeypatch, tmp_path):
    monkeypatch.setattr(sys, "_MEIPASS", str(tmp_path), raising=False)
    assert get_project_root() == tmp_path


def test_get_current_version_reads_version_file(monkeypatch, tmp_path):
    (tmp_path / "VERSION").write_text("1.2.3\n", encoding="utf-8")
    monkeypatch.setattr(utils, "get_project_root", lambda: tmp_path)

    assert get_current_version() == "1.2.3"


def test_get_current_version_strips_whitespace(monkeypatch, tmp_path):
    (tmp_path / "VERSION").write_text("  0.1.0  \n\n", encoding="utf-8")
    monkeypatch.setattr(utils, "get_project_root", lambda: tmp_path)

    assert get_current_version() == "0.1.0"


@pytest.mark.parametrize(
    ("seconds", "expected"),
    [
        (None, "N/A"),
        (0, "0s"),
        (45, "45s"),
        (60, "1 Min"),
        (125, "2 Min 5s"),
    ],
)
def test_format_duration(seconds, expected):
    assert format_duration(seconds) == expected
