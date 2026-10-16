using System.Text;

namespace Gooner.Build;

// verify_export: what scripts/verify_frozen.py is to the PyInstaller .exe. Reads a package and lists
// what is wrong with it - a part missing, a copy that differs from the repository, a private path
// inside a file. Do not publish a package it refuses.
public static class ExportCheck
{
    // Paths that must appear in no file of the package: the builder's home folder, and with it their
    // name. A debug record or a stack trace with a build path would carry it.
    public static IReadOnlyList<string> PrivatePaths() => [Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)];

    public static IReadOnlyList<string> Problems(Repository repo, string outDir, IEnumerable<string> privatePaths)
    {
        ArgumentNullException.ThrowIfNull(repo);
        var problems = new List<string>();
        if (!Directory.Exists(outDir)) return [$"There is no package at {outDir}."];

        if (!File.Exists(Path.Combine(outDir, Package.Executable)))
            problems.Add($"{Package.Executable} is missing.");
        foreach (var assembly in Package.Assemblies)
            if (!File.Exists(Path.Combine(outDir, Package.DataFolder, assembly)))
                problems.Add($"{Package.DataFolder}/{assembly} is missing.");

        foreach (var library in Package.FfmpegLibraries(repo))
            Same(problems, library, Path.Combine(outDir, Path.GetFileName(library)), Path.GetFileName(library));
        if (Package.FfmpegLibraries(repo).Count == 0)
            problems.Add($"The FFmpeg extension is missing from {repo.FfmpegLibraries}; video would not play.");

        foreach (var (relative, source) in Package.BesideExecutable(repo))
            Same(problems, source, Path.Combine(outDir, relative), relative.Replace('\\', '/'));
        if (Directory.Exists(Path.Combine(outDir, "res", "licenses")))
            problems.Add("res/licenses is the Python app's license folder and must not ship with the engine.");

        problems.AddRange(Holding(outDir, privatePaths));
        return problems;
    }

    private static void Same(List<string> problems, string source, string packaged, string name)
    {
        if (!File.Exists(packaged))
            problems.Add($"{name} is missing.");
        else if (!File.ReadAllBytes(packaged).AsSpan().SequenceEqual(File.ReadAllBytes(source)))
            problems.Add($"{name} differs from the repository's.");
    }

    // The files holding any of the paths - UTF-8 or UTF-16, either slash, any case. Names the file,
    // never the path: that would print the very thing being kept out.
    private static IEnumerable<string> Holding(string outDir, IEnumerable<string> privatePaths)
    {
        var needles = privatePaths.Where(p => p.Length > 0)
            .SelectMany(p => new[] { p, p.Replace('\\', '/') })
            .SelectMany(p => new[] { Encoding.UTF8.GetBytes(p.ToLowerInvariant()), Encoding.Unicode.GetBytes(p.ToLowerInvariant()) })
            .ToList();
        foreach (var file in Directory.GetFiles(outDir, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var data = File.ReadAllBytes(file);
            Lower(data);
            if (needles.Exists(needle => data.AsSpan().IndexOf(needle) >= 0))
                yield return $"{Path.GetRelativePath(outDir, file).Replace('\\', '/')} contains a private path (the builder's home folder).";
        }
    }

    // ASCII lower case in place - enough for a path's letters, in UTF-8 and in UTF-16 alike.
    private static void Lower(byte[] data)
    {
        for (var i = 0; i < data.Length; i++)
            if (data[i] is >= (byte)'A' and <= (byte)'Z')
                data[i] = (byte)(data[i] + 32);
    }
}
