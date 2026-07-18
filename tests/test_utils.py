import sys

from src.utils import get_project_root


def test_get_project_root_finds_repo_root_from_script():
    root = get_project_root()
    assert (root / "main.py").exists()


def test_get_project_root_uses_meipass_when_frozen(monkeypatch, tmp_path):
    monkeypatch.setattr(sys, "_MEIPASS", str(tmp_path), raising=False)
    assert get_project_root() == tmp_path
