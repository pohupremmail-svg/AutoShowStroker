namespace Gooner.Core.Tests.Support;

// The repository root, for tests that check what Gooner.Core says against the shared res/ tree -
// the one source both apps read from.
internal static class Repo
{
    private static readonly Lazy<string> RootDir = new(Find);

    public static string Root => RootDir.Value;

    public static string PathTo(string relative) => Path.Combine(Root, relative);

    private static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "res")) && Directory.Exists(Path.Combine(dir.FullName, "engine")))
                return dir.FullName;
        throw new DirectoryNotFoundException("The repository root was not found above " + AppContext.BaseDirectory);
    }
}
