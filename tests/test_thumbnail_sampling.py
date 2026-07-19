import random
from pathlib import Path

from src.thumbnail_sampling import (
    compute_thumbnail_grid,
    sample_thumbnails_per_folder,
    sample_thumbnails_with_video_cap,
)


def _files(folder_name, count, ext="png"):
    return [Path(f"{folder_name}/file{i}.{ext}") for i in range(count)]


def _is_video(path):
    return path.suffix == ".mp4"


def test_even_split_across_folders_with_enough_files():
    folder_files = {
        "a": _files("a", 10),
        "b": _files("b", 10),
        "c": _files("c", 10),
    }

    result = sample_thumbnails_per_folder(folder_files, 21, rng=random.Random(0))

    by_folder = {name: sum(1 for p in result if p.parent.name == name) for name in folder_files}
    assert by_folder == {"a": 7, "b": 7, "c": 7}
    assert len(result) == 21


def test_uneven_split_shares_differ_by_at_most_one():
    folder_files = {
        "a": _files("a", 10),
        "b": _files("b", 10),
        "c": _files("c", 10),
    }

    result = sample_thumbnails_per_folder(folder_files, 22, rng=random.Random(0))

    by_folder = {name: sum(1 for p in result if p.parent.name == name) for name in folder_files}
    assert max(by_folder.values()) - min(by_folder.values()) <= 1
    assert len(result) == 22


def test_folder_shorter_than_its_share_contributes_only_what_it_has():
    folder_files = {
        "a": _files("a", 2),
        "b": _files("b", 10),
        "c": _files("c", 10),
    }

    result = sample_thumbnails_per_folder(folder_files, 21, rng=random.Random(0))

    by_folder = {name: sum(1 for p in result if p.parent.name == name) for name in folder_files}
    assert by_folder["a"] == 2
    assert by_folder["b"] == by_folder["c"]


def test_empty_folder_contributes_nothing():
    folder_files = {
        "a": [],
        "b": _files("b", 10),
    }

    result = sample_thumbnails_per_folder(folder_files, 10, rng=random.Random(0))

    assert all(p.parent.name == "b" for p in result)
    assert len(result) == 10


def test_single_folder_gets_full_count():
    folder_files = {"a": _files("a", 10)}

    result = sample_thumbnails_per_folder(folder_files, 5, rng=random.Random(0))

    assert len(result) == 5
    assert all(p.parent.name == "a" for p in result)


def test_single_folder_fewer_files_than_requested():
    folder_files = {"a": _files("a", 3)}

    result = sample_thumbnails_per_folder(folder_files, 10, rng=random.Random(0))

    assert len(result) == 3


def test_zero_folders_returns_empty_list():
    assert sample_thumbnails_per_folder({}, 10, rng=random.Random(0)) == []


def test_all_empty_folders_returns_empty_list():
    folder_files = {"a": [], "b": []}
    assert sample_thumbnails_per_folder(folder_files, 10, rng=random.Random(0)) == []


def test_deterministic_with_injected_rng():
    folder_files = {"a": _files("a", 10), "b": _files("b", 10)}

    result1 = sample_thumbnails_per_folder(folder_files, 6, rng=random.Random(42))
    result2 = sample_thumbnails_per_folder(folder_files, 6, rng=random.Random(42))

    assert result1 == result2


def test_result_order_is_shuffled_not_grouped_by_folder():
    folder_files = {"a": _files("a", 20), "b": _files("b", 20)}

    result = sample_thumbnails_per_folder(folder_files, 20, rng=random.Random(1))

    folder_sequence = [p.parent.name for p in result]
    assert folder_sequence != sorted(folder_sequence)


def test_zero_total_count_returns_empty_list():
    folder_files = {"a": _files("a", 10)}
    assert sample_thumbnails_per_folder(folder_files, 0, rng=random.Random(0)) == []


# --- compute_thumbnail_grid ---


def test_compute_thumbnail_grid_basic():
    columns, rows, count = compute_thumbnail_grid(900, 650, 140, 140)
    assert columns == 6
    assert rows == 4
    assert count == 24


