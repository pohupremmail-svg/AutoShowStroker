namespace Gooner.Core.Data;

// Where res/ is: the callouts, the click, the achievement marks - the one copy both apps read.
// It sits at the top of the repo, outside the Godot project, and beside the executable once
// exported; utils.get_project_root() answers the same question on the Python side. Read from the
// file system rather than through Godot's res://, so the files never go through its importer
// and the engine reads exactly what the Python app reads.
public static class ResourceRoot
{
    // The first res/ found walking up from each start in turn. It has to hold callouts/ to count,
    // so an unrelated res/ further up the tree is never mistaken for this one.
    public static string? Find(IEnumerable<string> starts)
    {
        ArgumentNullException.ThrowIfNull(starts);
        foreach (var start in starts)
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var res = Path.Combine(dir.FullName, "res");
                if (Directory.Exists(Path.Combine(res, "callouts")))
                    return res;
            }
        return null;
    }
}
