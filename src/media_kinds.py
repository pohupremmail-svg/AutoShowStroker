from pathlib import Path

IMAGE_EXTENSIONS = {'.png', '.jpg', '.jpeg', '.bmp'}
GIF_EXTENSIONS = {'.gif'}
VIDEO_EXTENSIONS = {'.mp4', '.avi', '.mov', '.mkv'}
SUPPORTED_EXTENSIONS = IMAGE_EXTENSIONS | GIF_EXTENSIONS | VIDEO_EXTENSIONS


def media_kind(path) -> str:
    ext = Path(path).suffix.lower()
    if ext in IMAGE_EXTENSIONS:
        return "image"
    if ext in GIF_EXTENSIONS:
        return "gif"
    if ext in VIDEO_EXTENSIONS:
        return "video"
    return "unknown"


def find_supported_files(folder: str) -> list[Path]:
    pfad = Path(folder)
    gefundene_dateien = []
    try:
        for datei in pfad.rglob('*'):
            if datei.is_file() and datei.suffix.lower() in SUPPORTED_EXTENSIONS:
                gefundene_dateien.append(datei)
    except OSError:
        # A restricted subdirectory (permissions, a broken junction, ...) anywhere under
        # `folder` would otherwise abort the whole walk - better to return whatever was
        # found before the error than crash the folder picker over one bad subtree.
        pass
    return gefundene_dateien