def test_compute_thumbnail_grid_never_zero_columns_or_rows():
    columns, rows, count = compute_thumbnail_grid(50, 50, 140, 140)
    assert columns == 1
    assert rows == 1
    assert count == 1


def test_compute_thumbnail_grid_scales_with_viewport():
    small = compute_thumbnail_grid(600, 400, 140, 140)
    large = compute_thumbnail_grid(1200, 800, 140, 140)
    assert large[2] > small[2]


def test_compute_thumbnail_grid_accounts_for_spacing_and_margin():
    # 2 cells of 140 with 8px spacing and 8px margin on each side need
    # 2*140 + 1*8 + 2*8 = 304px - a bare 300px viewport (no spacing/margin) would wrongly
    # fit 2 columns via naive floor division, but must only fit 1 once spacing/margin count.
    columns, _rows, _count = compute_thumbnail_grid(300, 300, 140, 140, spacing=8, margin=8)
    assert columns == 1

    columns, _rows, _count = compute_thumbnail_grid(304, 304, 140, 140, spacing=8, margin=8)
    assert columns == 2


def test_compute_thumbnail_grid_with_spacing_never_overflows_viewport():
    # Below cell + 2*margin, even a single column can't physically fit - the "always at
    # least 1 column" guarantee necessarily overflows there, which is fine (better one
    # slightly-clipped cell than zero). Only assert the no-overflow invariant above that floor.
    cell, spacing, margin = 140, 8, 8
    min_fitting_width = cell + 2 * margin
    for width in range(min_fitting_width, 2000, 37):
        columns, _rows, _count = compute_thumbnail_grid(width, 800, cell, cell, spacing, margin)
        footprint = columns * cell + (columns - 1) * spacing + 2 * margin
        assert footprint <= width


# --- sample_thumbnails_with_video_cap ---


def test_video_cap_limits_total_videos_across_whole_sample():
    folder_files = {
        "a": _files("a", 20, "mp4"),
        "b": _files("b", 20, "png"),
        "c": _files("c", 20, "png"),
    }

    result = sample_thumbnails_with_video_cap(
        folder_files, total_count=30, max_video_count=4, is_video=_is_video, rng=random.Random(0)
    )

    assert sum(1 for p in result if _is_video(p)) == 4
    assert len(result) == 30


def test_video_cap_not_reached_when_fewer_videos_available_than_cap():
    folder_files = {
        "a": _files("a", 2, "mp4"),
        "b": _files("b", 20, "png"),
    }

    result = sample_thumbnails_with_video_cap(
        folder_files, total_count=10, max_video_count=4, is_video=_is_video, rng=random.Random(0)
    )

    assert sum(1 for p in result if _is_video(p)) == 2
    assert len(result) == 10


def test_video_cap_of_zero_excludes_all_videos():
    folder_files = {
        "a": _files("a", 20, "mp4"),
        "b": _files("b", 20, "png"),
    }

    result = sample_thumbnails_with_video_cap(
        folder_files, total_count=10, max_video_count=0, is_video=_is_video, rng=random.Random(0)
    )

    assert sum(1 for p in result if _is_video(p)) == 0
    assert len(result) == 10


def test_video_cap_with_no_non_video_files_available():
    folder_files = {"a": _files("a", 20, "mp4")}

    result = sample_thumbnails_with_video_cap(
        folder_files, total_count=10, max_video_count=4, is_video=_is_video, rng=random.Random(0)
    )

    assert sum(1 for p in result if _is_video(p)) == 4
    assert len(result) == 4


def test_video_cap_result_is_shuffled_not_videos_first():
    folder_files = {
        "a": _files("a", 20, "mp4"),
        "b": _files("b", 20, "png"),
    }

    result = sample_thumbnails_with_video_cap(
        folder_files, total_count=20, max_video_count=4, is_video=_is_video, rng=random.Random(1)
    )

    is_video_sequence = [_is_video(p) for p in result]
    assert is_video_sequence != sorted(is_video_sequence, reverse=True)
