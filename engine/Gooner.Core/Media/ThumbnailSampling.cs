using Gooner.Core.Randomness;

namespace Gooner.Core.Media;

// Port of src/thumbnail_sampling.py: which files the folder picker shows in its preview grid, and
// how many cells fit.
public static class ThumbnailSampling
{
    // Up to `total` files, split as evenly as possible across the folders that have any. A folder
    // with fewer files than its share contributes what it has; the shortfall is not handed to the
    // others, so the result can come out shorter. Shuffled, so the grid is not grouped by folder.
    public static List<string> PerFolder(IReadOnlyDictionary<string, List<string>> folderFiles, int total, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(folderFiles);
        ArgumentNullException.ThrowIfNull(random);
        var folders = folderFiles.Values.Where(files => files.Count > 0).ToList();
        if (folders.Count == 0 || total <= 0) return [];
        var baseShare = total / folders.Count;
        var remainder = total % folders.Count;
        var result = new List<string>();
        for (var i = 0; i < folders.Count; i++)
        {
            var share = baseShare + (i < remainder ? 1 : 0);
            result.AddRange(Sample(folders[i], Math.Min(share, folders[i].Count), random));
        }
        Shuffle(result, random);
        return result;
    }

    // Like PerFolder, but never more than `maxVideos` videos across the whole sample, whatever the
    // folder: a video cell is far heavier than an image, so this is a global cap, not a per-folder one.
    public static List<string> WithVideoCap(IReadOnlyDictionary<string, List<string>> folderFiles, int total, int maxVideos,
        Func<string, bool> isVideo, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(folderFiles);
        ArgumentNullException.ThrowIfNull(isVideo);
        var videos = PerFolder(folderFiles.ToDictionary(f => f.Key, f => f.Value.Where(isVideo).ToList()), Math.Min(maxVideos, total), random);
        var others = PerFolder(folderFiles.ToDictionary(f => f.Key, f => f.Value.Where(p => !isVideo(p)).ToList()), total - videos.Count, random);
        var result = videos.Concat(others).ToList();
        Shuffle(result, random);
        return result;
    }

    // How many cells of the given size fit the viewport, as (columns, rows, count). Spacing and margin
    // have to be those of the real layout: N cells need N*cell + (N-1)*spacing + 2*margin, and leaving
    // that out overflows the viewport. Never less than one of each.
    public static (int Columns, int Rows, int Count) Grid(int width, int height, int cellWidth, int cellHeight, int spacing = 0, int margin = 0)
    {
        var usableWidth = Math.Max(0, width - 2 * margin);
        var usableHeight = Math.Max(0, height - 2 * margin);
        var columns = Math.Max(1, (usableWidth + spacing) / (cellWidth + spacing));
        var rows = Math.Max(1, (usableHeight + spacing) / (cellHeight + spacing));
        return (columns, rows, columns * rows);
    }

    // random.sample: k distinct items, in random order.
    private static List<string> Sample(List<string> items, int k, IRandomSource random)
    {
        var pool = items.ToList();
        for (var i = 0; i < k; i++)
        {
            var j = i + random.NextIndex(pool.Count - i);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        return pool.GetRange(0, k);
    }

    private static void Shuffle(List<string> items, IRandomSource random)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = random.NextIndex(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
