namespace Gooner.Build;

// What the release package is: Godot's export - the executable with its .pck embedded, the .NET
// assemblies, the FFmpeg extension - and beside it what the engine reads from disk rather than from
// the .pck (AppPaths): res/, the license texts, VERSION.
public static class Package
{
    public const string Executable = "GoonerApp.exe";

    // Where Godot puts the .NET assemblies and runtime: data_<assembly name>_<platform>_<arch>.
    public const string DataFolder = "data_Gooner.Engine_windows_x86_64";

    public static readonly IReadOnlyList<string> Assemblies = ["Gooner.Engine.dll", "Gooner.Core.dll", "GodotSharp.dll"];

    // The folders of res/ the engine reads, and nothing else: what goes is named, not what stays. The
    // rest of res/ is the Python app's (licenses: Qt, PyQt6, NumPy) or the developer's own and
    // gitignored - test figures in characters/, a licensed prop in props/, VRoid sources in vroids/ -
    // and a blocklist once packed the prop into a local build. A folder added to res/ later stays out
    // until it is named here.
    private static readonly string[] ResFolders = ["callouts", "locales", "icons"];

    // Every file that goes beside the executable, as (path in the package, path in the repository).
    public static IReadOnlyList<(string Relative, string Source)> BesideExecutable(Repository repo)
    {
        ArgumentNullException.ThrowIfNull(repo);
        var files = new List<(string, string)>();
        foreach (var file in Files(repo.Res))
        {
            var inRes = Path.GetRelativePath(repo.Res, file);
            var parts = inRes.Split(Path.DirectorySeparatorChar);
            // The click, the one file at the top of res/ the engine plays.
            var isClick = parts.Length == 1 && string.Equals(Path.GetExtension(file), ".wav", StringComparison.OrdinalIgnoreCase);
            if (!isClick && !(parts.Length > 1 && ResFolders.Contains(parts[0]))) continue;
            files.Add((Path.Combine("res", inRes), file));
        }
        files.AddRange(Files(repo.Licenses).Select(file => (Path.Combine("licenses", Path.GetRelativePath(repo.Licenses, file)), file)));
        files.Add(("VERSION", repo.Version));
        return files;
    }

    // The FFmpeg extension's libraries as the export copies them beside the executable: the release
    // build of the wrapper and FFmpeg itself - not the debug build, nor the copy the editor makes of
    // it while it runs (~libgdffmpeg...).
    public static IReadOnlyList<string> FfmpegLibraries(Repository repo)
    {
        ArgumentNullException.ThrowIfNull(repo);
        if (!Directory.Exists(repo.FfmpegLibraries)) return [];
        return [.. Directory.GetFiles(repo.FfmpegLibraries, "*.dll")
            .Where(f => !Path.GetFileName(f).StartsWith('~') && !Path.GetFileName(f).Contains("template_debug", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];
    }

    public static void AddBesideExecutable(Repository repo, string outDir)
    {
        foreach (var (relative, source) in BesideExecutable(repo))
        {
            var target = Path.Combine(outDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
        }
    }

    private static IEnumerable<string> Files(string dir) =>
        Directory.Exists(dir) ? Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal) : [];
}
