using System;
using System.IO;
using System.Linq;
using Gooner.Core.Data;
using Godot;

namespace Gooner.Engine;

// Where the engine finds what it reads from disk, and where it keeps what it writes.
public static class AppPaths
{
    // res/ beside the executable once exported; from the editor or a dev run, the repo's own,
    // found by walking up from the Godot project.
    public static string? Resources { get; } = ResourceRoot.Find([
        Path.GetDirectoryName(OS.GetExecutablePath()) ?? "",
        ProjectSettings.GlobalizePath("res://"),
    ]);

    // The license texts the program carries: licenses/ beside the executable once exported, the
    // repo's engine/licenses from a dev run.
    public static string? Licenses { get; } = new[]
    {
        Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? "", "licenses"),
        Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "licenses")),
    }.FirstOrDefault(Directory.Exists);

    // Until the data migration (phase 4) is tested against a copy of a real profile, the engine
    // keeps a folder of its own beside the Python app's, so a preview run can never change what the
    // installed app remembers. -- --data-dir=<path> puts it anywhere else.
    public static string DataDir { get; } = UserArg("data-dir") ?? Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "GoonerCock", "GoonerApp.EnginePreview");

    // The value of `-- --name=value`, or null.
    public static string? UserArg(string name)
    {
        var prefix = $"--{name}=";
        return OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
    }

    public static bool UserFlag(string name) => OS.GetCmdlineUserArgs().Contains($"--{name}");
}
