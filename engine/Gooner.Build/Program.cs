using System.Diagnostics;
using System.IO.Compression;
using Gooner.Build;

// Builds the release package and checks it. From the repo root:
//
//   dotnet run --project engine/Gooner.Build -- build --godot <Godot .NET console exe> [--out DIR] [--zip]
//   dotnet run --project engine/Gooner.Build -- verify [--dir DIR]
//
// `build` exports the Godot project (export templates for that Godot version installed), puts res/,
// the license texts and VERSION beside the executable, starts the result once and verifies it;
// `--zip` then packs it as dist/GoonerApp-<version>-windows-x64.zip. `--godot` may also come from
// the GODOT environment variable. The package lands in dist/GoonerApp unless --out says otherwise.
// Only ever builds - nothing here talks to GitHub or any other server.

var command = args.FirstOrDefault();
var repo = Repository.Find(AppContext.BaseDirectory);
string? Option(string name)
{
    var at = Array.IndexOf(args, name);
    return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
}
var outDir = Path.GetFullPath(Option("--out") ?? Option("--dir") ?? Path.Combine(repo.Dist, "GoonerApp"));

switch (command)
{
    case "build":
        return Build(Option("--godot") ?? Environment.GetEnvironmentVariable("GODOT"), args.Contains("--zip"));
    case "verify":
        return Verify(runCheck: true);
    default:
        Console.Error.WriteLine("usage: build --godot <Godot .NET console exe> [--out DIR] [--zip] | verify [--dir DIR]");
        return 2;
}

int Build(string? godot, bool zip)
{
    if (godot is null || !File.Exists(godot))
    {
        Console.Error.WriteLine("Name the Godot .NET binary: --godot <path> or the GODOT environment variable.");
        return 2;
    }
    if (Package.FfmpegLibraries(repo).Count == 0)
    {
        Console.Error.WriteLine($"The FFmpeg extension is missing from {repo.FfmpegLibraries}; it is committed in engine/godot/addons/ffmpeg - see CLAUDE.md.");
        return 2;
    }
    // Only ever a package of ours is replaced - never a folder someone pointed --out at by mistake.
    if (Directory.Exists(outDir) && Directory.EnumerateFileSystemEntries(outDir).Any()
        && !File.Exists(Path.Combine(outDir, Package.Executable)))
    {
        Console.Error.WriteLine($"{outDir} is not empty and holds no earlier package; not touching it.");
        return 2;
    }
    if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
    Directory.CreateDirectory(outDir);

    Console.WriteLine($"Exporting to {outDir} ...");
    var export = new ProcessStartInfo(godot) { UseShellExecute = false };
    foreach (var arg in new[] { "--headless", "--path", repo.GodotProject, "--export-release", "Windows Desktop", Path.Combine(outDir, Package.Executable) })
        export.ArgumentList.Add(arg);
    using (var process = Process.Start(export)!)
    {
        if (!process.WaitForExit(TimeSpan.FromMinutes(30)))
        {
            process.Kill(entireProcessTree: true);
            Console.Error.WriteLine("The export did not finish within 30 minutes.");
            return 1;
        }
        // Godot reports some failures with exit code 0; the executable is what tells.
        if (process.ExitCode != 0 || !File.Exists(Path.Combine(outDir, Package.Executable)))
        {
            Console.Error.WriteLine($"The export failed (exit code {process.ExitCode}).");
            return 1;
        }
    }
    Package.AddBesideExecutable(repo, outDir);

    var result = Verify(runCheck: true);
    if (result != 0 || !zip) return result;

    var version = File.ReadAllText(repo.Version).Trim();
    var archive = Path.Combine(repo.Dist, $"GoonerApp-{version}-windows-x64.zip");
    File.Delete(archive);
    ZipFile.CreateFromDirectory(outDir, archive, CompressionLevel.Optimal, includeBaseDirectory: true);
    Console.WriteLine($"Packed {archive}");
    return 0;
}

int Verify(bool runCheck)
{
    var problems = new List<string>(ExportCheck.Problems(repo, outDir, ExportCheck.PrivatePaths()));
    if (runCheck && OperatingSystem.IsWindows())
        problems.AddRange(RunCheck.Problems(outDir, note => Console.WriteLine("note: " + note)));
    if (problems.Count == 0)
    {
        Console.WriteLine($"OK: {outDir}");
        return 0;
    }
    Console.Error.WriteLine($"{problems.Count} problem(s) with {outDir} - do not publish it:");
    foreach (var problem in problems)
        Console.Error.WriteLine("  - " + problem);
    return 1;
}
