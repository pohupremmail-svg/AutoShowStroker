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

    // Both go to %APPDATA%\Godot\app_userdata\GoonerApp (measured: 5 MB after one start). They were
    // off so that nothing of Godot's was left behind; without them every start compiled its shaders
    // again, and the first session stalled on it - 130 ms as it began, 140 ms as she appeared in her
    // frame. The user's call (2026-10-21): keep them. Disclosed and deletable under Privacy & Data.
    [Fact]
    public void godot_keeps_its_shader_and_pipeline_caches()
    {
        var rendering = Section(ProjectFile, "rendering");
        Has(rendering, "shader_compiler/shader_cache/enabled=true");
        Has(rendering, "rendering_device/pipeline_cache/enable=true");
    }

    // main.py: window.showMaximized().
    [Fact]
    public void the_window_opens_maximized_as_the_python_window_did() =>
        Has(Section(ProjectFile, "display"), "window/size/mode=2");

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
        // FFmpeg-Builds-* is the scripts that built it, not FFmpeg.
        var archive = Path.GetFileName(Assert.Single(Directory.GetFiles(Repo.FfmpegSources, "FFmpeg-*-source.tar.gz"),
            f => !Path.GetFileName(f).StartsWith("FFmpeg-Builds-", StringComparison.Ordinal)));
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

        // FFmpeg, the extension, the scripts that built the FFmpeg, and the two LGPL libraries built
        // into it (see the next test).
        Assert.Equal(["EIRTeam.FFmpeg-", "FFmpeg-", "FFmpeg-Builds-", "libiconv-", "soxr-"],
            archives.Select(a => a![..(a!.LastIndexOf('-', a.Length - "-source.tar.gz".Length - 1) + 1)]).Order(StringComparer.Ordinal));
        foreach (var archive in archives)
            Assert.Contains(archive!, notices, StringComparison.Ordinal);
        Assert.Contains("engine/third_party/ffmpeg/*-source.tar.gz",
            File.ReadAllText(Path.Combine(Repo.Root, ".github", "workflows", "release-check.yml")), StringComparison.Ordinal);
    }

    // The FFmpeg libraries came out of a build that linked other people's libraries into them, and each
    // has to be named, with its license text, in the notices; the two under the LGPL also go out with
    // their source (the archives above). Found by reading the libraries themselves, not the build's
    // configure line - which lists far more than was linked (2026-10-24). Each is told by text of its
    // own inside the library it is in. A new build of the libraries fails here for whatever it no
    // longer contains, and has to be read again for what it contains now.
    [Theory]
    [InlineData("avcodec-60.dll", "Xiph.Org libVorbis", "libvorbis")]
    [InlineData("avcodec-60.dll", "Xiph.Org libVorbis", "libogg")]
    [InlineData("avcodec-60.dll", "request not implemented", "libopus")]
    [InlineData("avcodec-60.dll", "CSISOLATIN1", "libiconv")]
    [InlineData("avformat-60.dll", "inflate 1.2.13 Copyright", "zlib")]
    [InlineData("avutil-58.dll", "OCL_ICD", "OpenCL ICD Loader")]
    [InlineData("swresample-4.dll", "SoX Resampler", "soxr")]
    [InlineData("avutil-58.dll", "Mingw-w64 runtime failure", "MinGW-w64 runtime")]
    [InlineData("libgdffmpeg.windows.template_release.x86_64.dll", "winpthread", "winpthreads")]
    public void every_library_built_into_the_ffmpeg_libraries_has_its_notice(string library, string marker, string heading)
    {
        var binary = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(Repo.FfmpegLibraries, library)));
        var notices = File.ReadAllText(Path.Combine(Repo.Licenses, "THIRD_PARTY_NOTICES.txt"));

        Assert.Contains(marker, binary, StringComparison.Ordinal);
        Assert.Contains($"== {heading}", notices, StringComparison.Ordinal);
    }

    // Godot and its templates come from the cache when they can, and a cache entry is something
    // another workflow run can write. The job that builds the release holds a token that writes to
    // the repository, so what it runs is checked against the pinned SHA-512 every time - not only
    // when it was just downloaded.
    [Fact]
    public void the_release_build_checks_godot_whether_it_came_from_the_cache_or_not()
    {
        var workflow = File.ReadAllText(Path.Combine(Repo.Root, ".github", "workflows", "release-check.yml")).ReplaceLineEndings("\n");
        var steps = workflow.Split("\n      - ").Skip(1).ToList();

        var checking = steps.Where(step => step.Contains("Get-FileHash", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(checking);
        Assert.All(checking, step => Assert.DoesNotContain("cache-hit", step, StringComparison.Ordinal));
        Assert.All(checking, step =>
        {
            Assert.Contains("GODOT_EDITOR_SHA512", step, StringComparison.Ordinal);
            Assert.Contains("GODOT_TEMPLATES_SHA512", step, StringComparison.Ordinal);
        });
        // ... and before anything of it is unpacked or run.
        var check = steps.FindIndex(step => step.Contains("Get-FileHash", StringComparison.Ordinal) && !step.Contains("cache-hit", StringComparison.Ordinal));
        var install = steps.FindIndex(step => step.Contains("Expand-Archive", StringComparison.Ordinal));
        Assert.True(check >= 0 && check < install, "the check has to run before Godot is unpacked");
    }

    // The executable's icon has to sit inside the Godot project for the export to embed it; it is a
    // copy of the one both apps use, and has to stay one.
    [Fact]
    public void the_executable_icon_is_the_apps() =>
        Assert.Equal(File.ReadAllBytes(Path.Combine(Repo.Res, "icons", "favicon.ico")),
            File.ReadAllBytes(Path.Combine(Repo.GodotProject, "icon.ico")));
}
