using Gooner.Core.Media;

namespace Gooner.Core.Tests.Media;

// Port of tests/test_media_kinds.py.
public sealed class MediaKindsTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("gooner-media-");

    public void Dispose() => _dir.Delete(recursive: true);

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
}
