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

    // res/licenses is the Python app's - Qt, PyQt6, NumPy - none of which ships with the engine.
    private const string PythonLicenses = "licenses";

    // Every file that goes beside the executable, as (path in the package, path in the repository).
    public static IReadOnlyList<(string Relative, string Source)> BesideExecutable(Repository repo)
    {
        ArgumentNullException.ThrowIfNull(repo);
        var files = new List<(string, string)>();
        foreach (var file in Files(repo.Res))
        {
            var inRes = Path.GetRelativePath(repo.Res, file);
            if (inRes.Split(Path.DirectorySeparatorChar)[0] == PythonLicenses) continue;
            // The developer's own test figures: gigabytes, and none the app may pass on yet.
            if (string.Equals(Path.GetExtension(file), ".vrm", StringComparison.OrdinalIgnoreCase)) continue;
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
