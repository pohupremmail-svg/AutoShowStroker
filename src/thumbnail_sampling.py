import random
from pathlib import Path


def sample_thumbnails_per_folder(
    folder_files: dict[str, list[Path]],
    total_count: int,
    rng: random.Random | None = None,
) -> list[Path]:
    """Samples up to `total_count` files, split as evenly as possible across folders.

    A folder with fewer files than its computed share just contributes what it has -
    the shortfall is not redistributed to other folders, so the result can be shorter
    than `total_count` if folders are thin."""
    rng = rng or random.Random()

    non_empty_folders = [files for files in folder_files.values() if files]
    if not non_empty_folders or total_count <= 0:
        return []

    folder_count = len(non_empty_folders)
    base_share, remainder = divmod(total_count, folder_count)

    result = []
    for i, files in enumerate(non_empty_folders):
        share = base_share + (1 if i < remainder else 0)
        result.extend(rng.sample(files, min(share, len(files))))

    rng.shuffle(result)
    return result


def sample_thumbnails_with_video_cap(
    folder_files: dict[str, list[Path]],
    total_count: int,
    max_video_count: int,
    is_video,
    rng: random.Random | None = None,
) -> list[Path]:
    """Like sample_thumbnails_per_folder, but never selects more than `max_video_count`
    video files across the *whole* sample (regardless of folder) - videos are heavier to
    animate/thumbnail than images, so this is a deliberate global cap, not a per-folder one.

    `is_video` is a callable(Path) -> bool, injected so this module stays free of any Qt/
    media_kinds dependency."""
    rng = rng or random.Random()

    video_pool = {folder: [f for f in files if is_video(f)] for folder, files in folder_files.items()}
    non_video_pool = {folder: [f for f in files if not is_video(f)] for folder, files in folder_files.items()}

    videos = sample_thumbnails_per_folder(video_pool, min(max_video_count, total_count), rng)
    non_videos = sample_thumbnails_per_folder(non_video_pool, total_count - len(videos), rng)

    result = videos + non_videos
    rng.shuffle(result)
    return result


def compute_thumbnail_grid(
    viewport_width: int,
    viewport_height: int,
    cell_width: int,
    cell_height: int,
    spacing: int = 0,
    margin: int = 0,
) -> tuple[int, int, int]:
    """Returns (columns, rows, count) of thumbnail cells that fit in the given viewport.

    `spacing`/`margin` must match the real QGridLayout's spacing/contentsMargins - a grid
    of N cells needs N*cell + (N-1)*spacing + 2*margin pixels, not just N*cell. Ignoring
    that undercounts the real footprint and overflows the viewport (forcing an unwanted
    scrollbar, which then shrinks the viewport further and desyncs any column count
    computed before it appeared)."""
    usable_width = max(0, viewport_width - 2 * margin)
    usable_height = max(0, viewport_height - 2 * margin)
    columns = max(1, (usable_width + spacing) // (cell_width + spacing))
    rows = max(1, (usable_height + spacing) // (cell_height + spacing))
    return columns, rows, columns * rows
