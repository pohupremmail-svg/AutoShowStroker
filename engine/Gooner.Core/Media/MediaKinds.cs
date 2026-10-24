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
    //
    // A junction or a symlink is followed - it is how a folder from another drive is hung into a
    // collection - but every real folder is walked once: a junction back up the tree was walked
    // round and round until the path grew too long, and that one error then cost the whole folder.
    // A file reached through a link keeps the link's path, as it was found.
    public static List<string> FindSupportedFiles(string folder)
    {
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 };
        var found = new List<string>();
        var walked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<(string Path, string Real)>();
        if (RealFolder(folder) is { } root) pending.Push((folder, root));
        while (pending.Count > 0)
        {
            var (dir, real) = pending.Pop();
            if (!walked.Add(real)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*", options))
                    if (SupportedExtensions.Contains(Path.GetExtension(file)))
                        found.Add(file);
                foreach (var sub in Directory.EnumerateDirectories(dir, "*", options))
                    if (RealFolder(sub, Path.Combine(real, Path.GetFileName(sub))) is { } subReal)
                        pending.Push((sub, subReal));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // This subtree only; what was found elsewhere stays.
            }
        }
        return found;
    }

    // Where a folder really is: a link's final target, anything else where it already stands
    // (`within`, the path through real folders it was reached by). Null for a link that leads nowhere.
    private static string? RealFolder(string dir, string? within = null)
    {
        try
        {
            var info = new DirectoryInfo(dir);
            if (info.LinkTarget is null) return Path.TrimEndingDirectorySeparator(Path.GetFullPath(within ?? dir));
            return info.ResolveLinkTarget(returnFinalTarget: true) is { Exists: true } target
                ? Path.TrimEndingDirectorySeparator(target.FullName)
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static HashSet<string> Set(params string[] items) => new(items, StringComparer.OrdinalIgnoreCase);
}
