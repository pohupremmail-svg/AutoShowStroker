using System.Text;

namespace Gooner.Build.Tests;

// A repository and an export of it in a temp folder, with every part the package is built from:
// res/ (the Python app's licenses included), the engine's licenses, VERSION and the FFmpeg extension.
internal sealed class FakeRepository : IDisposable
{
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-build-");

    public FakeRepository()
    {
        Repo = new Repository(Path.Combine(_tmp.FullName, "repo"));
        Write("res/callouts/en/flirty.json", """{"session_started": ["hi"]}""");
        Write("res/icons/favicon.ico", "icon");
        Write("res/icons/achievements/first.svg", "<svg/>");
        Write("res/click.wav", "RIFF");
        Write("res/licenses/THIRD_PARTY_NOTICES.txt", "Qt, PyQt6, NumPy");
        Write("engine/licenses/THIRD_PARTY_NOTICES.txt", "Godot, .NET, FFmpeg");
        Write("engine/licenses/LGPL-3.0.txt", "LGPL");
        Write("VERSION", "1.2.3\n");
        Write("engine/godot/addons/ffmpeg/win64/avcodec-60.dll", "avcodec");
        Write("engine/godot/addons/ffmpeg/win64/libgdffmpeg.windows.template_release.x86_64.dll", "release");
        Write("engine/godot/addons/ffmpeg/win64/libgdffmpeg.windows.template_debug.x86_64.dll", "debug");
        Write("engine/godot/addons/ffmpeg/win64/~libgdffmpeg.windows.template_debug.x86_64.dll", "editor copy");
        Output = Path.Combine(_tmp.FullName, "out");
    }

    public Repository Repo { get; }

    public string Output { get; }

    public void Dispose() => _tmp.Delete(recursive: true);

    public void Write(string relative, string content)
    {
        var path = Path.Combine(Repo.Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public string OutputFile(string relative) => Path.Combine(Output, relative);

    public void WriteOutput(string relative, string content) => WriteOutput(relative, Encoding.UTF8.GetBytes(content));

    public void WriteOutput(string relative, byte[] content)
    {
        var path = OutputFile(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    // What Godot's export leaves, and the rest added beside it: a package that passes.
    public void Export()
    {
        WriteOutput(Package.Executable, "MZ");
        foreach (var assembly in Package.Assemblies)
            WriteOutput(Path.Combine(Package.DataFolder, assembly), "assembly");
        foreach (var library in Package.FfmpegLibraries(Repo))
            File.Copy(library, OutputFile(Path.GetFileName(library)));
        Package.AddBesideExecutable(Repo, Output);
    }
}
