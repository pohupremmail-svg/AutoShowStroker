using System;
using System.IO;
using System.Linq;
using Gooner.Core.Data;
using Gooner.Core.Migration;
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
    // repo's engine/licenses from a dev run. An exported build has no res:// on disk.
    public static string? Licenses { get; } = new[]
    {
        Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? "", "licenses"),
        ProjectSettings.GlobalizePath("res://") is { Length: > 0 } project
            ? Path.GetFullPath(Path.Combine(project, "..", "licenses"))
            : "",
    }.FirstOrDefault(Directory.Exists);

    // The data folder the Python app used too, so an install of it is carried over at the first
    // start: its registry key and what is left in its old roaming folder are imported, and the import
    // deletes what it has carried over. -- --data-dir=<path> puts it anywhere else.
    public static string DataDir { get; } = UserArg("data-dir") ?? UserDataStore.DefaultBaseDir;

    // What is imported from, on the app's own data folder only. A folder given with --data-dir
    // imports nothing unless told where from - --legacy-registry=<key under HKEY_CURRENT_USER> and
    // --legacy-data-dir=<folder>, which is how a copy of a real profile is tried out.
    private static bool OwnProfile => UserArg("data-dir") is null;

    public static ILegacySettings? LegacySettings()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var key = UserArg("legacy-registry") ?? (OwnProfile ? WindowsLegacySettings.PythonAppKey : null);
        return key is null ? null : new WindowsLegacySettings(key);
    }

    public static string? LegacyDataDir => UserArg("legacy-data-dir") ?? (OwnProfile ? UserDataStore.LegacyBaseDir : null);

    // The value of `-- --name=value`, or null.
    public static string? UserArg(string name)
    {
        var prefix = $"--{name}=";
        return OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
    }

    public static bool UserFlag(string name) => OS.GetCmdlineUserArgs().Contains($"--{name}");
}
