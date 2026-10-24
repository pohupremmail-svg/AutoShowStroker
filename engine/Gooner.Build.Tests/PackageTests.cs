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
    // not the app's to pass on - all but the app's own, which are named; a user brings their own
    // through Settings > Look.
    [Fact]
    public void character_models_stay_out_but_the_apps_own()
    {
        _fake.Write("res/characters/Someone - Nude.vrm", "model");
        _fake.Write("res/characters/sub/Other.vrm", "model");
        foreach (var name in Package.Characters)
            _fake.Write($"res/characters/{name}", "model");

        Assert.Equal(Package.Characters.Select(name => $"res/characters/{name}").Order(StringComparer.Ordinal),
            Listed().Where(f => f.EndsWith(".vrm", StringComparison.Ordinal)));
    }

    // res/props holds a licensed model nobody may pass on, res/vroids the VRoid Studio sources of
    // figures - both are the developer's own, gitignored, and were packed into a local build because
    // the package took all of res/ but what it named.
    [Fact]
    public void props_and_vroid_sources_stay_out()
    {
        _fake.Write("res/props/TOY.blend", "source");
        _fake.Write("res/props/toy.glb", "export");
        _fake.Write("res/props/toy.json", "{}");
        _fake.Write("res/vroids/model.vroid", "vroid");

        var listed = Listed();
        Assert.DoesNotContain(listed, f => f.StartsWith("res/props/", StringComparison.Ordinal));
        Assert.DoesNotContain(listed, f => f.StartsWith("res/vroids/", StringComparison.Ordinal));
    }

    // What goes is named, not what stays: a folder that turns up in res/ later is the developer's until
    // it is added here on purpose.
    [Fact]
    public void only_what_the_engine_reads_goes_along()
    {
        _fake.Write("res/locales/de/ui.json", "{}");
        _fake.Write("res/whatever/notes.txt", "private");
        _fake.Write("res/stray.png", "png");

        var res = Listed().Where(f => f.StartsWith("res/", StringComparison.Ordinal)).ToList();
        Assert.Contains("res/locales/de/ui.json", res);
        Assert.All(res, f => Assert.True(
            f.StartsWith("res/callouts/", StringComparison.Ordinal) || f.StartsWith("res/locales/", StringComparison.Ordinal)
            || f.StartsWith("res/icons/", StringComparison.Ordinal) || f == "res/click.wav"
            || Package.Characters.Any(name => f == $"res/characters/{name}"), f));
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
