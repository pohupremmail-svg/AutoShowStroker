using Gooner.Core.Data;

namespace Gooner.Core.Tests.Data;

// res/ sits at the top of the repo, outside the Godot project, and next to the executable once
// exported - utils.get_project_root() answers the same question on the Python side.
public sealed class ResourceRootTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("gooner-res-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string MakeRes(string under)
    {
        var dir = Path.Combine(_root, under);
        Directory.CreateDirectory(Path.Combine(dir, "res", "callouts"));
        return dir;
    }

    [Fact]
    public void finds_res_beside_the_start()
    {
        var app = MakeRes("app");

        Assert.Equal(Path.Combine(app, "res"), ResourceRoot.Find([app]));
    }

    [Fact]
    public void walks_up_to_an_ancestor_that_has_it()
    {
        var repo = MakeRes("repo");
        var deep = Directory.CreateDirectory(Path.Combine(repo, "engine", "godot", ".godot")).FullName;

        Assert.Equal(Path.Combine(repo, "res"), ResourceRoot.Find([deep]));
    }

    [Fact]
    public void the_first_start_that_finds_one_wins()
    {
        var first = MakeRes("first");
        var second = MakeRes("second");

        Assert.Equal(Path.Combine(first, "res"), ResourceRoot.Find([first, second]));
    }

    // A res/ without callouts is somebody else's folder, not this app's.
    [Fact]
    public void a_res_folder_without_callouts_does_not_count()
    {
        var stranger = Directory.CreateDirectory(Path.Combine(_root, "stranger", "res")).Parent!.FullName;

        Assert.Null(ResourceRoot.Find([stranger]));
    }

    // An exported build has no res:// on disk, so Godot globalizes it to "" - a start that names
    // nothing, not the working directory.
    [Fact]
    public void an_empty_start_is_skipped()
    {
        var app = MakeRes("app");

        Assert.Equal(Path.Combine(app, "res"), ResourceRoot.Find(["", app]));
        Assert.Null(ResourceRoot.Find([""]));
    }

    [Fact]
    public void nothing_found_is_null() =>
        Assert.Null(ResourceRoot.Find([Directory.CreateDirectory(Path.Combine(_root, "empty")).FullName]));
}
