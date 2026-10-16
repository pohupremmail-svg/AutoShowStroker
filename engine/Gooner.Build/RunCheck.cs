using System.Diagnostics;

namespace Gooner.Build;

// Starts the package as it would start on a user's machine - no editor, no repository, no res:// on
// disk - just far enough to find its files: `--headless -- --smoke` exits 0 once the engine has found
// res/ beside the executable. Headless, so it runs on a build machine without a GPU.
//
// Then Godot's user folder: Godot makes %APPDATA%\Godot\app_userdata\GoonerApp at every start, and
// the app removes it again (Main.RemoveGodotUserDir). Where it was already there before the run - a
// development machine, where the editor keeps its own things in it - that cannot be told apart, and
// the check says so instead of passing.
public static class RunCheck
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    public static string GodotUserDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Godot", "app_userdata", "GoonerApp");

    public static IReadOnlyList<string> Problems(string outDir, Action<string> note)
    {
        ArgumentNullException.ThrowIfNull(note);
        var problems = new List<string>();
        var exe = Path.Combine(outDir, Package.Executable);
        if (!File.Exists(exe)) return [$"{Package.Executable} is missing - nothing to start."];

        // The export itself leaves it behind, holding nothing but empty folders (the editor's
        // objectdb_snapshots); cleared first, or the check could never tell on any machine.
        if (Directory.Exists(GodotUserDir) && !Directory.EnumerateFiles(GodotUserDir, "*", SearchOption.AllDirectories).Any())
            Directory.Delete(GodotUserDir, recursive: true);
        var userDirBefore = Directory.Exists(GodotUserDir);
        var data = Directory.CreateTempSubdirectory("gooner-runcheck-");
        try
        {
            var start = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = data.FullName,
            };
            foreach (var arg in new[] { "--headless", "--", "--smoke", $"--data-dir={data.FullName}" })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(Limit))
            {
                process.Kill(entireProcessTree: true);
                problems.Add($"The package did not exit within {Limit.TotalSeconds:0} s of a smoke start - it most likely failed to start.");
            }
            else if (process.ExitCode != 0)
                problems.Add($"A smoke start exited with {process.ExitCode} - the engine did not find res/ beside the executable.");
            if (problems.Count > 0)
                note(Tail(output.Result + errors.Result));
            if (Directory.EnumerateFileSystemEntries(data.FullName).Any())
                problems.Add("A smoke start wrote into the data folder; it should not get that far.");
        }
        finally
        {
            data.Delete(recursive: true);
        }

        if (userDirBefore)
            note($"Godot's user folder existed before the run (development runs), so whether the package leaves it behind was not checked: {GodotUserDir}");
        else if (Directory.Exists(GodotUserDir))
            problems.Add($"The package left Godot's user folder behind: {GodotUserDir}");
        return problems;
    }

    private static string Tail(string text)
    {
        var lines = text.Split('\n');
        return string.Join('\n', lines.Skip(Math.Max(0, lines.Length - 15)));
    }
}
