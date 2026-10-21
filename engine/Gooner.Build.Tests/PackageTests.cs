namespace Gooner.Build.Tests;

// What goes beside the executable: everything the engine reads from disk rather than from its .pck
// (AppPaths) - res/, its licenses, VERSION - and nothing that is the Python app's.
public sealed class PackageTests : IDisposable
{
    private readonly FakeRepository _fake = new();

    public void Dispose() => _fake.Dispose();

    private List<string> Listed() => [.. Package.BesideExecutable(_fake.Repo).Select(f => f.Relative.Replace('\\', '/')).Order(StringComparer.Ordinal)];

    [Fact]
    public void res_goes_beside_the_executable_without_the_python_apps_licenses()
    {
        var listed = Listed();

        Assert.Contains("res/callouts/en/flirty.json", listed);
        Assert.Contains("res/icons/achievements/first.svg", listed);
        Assert.Contains("res/click.wav", listed);
        // Qt, PyQt6, NumPy: none of it ships with the engine.
        Assert.DoesNotContain(listed, f => f.StartsWith("res/licenses/", StringComparison.Ordinal));
    }

    // Character models in res/characters are the developer's own test figures, gigabytes of them, and
    // none the app may pass on yet; a user brings their own through Settings > Look.
    [Fact]
    public void character_models_stay_out()
    {
        _fake.Write("res/characters/Someone - Nude.vrm", "model");

        Assert.DoesNotContain(Listed(), f => f.EndsWith(".vrm", StringComparison.Ordinal));
    }

    [Fact]
    public void the_engines_licenses_and_the_version_go_with_it()
    {
        var listed = Listed();

        Assert.Contains("licenses/THIRD_PARTY_NOTICES.txt", listed);
        Assert.Contains("licenses/LGPL-3.0.txt", listed);
        Assert.Contains("VERSION", listed);
    }

    [Fact]
    public void adding_them_copies_every_listed_file()
    {
        Package.AddBesideExecutable(_fake.Repo, _fake.Output);

        foreach (var (relative, source) in Package.BesideExecutable(_fake.Repo))
            Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(_fake.OutputFile(relative)));
        Assert.False(Directory.Exists(_fake.OutputFile(Path.Combine("res", "licenses"))));
    }

    [Fact]
    public void the_ffmpeg_libraries_are_the_release_ones()
    {
        var names = Package.FfmpegLibraries(_fake.Repo).Select(Path.GetFileName).Order(StringComparer.Ordinal);

        // Not the debug build of the extension, nor the copy the editor makes while it runs.
        Assert.Equal(["avcodec-60.dll", "libgdffmpeg.windows.template_release.x86_64.dll"], names);
    }
}
