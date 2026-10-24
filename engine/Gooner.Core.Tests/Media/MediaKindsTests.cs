using Gooner.Core.Media;
using Gooner.Core.Tests.Support;

namespace Gooner.Core.Tests.Media;

// Port of tests/test_media_kinds.py.
public sealed class MediaKindsTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("gooner-media-");
    private readonly List<string> _junctions = [];

    // The junctions go first, each on its own: deleted non-recursively, only the link goes, never what
    // it points at - and a recursive delete refuses one ("access denied").
    public void Dispose()
    {
        foreach (var link in _junctions) Directory.Delete(link);
        _dir.Delete(recursive: true);
    }

    private void Touch(string relative)
    {
        var path = Path.Combine(_dir.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0x78]);
    }

    [Fact]
    public void extension_sets_are_disjoint()
    {
        Assert.Empty(MediaKinds.ImageExtensions.Intersect(MediaKinds.GifExtensions));
        Assert.Empty(MediaKinds.ImageExtensions.Intersect(MediaKinds.VideoExtensions));
        Assert.Empty(MediaKinds.GifExtensions.Intersect(MediaKinds.VideoExtensions));
    }

    [Fact]
    public void supported_extensions_matches_original_hardcoded_list()
    {
        string[] original = [".mp4", ".avi", ".mov", ".mkv", ".gif", ".jpeg", ".jpg", ".png", ".bmp"];
        Assert.True(MediaKinds.SupportedExtensions.SetEquals(original));
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".bmp")]
    public void media_kind_recognizes_images(string ext) =>
        Assert.Equal(MediaKind.Image, MediaKinds.Of("file" + ext));

    [Fact]
    public void media_kind_recognizes_gif() => Assert.Equal(MediaKind.Gif, MediaKinds.Of("file.gif"));

    [Theory]
    [InlineData(".mp4")]
    [InlineData(".avi")]
    [InlineData(".mov")]
    [InlineData(".mkv")]
    public void media_kind_recognizes_video(string ext) =>
        Assert.Equal(MediaKind.Video, MediaKinds.Of("file" + ext));

    [Fact]
    public void media_kind_is_case_insensitive()
    {
        Assert.Equal(MediaKind.Image, MediaKinds.Of("file.PNG"));
        Assert.Equal(MediaKind.Gif, MediaKinds.Of("file.GIF"));
        Assert.Equal(MediaKind.Video, MediaKinds.Of("file.MP4"));
    }

    [Fact]
    public void media_kind_unknown_extension() => Assert.Equal(MediaKind.Unknown, MediaKinds.Of("file.txt"));

    [Fact]
    public void find_supported_files_finds_all_supported_extensions()
    {
        foreach (var ext in MediaKinds.SupportedExtensions) Touch("file" + ext);
        Touch("file.txt");

        var found = MediaKinds.FindSupportedFiles(_dir.FullName).Select(Path.GetFileName).ToHashSet();

        Assert.True(found.SetEquals(MediaKinds.SupportedExtensions.Select(ext => "file" + ext)));
    }

    [Fact]
    public void find_supported_files_searches_recursively()
    {
        Touch(Path.Combine("sub", "deep.png"));

        Assert.Contains(MediaKinds.FindSupportedFiles(_dir.FullName), f => Path.GetFileName(f) == "deep.png");
    }

    [Fact]
    public void find_supported_files_empty_folder() => Assert.Empty(MediaKinds.FindSupportedFiles(_dir.FullName));

    // The Python test forces a PermissionError mid-walk. The C# walk skips what it cannot read
    // by construction (IgnoreInaccessible); what is left to pin is that a folder that cannot
    // be listed at all yields nothing rather than an exception.
    [Fact]
    public void find_supported_files_survives_a_folder_it_cannot_list() =>
        Assert.Empty(MediaKinds.FindSupportedFiles(Path.Combine(_dir.FullName, "does-not-exist")));

    // A junction is how Windows hangs a folder from elsewhere into a tree, and the walk follows one
    // as the Python one does - but a junction back up the tree used to be walked round and round
    // until the path grew too long, and that error then cost the whole folder: nothing was found.
    private void Junction(string relative, string target)
    {
        var link = Path.Combine(_dir.FullName, relative);
        using var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            ArgumentList = { "/c", "mklink", "/J", link, Path.Combine(_dir.FullName, target) },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        })!;
        mklink.WaitForExit();
        _junctions.Add(link);
        Assert.True(Directory.Exists(link), "mklink /J failed");
    }

    [WindowsFact]
    public void a_junction_back_up_the_tree_is_walked_once()
    {
        Touch(Path.Combine("media", "a.png"));
        Touch(Path.Combine("media", "sub", "b.gif"));
        Junction(Path.Combine("media", "sub", "back"), "media");

        var found = MediaKinds.FindSupportedFiles(Path.Combine(_dir.FullName, "media"));

        Assert.Equal(["a.png", "b.gif"], found.Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [WindowsFact]
    public void a_folder_hung_in_by_a_junction_is_found_under_the_junctions_name()
    {
        Touch(Path.Combine("media", "a.png"));
        Touch(Path.Combine("elsewhere", "c.mp4"));
        Junction(Path.Combine("media", "linked"), "elsewhere");

        var found = MediaKinds.FindSupportedFiles(Path.Combine(_dir.FullName, "media"));

        Assert.Contains(Path.Combine(_dir.FullName, "media", "linked", "c.mp4"), found);
        Assert.Equal(2, found.Count);
    }

    [WindowsFact]
    public void a_folder_reached_twice_is_listed_once()
    {
        Touch(Path.Combine("elsewhere", "c.mp4"));
        Directory.CreateDirectory(Path.Combine(_dir.FullName, "media"));
        Junction(Path.Combine("media", "one"), "elsewhere");
        Junction(Path.Combine("media", "two"), "elsewhere");

        Assert.Single(MediaKinds.FindSupportedFiles(Path.Combine(_dir.FullName, "media")));
    }

    // One subtree that cannot be walked costs that subtree, not what was found everywhere else.
    [WindowsFact]
    public void a_junction_whose_target_is_gone_costs_nothing_else()
    {
        Touch(Path.Combine("media", "a.png"));
        Touch(Path.Combine("gone", "x.png"));
        Junction(Path.Combine("media", "dangling"), "gone");
        Directory.Delete(Path.Combine(_dir.FullName, "gone"), recursive: true);

        Assert.Equal(["a.png"], MediaKinds.FindSupportedFiles(Path.Combine(_dir.FullName, "media")).Select(Path.GetFileName));
    }
}
