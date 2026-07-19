from pathlib import Path

from src import media_kinds


def test_extension_sets_are_disjoint():
    assert media_kinds.IMAGE_EXTENSIONS.isdisjoint(media_kinds.GIF_EXTENSIONS)
    assert media_kinds.IMAGE_EXTENSIONS.isdisjoint(media_kinds.VIDEO_EXTENSIONS)
    assert media_kinds.GIF_EXTENSIONS.isdisjoint(media_kinds.VIDEO_EXTENSIONS)


def test_supported_extensions_matches_original_hardcoded_list():
    original = {'.mp4', '.avi', '.mov', '.mkv', '.gif', '.jpeg', '.jpg', '.png', '.bmp'}
    assert media_kinds.SUPPORTED_EXTENSIONS == original


def test_media_kind_recognizes_images():
    for ext in ['.png', '.jpg', '.jpeg', '.bmp']:
        assert media_kinds.media_kind(Path(f"file{ext}")) == "image"


def test_media_kind_recognizes_gif():
    assert media_kinds.media_kind(Path("file.gif")) == "gif"


def test_media_kind_recognizes_video():
    for ext in ['.mp4', '.avi', '.mov', '.mkv']:
        assert media_kinds.media_kind(Path(f"file{ext}")) == "video"


def test_media_kind_is_case_insensitive():
    assert media_kinds.media_kind(Path("file.PNG")) == "image"
    assert media_kinds.media_kind(Path("file.GIF")) == "gif"
    assert media_kinds.media_kind(Path("file.MP4")) == "video"


def test_media_kind_unknown_extension():
    assert media_kinds.media_kind(Path("file.txt")) == "unknown"


def test_find_supported_files_finds_all_supported_extensions(tmp_path):
    for ext in media_kinds.SUPPORTED_EXTENSIONS:
        (tmp_path / f"file{ext}").write_bytes(b"x")
    (tmp_path / "file.txt").write_bytes(b"x")

    found = media_kinds.find_supported_files(str(tmp_path))

    assert {f.name for f in found} == {f"file{ext}" for ext in media_kinds.SUPPORTED_EXTENSIONS}


def test_find_supported_files_searches_recursively(tmp_path):
    sub = tmp_path / "sub"
    sub.mkdir()
    (sub / "deep.png").write_bytes(b"x")

    found = media_kinds.find_supported_files(str(tmp_path))

    assert any(f.name == "deep.png" for f in found)


def test_find_supported_files_empty_folder(tmp_path):
    assert media_kinds.find_supported_files(str(tmp_path)) == []


def test_find_supported_files_survives_permission_error(tmp_path, monkeypatch):
    (tmp_path / "a.png").write_bytes(b"x")

    def raising_rglob(self, pattern):
        raise PermissionError("no access")
        yield  # pragma: no cover - makes this a generator, matching rglob's real shape

    monkeypatch.setattr(Path, "rglob", raising_rglob)

    assert media_kinds.find_supported_files(str(tmp_path)) == []
