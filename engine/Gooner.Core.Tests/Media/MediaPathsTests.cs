using Gooner.Core.Media;

namespace Gooner.Core.Tests.Media;

// Which paths of a saved session the app will touch. A session file is meant to be handed on and
// written by an LLM, so its paths are whatever someone put there: a share on another machine is a
// connection out that carries the Windows account's credentials, and a URL or a shell scheme handed
// to the shell runs whatever is registered for it.
public class MediaPathsTests
{
    [Theory]
    [InlineData(@"C:\pics\a.png")]
    [InlineData(@"D:/clips/b.MP4")]
    [InlineData(@"Z:\mapped drive\c.gif")]
    [InlineData(@"C:\pics\with spaces\d.jpeg")]
    public void a_picture_or_clip_on_a_drive_of_this_computer_is_one(string path) =>
        Assert.True(MediaPaths.IsOnThisComputer(path));

    [Theory]
    [InlineData(@"\\host\share\a.png")]
    [InlineData(@"\\host@80\share\a.png")]
    [InlineData(@"//host/share/a.png")]
    [InlineData(@"\\?\C:\pics\a.png")]
    [InlineData(@"\\.\C:\pics\a.png")]
    [InlineData("https://example.org/a.png")]
    [InlineData("file:///C:/pics/a.png")]
    [InlineData(@"search-ms:query=a&crumb=location:\\host\share\a.png")]
    [InlineData("a.png")]
    [InlineData(@"\pics\a.png")]
    [InlineData(@"C:pics\a.png")]
    [InlineData(@"C:\pics\a.txt")]
    [InlineData(@"C:\pics\a.exe")]
    [InlineData(@"C:\pics\a.png:hidden")]
    [InlineData("C:\\pics\\a\0.png")]
    [InlineData("")]
    public void anything_else_is_not(string path) =>
        Assert.False(MediaPaths.IsOnThisComputer(path));

    // What the shell may be handed: the user's own files, which may sit on a share they picked as a
    // media folder - but never a URL, a scheme or a device path.
    [Theory]
    [InlineData(@"C:\pics\a.png")]
    [InlineData(@"\\nas\media\a.png")]
    public void the_shell_is_handed_a_file_of_the_users(string path) =>
        Assert.True(MediaPaths.IsAFile(path));

    [Theory]
    [InlineData("https://example.org/a.png")]
    [InlineData(@"search-ms:query=a&crumb=location:\\host\share\a.png")]
    [InlineData(@"\\?\C:\pics\a.png")]
    [InlineData(@"\\.\pipe\a.png")]
    [InlineData(@"\\host")]
    [InlineData(@"C:\pics\a.exe")]
    [InlineData("a.png")]
    public void and_nothing_else(string path) =>
        Assert.False(MediaPaths.IsAFile(path));
}
