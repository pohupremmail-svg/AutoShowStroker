using Gooner.Core.Media;
using Gooner.Core.Randomness;

namespace Gooner.Core.Tests.Media;

// Port of tests/test_thumbnail_sampling.py: which files the folder picker shows as its preview grid,
// split across the folders, with videos capped across the whole grid, and how many cells fit.
public sealed class ThumbnailSamplingTests
{
    private static List<string> Files(string folder, int count, string ext = "png") =>
        [.. Enumerable.Range(0, count).Select(i => $"{folder}/file{i}.{ext}")];

    private static string FolderOf(string path) => path.Split('/')[0];

    private static bool IsVideo(string path) => path.EndsWith(".mp4", StringComparison.Ordinal);

    private static SystemRandomSource Rng(int seed) => new(new Random(seed));

    private static Dictionary<string, int> ByFolder(IEnumerable<string> result, params string[] folders) =>
        folders.ToDictionary(f => f, f => result.Count(p => FolderOf(p) == f));

    [Fact]
    public void even_split_across_folders_with_enough_files()
    {
        var folders = new Dictionary<string, List<string>> { ["a"] = Files("a", 10), ["b"] = Files("b", 10), ["c"] = Files("c", 10) };

        var result = ThumbnailSampling.PerFolder(folders, 21, Rng(0));

        Assert.Equal(new Dictionary<string, int> { ["a"] = 7, ["b"] = 7, ["c"] = 7 }, ByFolder(result, "a", "b", "c"));
        Assert.Equal(21, result.Count);
    }

    [Fact]
    public void uneven_split_shares_differ_by_at_most_one()
    {
        var folders = new Dictionary<string, List<string>> { ["a"] = Files("a", 10), ["b"] = Files("b", 10), ["c"] = Files("c", 10) };

        var result = ThumbnailSampling.PerFolder(folders, 22, Rng(0));

        var counts = ByFolder(result, "a", "b", "c").Values;
        Assert.True(counts.Max() - counts.Min() <= 1);
        Assert.Equal(22, result.Count);
    }

    [Fact]
    public void folder_shorter_than_its_share_contributes_only_what_it_has()
    {
        var folders = new Dictionary<string, List<string>> { ["a"] = Files("a", 2), ["b"] = Files("b", 10), ["c"] = Files("c", 10) };

        var counts = ByFolder(ThumbnailSampling.PerFolder(folders, 21, Rng(0)), "a", "b", "c");

        Assert.Equal(2, counts["a"]);
        Assert.Equal(counts["b"], counts["c"]);
    }

    [Fact]
    public void empty_folder_contributes_nothing()
    {
        var folders = new Dictionary<string, List<string>> { ["a"] = [], ["b"] = Files("b", 10) };

        var result = ThumbnailSampling.PerFolder(folders, 10, Rng(0));

        Assert.All(result, p => Assert.Equal("b", FolderOf(p)));
        Assert.Equal(10, result.Count);
    }

    [Fact]
    public void single_folder_gets_full_count()
    {
        var result = ThumbnailSampling.PerFolder(new Dictionary<string, List<string>> { ["a"] = Files("a", 10) }, 5, Rng(0));

        Assert.Equal(5, result.Count);
        Assert.All(result, p => Assert.Equal("a", FolderOf(p)));
    }

    [Fact]
    public void single_folder_fewer_files_than_requested() =>
        Assert.Equal(3, ThumbnailSampling.PerFolder(new Dictionary<string, List<string>> { ["a"] = Files("a", 3) }, 10, Rng(0)).Count);

    [Fact]
    public void zero_folders_returns_empty_list() =>
        Assert.Empty(ThumbnailSampling.PerFolder(new Dictionary<string, List<string>>(), 10, Rng(0)));

    [Fact]
    public void all_empty_folders_returns_empty_list() =>
        Assert.Empty(ThumbnailSampling.PerFolder(new Dictionary<string, List<string>> { ["a"] = [], ["b"] = [] }, 10, Rng(0)));

    [Fact]
    public void deterministic_with_injected_rng()
    {
        var folders = new Dictionary<string, List<string>> { ["a"] = Files("a", 10), ["b"] = Files("b", 10) };

        Assert.Equal(ThumbnailSampling.PerFolder(folders, 6, Rng(42)), ThumbnailSampling.PerFolder(folders, 6, Rng(42)));
    }

    [Fact]
    public void result_order_is_shuffled_not_grouped_by_folder()
    {
        var folders = new Dictionary<string, List<string>> { ["a"] = Files("a", 20), ["b"] = Files("b", 20) };

        var sequence = ThumbnailSampling.PerFolder(folders, 20, Rng(1)).Select(FolderOf).ToList();

        Assert.NotEqual(sequence.Order(), sequence);
    }

