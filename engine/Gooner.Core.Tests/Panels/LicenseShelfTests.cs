using System.Text.RegularExpressions;
using Gooner.Core.Panels;
using Gooner.Core.Tests.Support;

namespace Gooner.Core.Tests.Panels;

// The engine's counterpart of tests/test_third_party_licenses.py: engine/licenses/ is what the
// packaged engine carries of other people's software, and the notices are checked against the
// build's own inputs, so a version bump or a new dependency fails here until the notices say so.
public sealed partial class LicenseShelfTests : IDisposable
{
    private static readonly string Shelf = Repo.PathTo(Path.Combine("engine", "licenses"));
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-licenses-");

    public void Dispose() => _tmp.Delete(recursive: true);

    private static string Notices => File.ReadAllText(Path.Combine(Shelf, LicenseShelf.Notices));

    // The notices come first, as the index; every other text after them, by name.
    [Fact]
    public void the_notices_come_first_and_the_rest_by_name()
    {
        foreach (var name in new[] { "b.txt", "A.txt", LicenseShelf.Notices, "c.md" })
            File.WriteAllText(Path.Combine(_tmp.FullName, name), name);

        Assert.Equal([LicenseShelf.Notices, "A.txt", "b.txt"], LicenseShelf.Files(_tmp.FullName));
    }

    [Fact]
    public void a_shelf_that_is_not_there_lists_nothing() =>
        Assert.Empty(LicenseShelf.Files(Path.Combine(_tmp.FullName, "missing")));

    [Fact]
    public void the_texts_named_are_read_off_the_license_text_lines() =>
        Assert.Equal(["a.txt", "b.txt", "c.txt"],
            LicenseShelf.NamedTexts("== X ==\nLicense text: a.txt, b.txt\nLicense: whatever\nLicense text: c.txt\n").Order());

    [Fact]
    public void every_license_text_named_is_there()
    {
        var missing = LicenseShelf.NamedTexts(Notices).Where(name => !File.Exists(Path.Combine(Shelf, name))).ToList();

        Assert.True(missing.Count == 0, $"named in the notices but not in engine/licenses: {string.Join(", ", missing)}");
    }

    [Fact]
    public void no_license_text_is_left_unnamed()
    {
        var unnamed = LicenseShelf.Files(Shelf).Skip(1).Except(LicenseShelf.NamedTexts(Notices)).ToList();

        Assert.True(unnamed.Count == 0, $"in engine/licenses but named by no notice: {string.Join(", ", unnamed)}");
    }

    [GeneratedRegex("Sdk=\"(Godot\\.NET\\.Sdk/[0-9.]+)\"")]
    private static partial Regex GodotSdk();

    // A Godot upgrade has to bump the notice with it, or it describes a different build.
    [Fact]
    public void the_godot_the_project_builds_with_is_named_at_its_version()
    {
        var project = File.ReadAllText(Repo.PathTo(Path.Combine("engine", "godot", "Gooner.Engine.csproj")));
        var sdk = GodotSdk().Match(project).Groups[1].Value;

        Assert.NotEmpty(sdk);
        Assert.Contains($"Package: {sdk}", Notices);
    }

    [Fact]
    public void the_dotnet_the_project_targets_is_named()
    {
        var project = File.ReadAllText(Repo.PathTo(Path.Combine("engine", "godot", "Gooner.Engine.csproj")));
        var version = Regex.Match(project, "<TargetFramework>net([0-9.]+)</TargetFramework>").Groups[1].Value;

        Assert.Contains($"Package: Microsoft.NETCore.App.Runtime.win-x64/{version}", Notices);
    }

    // The extension's DLLs are not in git yet (engine/CLAUDE.md); where a checkout has them, every
    // FFmpeg library among them is named by file, so a new build of the extension shows up here.
    [Fact]
    public void the_ffmpeg_the_video_extension_bundles_is_named_library_by_library()
    {
        var dlls = Repo.PathTo(Path.Combine("engine", "godot", "addons", "ffmpeg", "win64"));
        if (!Directory.Exists(dlls)) return;

        foreach (var library in Directory.GetFiles(dlls, "*.dll").Select(Path.GetFileName)
                     .Where(name => name!.StartsWith("av", StringComparison.Ordinal) || name.StartsWith("sw", StringComparison.Ordinal)))
            Assert.Contains(library!, Notices);
    }
}
