namespace Gooner.Build;

// Where the parts of the package are in the repository.
public sealed record Repository(string Root)
{
    // The one res/ both apps read: callouts, icons, the click.
    public string Res => Path.Combine(Root, "res");

    public string Licenses => Path.Combine(Root, "engine", "licenses");

    public string Version => Path.Combine(Root, "VERSION");

    public string GodotProject => Path.Combine(Root, "engine", "godot");

    public string FfmpegLibraries => Path.Combine(GodotProject, "addons", "ffmpeg", "win64");

    // The source of the FFmpeg the libraries were built from, and of the extension that bundles
    // them - attached to every release, as the LGPL asks.
    public string FfmpegSources => Path.Combine(Root, "engine", "third_party", "ffmpeg");

    public string Dist => Path.Combine(Root, "dist");

    // The repository above `start`: the folder holding res/, VERSION and engine/.
    public static Repository Find(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "res")) && File.Exists(Path.Combine(dir.FullName, "VERSION"))
                && Directory.Exists(Path.Combine(dir.FullName, "engine")))
                return new Repository(dir.FullName);
        throw new DirectoryNotFoundException("No repository (res/, VERSION, engine/) above " + start);
    }
}
