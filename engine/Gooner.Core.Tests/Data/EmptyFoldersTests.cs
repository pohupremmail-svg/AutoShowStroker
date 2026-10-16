using Gooner.Core.Data;

namespace Gooner.Core.Tests.Data;

// Godot makes its user:// folder at every start, even when nothing is written there; the engine
// takes it away again - and the parents made for it - as long as each is empty.
public sealed class EmptyFoldersTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("gooner-empty-");

    public void Dispose() => _root.Delete(recursive: true);

    // <root>/Godot/app_userdata/GoonerApp, as Godot makes it.
    private string UserDir() =>
        Directory.CreateDirectory(Path.Combine(_root.FullName, "Godot", "app_userdata", "GoonerApp")).FullName;

    [Fact]
    public void an_empty_folder_goes_with_its_empty_parents()
    {
        var dir = UserDir();

        Assert.Equal(3, EmptyFolders.RemoveWithParents(dir, parents: 2));

        Assert.False(Directory.Exists(Path.Combine(_root.FullName, "Godot")));
        Assert.True(_root.Exists);
    }

    [Fact]
    public void a_parent_holding_anything_else_stays()
    {
        var dir = UserDir();
        Directory.CreateDirectory(Path.Combine(_root.FullName, "Godot", "app_userdata", "Some Other Game"));

        Assert.Equal(1, EmptyFolders.RemoveWithParents(dir, parents: 2));

        Assert.False(Directory.Exists(dir));
        Assert.True(Directory.Exists(Path.Combine(_root.FullName, "Godot", "app_userdata", "Some Other Game")));
    }

    [Fact]
    public void a_folder_with_anything_in_it_stays()
    {
        var dir = UserDir();
        File.WriteAllText(Path.Combine(dir, "something.cfg"), "");

        Assert.Equal(0, EmptyFolders.RemoveWithParents(dir, parents: 2));

        Assert.True(File.Exists(Path.Combine(dir, "something.cfg")));
    }

    [Fact]
    public void never_higher_than_asked()
    {
        var dir = UserDir();

        Assert.Equal(2, EmptyFolders.RemoveWithParents(dir, parents: 1));

        Assert.True(Directory.Exists(Path.Combine(_root.FullName, "Godot")));
    }

    [Fact]
    public void a_missing_folder_is_nothing_to_do() =>
        Assert.Equal(0, EmptyFolders.RemoveWithParents(Path.Combine(_root.FullName, "missing"), parents: 2));

    [Fact]
    public void an_empty_path_is_nothing_to_do() => Assert.Equal(0, EmptyFolders.RemoveWithParents("", parents: 2));
}
