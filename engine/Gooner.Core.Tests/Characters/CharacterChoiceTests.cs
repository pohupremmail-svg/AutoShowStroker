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

    // --- the user's own folder ---

    private string Own => Path.Combine(_tmp.FullName, "own");

    private void WriteOwn(string name)
    {
        Directory.CreateDirectory(Own);
        File.WriteAllBytes(Path.Combine(Own, name), [0]);
    }

    [Fact]
    public void a_folder_of_ones_own_is_offered_after_the_bundled_ones()
    {
        Write("a.vrm");
        WriteOwn("mine.vrm");
        var choice = new CharacterChoice(_settings, Dir);

        choice.ChooseFolder(Own);

        Assert.Equal(["a.vrm", "own/mine.vrm", CharacterChoice.None], choice.Options.Select(o => o.Value));
        Assert.Equal("mine", choice.Options[1].Label);
    }

    [Fact]
    public void an_own_model_is_loaded_from_that_folder_and_both_are_kept()
    {
        Write("a.vrm");
        WriteOwn("mine.vrm");
        var choice = new CharacterChoice(_settings, Dir);
        choice.ChooseFolder(Own);

        choice.Choose("own/mine.vrm");
        var again = new CharacterChoice(_settings, Dir);

        Assert.Equal(Own, again.Folder);
        Assert.Equal(Path.Combine(Own, "mine.vrm"), again.Path);
    }

    [Fact]
    public void forgetting_the_folder_drops_its_models_and_falls_back()
    {
        Write("a.vrm");
        WriteOwn("mine.vrm");
        var choice = new CharacterChoice(_settings, Dir);
        choice.ChooseFolder(Own);
        choice.Choose("own/mine.vrm");

        choice.ChooseFolder(null);

        Assert.Null(choice.Folder);
        Assert.DoesNotContain(choice.Options, o => o.Value.StartsWith("own/", StringComparison.Ordinal));
        Assert.Equal(Path.Combine(Dir, "a.vrm"), choice.Path);
        Assert.Equal("", _settings.GetString($"{CharacterChoice.SettingsGroup}/folder", "x"));
    }

    [Fact]
    public void a_folder_that_is_gone_offers_nothing_from_it()
    {
        Write("a.vrm");
        _settings.SetValue($"{CharacterChoice.SettingsGroup}/folder", Path.Combine(_tmp.FullName, "gone"));

        var choice = new CharacterChoice(_settings, Dir);

        Assert.Equal(["a.vrm", CharacterChoice.None], choice.Options.Select(o => o.Value));
    }

    [Fact]
    public void the_options_of_a_folder_not_yet_chosen_can_be_previewed()
    {
        Write("a.vrm");
        WriteOwn("mine.vrm");
        var choice = new CharacterChoice(_settings, Dir);

        Assert.Equal(["a.vrm", "own/mine.vrm", CharacterChoice.None], choice.OptionsFor(Own).Select(o => o.Value));
        Assert.Null(choice.Folder);
    }

    [Fact]
    public void no_folder_offers_only_none()
    {
        var choice = new CharacterChoice(_settings, Dir);

        Assert.Equal([CharacterChoice.None], choice.Options.Select(o => o.Value));
        Assert.Null(choice.Path);
    }

    // --- a figure that takes the app down with it ---

    // The pick is loaded at every start, so a model that crashes the app while it loads would crash
    // every start after it too, with no way into the settings to pick another. Which one is loading is
    // written down first and only crossed out once it is safely on screen.
    [Fact]
    public void a_figure_on_its_way_in_is_written_down_until_it_is_settled()
    {
        Write("a.vrm");
        var choice = new CharacterChoice(_settings, Dir);

        choice.LoadStarting();
        Assert.Equal("a.vrm", _settings.GetString("Character/loading", ""));

        choice.LoadSettled();
        Assert.False(_settings.Contains("Character/loading"));
    }

    // Still written down at the next start: the app went down with it. It is switched off, and the
    // start says which one it was.
    [Fact]
    public void the_figure_the_app_went_down_with_is_not_loaded_again()
    {
        Write("a.vrm");
        Write("b.vrm");
        var before = new CharacterChoice(_settings, Dir);
        before.Choose("b.vrm");
        before.LoadStarting();

        var after = new CharacterChoice(_settings, Dir);

        Assert.Equal(CharacterChoice.None, after.File);
        Assert.Null(after.Path);
        Assert.Equal("b", after.Abandoned);
        Assert.Equal(CharacterChoice.None, new CharacterChoice(_settings, Dir).File);
        Assert.False(_settings.Contains("Character/loading"));
    }

    // Picked again under Settings > Look, it is tried again: the user asked for it.
    [Fact]
    public void it_can_be_picked_again()
    {
        Write("a.vrm");
        var before = new CharacterChoice(_settings, Dir);
        before.LoadStarting();
        var after = new CharacterChoice(_settings, Dir);

        after.Choose("a.vrm");

        Assert.Equal("a.vrm", new CharacterChoice(_settings, Dir).File);
        Assert.Null(new CharacterChoice(_settings, Dir).Abandoned);
    }

    // Written down for a figure that is no longer the pick - changed while it loaded - it says nothing
    // about the one picked now.
    [Fact]
    public void a_note_about_another_figure_is_only_crossed_out()
    {
        Write("a.vrm");
        Write("b.vrm");
        var before = new CharacterChoice(_settings, Dir);
        before.LoadStarting();
        before.Choose("b.vrm");

        var after = new CharacterChoice(_settings, Dir);

        Assert.Equal("b.vrm", after.File);
        Assert.Null(after.Abandoned);
        Assert.False(_settings.Contains("Character/loading"));
    }

    [Fact]
    public void a_clean_start_abandons_nothing()
    {
        Write("a.vrm");

        Assert.Null(new CharacterChoice(_settings, Dir).Abandoned);
    }
}
