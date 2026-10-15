namespace Gooner.Core.Media;

public enum MediaKind
{
    Unknown,
    Image,
    Gif,
    Video,
}

// Port of src/media_kinds.py.
public static class MediaKinds
{
    public static readonly IReadOnlySet<string> ImageExtensions = Set(".png", ".jpg", ".jpeg", ".bmp");
    public static readonly IReadOnlySet<string> GifExtensions = Set(".gif");
    public static readonly IReadOnlySet<string> VideoExtensions = Set(".mp4", ".avi", ".mov", ".mkv");
    public static readonly IReadOnlySet<string> SupportedExtensions =
        Set([.. ImageExtensions, .. GifExtensions, .. VideoExtensions]);

    public static MediaKind Of(string path)
    {
        var ext = Path.GetExtension(path);
        if (ImageExtensions.Contains(ext)) return MediaKind.Image;
        if (GifExtensions.Contains(ext)) return MediaKind.Gif;
        if (VideoExtensions.Contains(ext)) return MediaKind.Video;
        return MediaKind.Unknown;
    }

    // Every supported file under the folder, recursively. A subfolder that cannot be read is
    // skipped rather than aborting the walk, and a folder that cannot be listed at all yields
    // nothing - better whatever was found than a picker that crashes over one bad subtree.
    // Filtering by suffix costs nothing extra here: the directory listing already says which
    // entries are files, so there is no per-entry stat to save (the reason the Python version
    // checks the suffix first).
    public static List<string> FindSupportedFiles(string folder)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
        };
        try
        {
            return Directory.EnumerateFiles(folder, "*", options)
                .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static HashSet<string> Set(params string[] items) => new(items, StringComparer.OrdinalIgnoreCase);
}
