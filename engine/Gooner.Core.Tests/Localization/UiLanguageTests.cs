using Gooner.Core.Data;
using Gooner.Core.Localization;
using Gooner.Core.Settings;

namespace Gooner.Core.Tests.Localization;

// Which language the app's own words are in - the windows, the settings, the guide. A setting of its
// own, apart from the language the callouts speak in: the two are picked separately.
public sealed class UiLanguageTests : IDisposable
{
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-ui-language-");

    public UiLanguageTests()
    {
        foreach (var (language, close) in new[] { ("de", "Schließen"), ("fr", "Fermer") })
        {
            var dir = Directory.CreateDirectory(Path.Combine(_tmp.FullName, "locales", language));
            File.WriteAllText(Path.Combine(dir.FullName, "ui.json"), $$"""{ "Close": "{{close}}" }""");
        }
    }

    public void Dispose()
    {
        L10n.Use(null);
        _tmp.Delete(recursive: true);
    }

    private string Locales => Path.Combine(_tmp.FullName, "locales");

    private JsonSettingsStore Store() => new(new UserDataStore(Path.Combine(_tmp.FullName, "data")));

    // The user's decision: English until they pick another, whatever language Windows is in.
    [Fact]
    public void it_is_english_until_another_is_picked()
    {
        Assert.Equal("en", new UiLanguage(null, Locales).Language);
        Assert.Equal("en", new UiLanguage(Store(), Locales).Language);
    }

    [Fact]
    public void the_language_picked_is_the_one_read_back()
    {
        var store = Store();
        store.SetValue("Ui/language", "de");

        Assert.Equal("de", new UiLanguage(store, Locales).Language);
    }

    [Fact]
    public void a_language_this_build_does_not_ship_reads_as_english()
    {
        var store = Store();
        store.SetValue("Ui/language", "es");

        Assert.Equal("en", new UiLanguage(store, Locales).Language);
    }

    [Fact]
    public void applying_it_switches_what_the_app_says()
    {
        var store = Store();
        store.SetValue("Ui/language", "fr");

        new UiLanguage(store, Locales).Apply();

        Assert.Equal("fr", L10n.Language);
        Assert.Equal("Fermer", L10n.T("Close"));
    }

    [Fact]
    public void choosing_one_keeps_it_and_switches_to_it()
    {
        var store = Store();
        var language = new UiLanguage(store, Locales);

        language.Choose("de");

        Assert.Equal("de", language.Language);
        Assert.Equal("de", store.GetString("Ui/language", ""));
        Assert.Equal("Schließen", L10n.T("Close"));
        Assert.Equal("de", new UiLanguage(store, Locales).Language);
    }

    [Fact]
    public void a_language_not_on_offer_cannot_be_chosen()
    {
        var store = Store();
        var language = new UiLanguage(store, Locales);

        language.Choose("es");

        Assert.Equal("en", language.Language);
        Assert.Equal("", store.GetString("Ui/language", ""));
    }

    // Each under its own name, English first.
    [Fact]
    public void the_options_are_every_language_on_offer()
    {
        var options = new UiLanguage(null, Locales).Options;

        Assert.Equal(["en", "de", "fr"], options.Select(o => o.Value));
        Assert.Equal(["English", "Deutsch", "Français"], options.Select(o => o.Label));
    }
}