    [Fact]
    public void zero_total_count_returns_empty_list() =>
        Assert.Empty(ThumbnailSampling.PerFolder(new Dictionary<string, List<string>> { ["a"] = Files("a", 10) }, 0, Rng(0)));

    // No file is shown twice.
    [Fact]
    public void a_file_is_sampled_at_most_once()
    {
        var result = ThumbnailSampling.PerFolder(new Dictionary<string, List<string>> { ["a"] = Files("a", 10) }, 10, Rng(3));

        Assert.Equal(10, result.Distinct().Count());
    }

    // --- the grid ---

    [Fact]
    public void compute_thumbnail_grid_basic() => Assert.Equal((6, 4, 24), ThumbnailSampling.Grid(900, 650, 140, 140));

    [Fact]
    public void compute_thumbnail_grid_never_zero_columns_or_rows() => Assert.Equal((1, 1, 1), ThumbnailSampling.Grid(50, 50, 140, 140));

    [Fact]
    public void compute_thumbnail_grid_scales_with_viewport() =>
        Assert.True(ThumbnailSampling.Grid(1200, 800, 140, 140).Count > ThumbnailSampling.Grid(600, 400, 140, 140).Count);

    // 2 cells of 140 with 8 px spacing and 8 px margin on each side need 2*140 + 8 + 2*8 = 304 px - a
    // bare 300 px viewport would wrongly fit 2 columns by naive division.
    [Fact]
    public void compute_thumbnail_grid_accounts_for_spacing_and_margin()
    {
        Assert.Equal(1, ThumbnailSampling.Grid(300, 300, 140, 140, spacing: 8, margin: 8).Columns);
        Assert.Equal(2, ThumbnailSampling.Grid(304, 304, 140, 140, spacing: 8, margin: 8).Columns);
    }

    // Below cell + 2*margin even one column cannot fit, and the "at least one" guarantee overflows -
    // better one clipped cell than none. Above that floor the grid never overflows.
    [Fact]
    public void compute_thumbnail_grid_with_spacing_never_overflows_viewport()
    {
        const int cell = 140, spacing = 8, margin = 8;
        for (var width = cell + 2 * margin; width < 2000; width += 37)
        {
            var columns = ThumbnailSampling.Grid(width, 800, cell, cell, spacing, margin).Columns;
            Assert.True(columns * cell + (columns - 1) * spacing + 2 * margin <= width);
        }
    }

    // --- the video cap ---

    [Fact]
    public void video_cap_limits_total_videos_across_whole_sample()
    {
        var folders = new Dictionary<string, List<string>> { ["a"] = Files("a", 20, "mp4"), ["b"] = Files("b", 20), ["c"] = Files("c", 20) };

        var result = ThumbnailSampling.WithVideoCap(folders, 30, 4, IsVideo, Rng(0));

        Assert.Equal(4, result.Count(IsVideo));
        Assert.Equal(30, result.Count);
    }

    [Fact]
    public void video_cap_not_reached_when_fewer_videos_available_than_cap()
    {
        var folders = new Dictionary<string, List<string>> { ["a"] = Files("a", 2, "mp4"), ["b"] = Files("b", 20) };

        var result = ThumbnailSampling.WithVideoCap(folders, 10, 4, IsVideo, Rng(0));

        Assert.Equal(2, result.Count(IsVideo));
        Assert.Equal(10, result.Count);
    }

    [Fact]
    public void video_cap_of_zero_excludes_all_videos()
    {
        var folders = new Dictionary<string, List<string>> { ["a"] = Files("a", 20, "mp4"), ["b"] = Files("b", 20) };

        var result = ThumbnailSampling.WithVideoCap(folders, 10, 0, IsVideo, Rng(0));

        Assert.Equal(0, result.Count(IsVideo));
        Assert.Equal(10, result.Count);
    }

    [Fact]
    public void video_cap_with_no_non_video_files_available()
    {
        var result = ThumbnailSampling.WithVideoCap(new Dictionary<string, List<string>> { ["a"] = Files("a", 20, "mp4") }, 10, 4, IsVideo, Rng(0));

        Assert.Equal(4, result.Count(IsVideo));
        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void video_cap_result_is_shuffled_not_videos_first()
    {
        var folders = new Dictionary<string, List<string>> { ["a"] = Files("a", 20, "mp4"), ["b"] = Files("b", 20) };

        var sequence = ThumbnailSampling.WithVideoCap(folders, 20, 4, IsVideo, Rng(1)).Select(IsVideo).ToList();

        Assert.NotEqual(sequence.OrderDescending(), sequence);
    }
}
