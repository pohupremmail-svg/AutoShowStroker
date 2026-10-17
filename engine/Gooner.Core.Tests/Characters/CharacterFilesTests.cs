using Gooner.Core.Characters;

namespace Gooner.Core.Tests.Characters;

// res/characters/*.vrm: the figures the app brings along. Until a picker exists, the first one stands
// on the start screen.
public sealed class CharacterFilesTests : IDisposable
{
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-characters-");

    public void Dispose() => _tmp.Delete(recursive: true);

    private void Write(string name)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_tmp.FullName, "characters"));
        File.WriteAllBytes(Path.Combine(dir.FullName, name), [0]);
    }

    [Fact]
    public void no_folder_means_no_character()
    {
        Assert.Null(CharacterFiles.First(_tmp.FullName));
        Assert.Null(CharacterFiles.First(null));
    }

    [Fact]
    public void the_first_by_name_stands_on_the_start_screen()
    {
        Write("b.vrm");
        Write("a.VRM");

        Assert.Equal(Path.Combine(_tmp.FullName, "characters", "a.VRM"), CharacterFiles.First(_tmp.FullName));
    }

    [Fact]
    public void only_vrm_files_are_characters()
    {
        Write("notes.txt");
        Write("model.glb");

        Assert.Null(CharacterFiles.First(_tmp.FullName));
    }
}
