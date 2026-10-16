using System.Text;

namespace Gooner.Build.Tests;

// verify_export: what verify_frozen.py is to the PyInstaller .exe. A package that is missing a part
// is loud here instead of quietly broken on somebody's machine - res/ beside the executable, which
// the engine reads at runtime, is exactly the kind of thing that goes missing without an error.
public sealed class ExportCheckTests : IDisposable
{
    // Stands in for the builder's home folder: the tests never use the real one.
    private const string Home = @"C:\Users\SomebodyElse";

    private readonly FakeRepository _fake = new();

    public ExportCheckTests() => _fake.Export();

    public void Dispose() => _fake.Dispose();

    private IReadOnlyList<string> Problems() => ExportCheck.Problems(_fake.Repo, _fake.Output, [Home]);

    [Fact]
    public void a_complete_package_passes() => Assert.Empty(Problems());

    [Fact]
    public void a_missing_executable_is_named()
    {
        File.Delete(_fake.OutputFile(Package.Executable));

        Assert.Contains(Problems(), p => p.Contains(Package.Executable, StringComparison.Ordinal));
    }

    [Fact]
    public void a_missing_assembly_is_named()
    {
        File.Delete(_fake.OutputFile(Path.Combine(Package.DataFolder, "Gooner.Core.dll")));

        Assert.Contains(Problems(), p => p.Contains("Gooner.Core.dll", StringComparison.Ordinal));
    }

    [Fact]
    public void a_missing_resource_is_named()
    {
        File.Delete(_fake.OutputFile(Path.Combine("res", "callouts", "en", "flirty.json")));

        Assert.Contains(Problems(), p => p.Contains("flirty.json", StringComparison.Ordinal));
    }

    // A package built before a callout was edited would ship yesterday's lines.
    [Fact]
    public void a_resource_that_differs_from_the_repository_is_named()
    {
        _fake.WriteOutput(Path.Combine("res", "callouts", "en", "flirty.json"), "{}");

        Assert.Contains(Problems(), p => p.Contains("flirty.json", StringComparison.Ordinal));
    }

    [Fact]
    public void a_version_that_differs_is_named()
    {
        _fake.WriteOutput("VERSION", "0.0.1\n");

        Assert.Contains(Problems(), p => p.Contains("VERSION", StringComparison.Ordinal));
    }

    [Fact]
    public void the_python_apps_licenses_must_not_ship()
    {
        _fake.WriteOutput(Path.Combine("res", "licenses", "THIRD_PARTY_NOTICES.txt"), "Qt, PyQt6, NumPy");

        Assert.Contains(Problems(), p => p.Contains("licenses", StringComparison.Ordinal));
    }

    [Fact]
    public void a_missing_ffmpeg_library_is_named()
    {
        File.Delete(_fake.OutputFile("avcodec-60.dll"));

        Assert.Contains(Problems(), p => p.Contains("avcodec-60.dll", StringComparison.Ordinal));
    }

    // A build path in a stack trace or a debug record carries the name of whoever built it.
    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    public void a_file_holding_the_home_path_is_named_without_the_path(string encoding)
    {
        var text = $"at Main._Ready() in {Home.ToLowerInvariant()}\\repo\\Main.cs:line 3";
        _fake.WriteOutput(Path.Combine(Package.DataFolder, "Gooner.Engine.dll"),
            encoding == "utf-8" ? Encoding.UTF8.GetBytes(text) : Encoding.Unicode.GetBytes(text));

        var problem = Assert.Single(Problems());

        Assert.Contains("Gooner.Engine.dll", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("SomebodyElse", problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void forward_slashes_count_too()
    {
        _fake.WriteOutput(Path.Combine(Package.DataFolder, "Gooner.Core.dll"), "C:/Users/SomebodyElse/repo");

        Assert.Single(Problems());
    }

    [Fact]
    public void the_home_path_by_default_is_the_users_profile() =>
        Assert.Contains(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ExportCheck.PrivatePaths());
}
