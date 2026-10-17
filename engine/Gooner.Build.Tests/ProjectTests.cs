namespace Gooner.Build.Tests;

// What the Godot project is set to, held where a change to it would otherwise go unnoticed: what
// Godot writes on the user's machine, and what the export carries.
public class ProjectTests
{
    private static readonly Repository Repo = Repository.Find(AppContext.BaseDirectory);

    private static string ProjectFile => File.ReadAllText(Path.Combine(Repo.GodotProject, "project.godot"));

    private static string ExportPresets => File.ReadAllText(Path.Combine(Repo.GodotProject, "export_presets.cfg"));

    private static string Section(string text, string name)
    {
        var start = text.IndexOf($"[{name}]", StringComparison.Ordinal);
        Assert.True(start >= 0, $"[{name}] is missing");
        var end = text.IndexOf("\n[", start + 1, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }

    private static void Has(string section, string line) =>
        Assert.Contains(section.Split('\n').Select(l => l.Trim()), l => l == line);

    // Godot's file log is on by default for desktop exports; the app's own opt-in log is the only one.
    [Fact]
    public void godot_writes_no_log_file()
    {
        var debug = Section(ProjectFile, "debug");
        Has(debug, "file_logging/enable_file_logging=false");
        Has(debug, "file_logging/enable_file_logging.pc=false");
    }

    // Both would go to %APPDATA%\Godot\app_userdata\GoonerApp - a folder whose name alone says this
    // app ran here (measured: 5 MB after one start). Costs about 0.8 s per start.
    [Fact]
    public void godot_keeps_no_shader_or_pipeline_cache()
    {
        var rendering = Section(ProjectFile, "rendering");
        Has(rendering, "shader_compiler/shader_cache/enabled=false");
        Has(rendering, "rendering_device/pipeline_cache/enable=false");
    }

    [Fact]
    public void the_export_is_one_executable_without_a_console_or_debug_symbols()
    {
        var options = Section(ExportPresets, "preset.0.options");
        Has(Section(ExportPresets, "preset.0"), "name=\"Windows Desktop\"");
        Has(options, "binary_format/embed_pck=true");
        Has(options, "debug/export_console_wrapper=0");
        // Debug symbols carry the build machine's paths - and with them a name.
        Has(options, "dotnet/include_debug_symbols=false");
        Has(options, "application/icon=\"res://icon.ico\"");
    }

    // The LGPL lets FFmpeg go out only with its source, so every release carries the archive in
    // engine/third_party/ffmpeg. It has to be the revision the bundled libraries were built from:
    // their version string names it (N-<count>-g<first ten digits of the commit>).
    [Fact]
    public void the_ffmpeg_source_is_the_revision_the_libraries_were_built_from()
    {
        var archive = Path.GetFileName(Assert.Single(Directory.GetFiles(Repo.FfmpegSources, "FFmpeg-*-source.tar.gz")));
        var commit = archive["FFmpeg-".Length..^"-source.tar.gz".Length];
        var avutil = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(Repo.FfmpegLibraries, "avutil-58.dll")));

        Assert.Equal(40, commit.Length);
        Assert.Contains($"-g{commit[..10]}-", avutil, StringComparison.Ordinal);
    }

    // The notices say where the source is; the release check attaches what they name.
    [Fact]
    public void the_notices_name_the_source_archives_a_release_carries()
    {
        var notices = File.ReadAllText(Path.Combine(Repo.Licenses, "THIRD_PARTY_NOTICES.txt"));
        var archives = Directory.GetFiles(Repo.FfmpegSources, "*-source.tar.gz").Select(Path.GetFileName).ToList();

        Assert.Equal(2, archives.Count);
        foreach (var archive in archives)
            Assert.Contains(archive!, notices, StringComparison.Ordinal);
        Assert.Contains("engine/third_party/ffmpeg/*-source.tar.gz",
            File.ReadAllText(Path.Combine(Repo.Root, ".github", "workflows", "release-check.yml")), StringComparison.Ordinal);
    }

    // The executable's icon has to sit inside the Godot project for the export to embed it; it is a
    // copy of the one both apps use, and has to stay one.
    [Fact]
    public void the_executable_icon_is_the_apps() =>
        Assert.Equal(File.ReadAllBytes(Path.Combine(Repo.Res, "icons", "favicon.ico")),
            File.ReadAllBytes(Path.Combine(Repo.GodotProject, "icon.ico")));
}
