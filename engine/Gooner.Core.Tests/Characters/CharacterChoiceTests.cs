using Gooner.Core.Characters;
using Gooner.Core.Data;
using Gooner.Core.Settings;

namespace Gooner.Core.Tests.Characters;

// Which of the .vrm files in res/characters stands on the start screen: any of them, or none. A user
// drops their own into the folder of their installation and picks it in Settings > Look.
public sealed class CharacterChoiceTests : IDisposable
{
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-characters-");
    private readonly JsonSettingsStore _settings;

    public CharacterChoiceTests() => _settings = new(new UserDataStore(Path.Combine(_tmp.FullName, "data")));

    public void Dispose() => _tmp.Delete(recursive: true);

    private string Dir => Path.Combine(_tmp.FullName, "characters");

    private void Write(string name)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllBytes(Path.Combine(Dir, name), [0]);
    }

    [Fact]
    public void every_vrm_in_the_folder_is_offered_by_name_and_none_last()
    {
        Write("b.vrm");
        Write("a.vrm");

        var choice = new CharacterChoice(_settings, Dir);

        Assert.Equal(["a.vrm", "b.vrm", CharacterChoice.None], choice.Options.Select(o => o.Value));
        Assert.Equal("a", choice.Options[0].Label);
    }

    [Fact]
    public void the_first_by_name_until_one_is_picked()
    {
        Write("b.vrm");
        Write("a.vrm");

        Assert.Equal(Path.Combine(Dir, "a.vrm"), new CharacterChoice(_settings, Dir).Path);
    }

    [Fact]
    public void a_pick_is_kept()
    {
        Write("a.vrm");
        Write("b.vrm");
        new CharacterChoice(_settings, Dir).Choose("b.vrm");

        var again = new CharacterChoice(_settings, Dir);

        Assert.Equal("b.vrm", again.File);
        Assert.Equal(Path.Combine(Dir, "b.vrm"), again.Path);
    }

    [Fact]
    public void none_means_no_character()
    {
        Write("a.vrm");
        var choice = new CharacterChoice(_settings, Dir);

        choice.Choose(CharacterChoice.None);

        Assert.Null(choice.Path);
    }

    [Fact]
    public void a_picked_file_that_is_gone_falls_back_to_the_first()
    {
        Write("a.vrm");
        _settings.SetValue($"{CharacterChoice.SettingsGroup}/file", "gone.vrm");

        Assert.Equal(Path.Combine(Dir, "a.vrm"), new CharacterChoice(_settings, Dir).Path);
    }

    [Fact]
    public void no_folder_offers_only_none()
    {
        var choice = new CharacterChoice(_settings, Dir);

        Assert.Equal([CharacterChoice.None], choice.Options.Select(o => o.Value));
        Assert.Null(choice.Path);
    }
}
