using Gooner.Core.Localization;

namespace Gooner.Core.Tests.Localization;

// res/locales/<language>/*.json: one folder per language, any number of files in it, each an object
// of English text -> translation. English is the code itself and has no folder.
public sealed class CatalogueTests : IDisposable
{
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-locales-");

    public void Dispose() => _tmp.Delete(recursive: true);

    private void Write(string language, string file, string json)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_tmp.FullName, language));
        File.WriteAllText(Path.Combine(dir.FullName, file), json);
    }

    [Fact]
    public void english_needs_no_files()
    {
        var english = Catalogue.Load(_tmp.FullName, "en");

        Assert.Equal("en", english.Code);
        Assert.Null(english.Lookup("Close"));
    }

    [Fact]
    public void a_language_is_every_file_of_its_folder_together()
    {
        Write("de", "ui.json", """{ "Close": "Schließen" }""");
        Write("de", "guide.json", """{ "Guide": "Anleitung" }""");

        var german = Catalogue.Load(_tmp.FullName, "de");

        Assert.Equal("de", german.Code);
        Assert.Equal("Schließen", german.Lookup("Close"));
        Assert.Equal("Anleitung", german.Lookup("Guide"));
    }

    // What the settings name and the folder does not have - from a later build, or a folder deleted
    // by hand - is English rather than a language that translates nothing.
    [Fact]
    public void a_language_without_a_folder_is_english()
    {
        var spanish = Catalogue.Load(_tmp.FullName, "es");

        Assert.Equal("en", spanish.Code);
        Assert.Equal("en", Catalogue.Load(null, "de").Code);
    }

    [Fact]
    public void an_unreadable_file_costs_only_itself()
    {
        Write("de", "broken.json", "{ \"Close\": ");
        Write("de", "list.json", """["Close"]""");
        Write("de", "ui.json", """{ "Guide": "Anleitung" }""");

        var german = Catalogue.Load(_tmp.FullName, "de");

        Assert.Equal("Anleitung", german.Lookup("Guide"));
        Assert.Null(german.Lookup("Close"));
    }

    // Files are read in name order, so which of two translations wins does not depend on the disk.
    [Fact]
    public void a_text_two_files_translate_keeps_the_first()
    {
        Write("de", "b.json", """{ "Close": "Zumachen" }""");
        Write("de", "a.json", """{ "Close": "Schließen" }""");

        Assert.Equal("Schließen", Catalogue.Load(_tmp.FullName, "de").Lookup("Close"));
    }

    // An empty translation would blank the text out; it reads as no translation at all.
    [Fact]
    public void an_empty_or_unwritten_translation_is_not_taken()
    {
        Write("de", "ui.json", """{ "Close": "", "Guide": 3, "Start": null, "Stop": "Stopp" }""");

        var german = Catalogue.Load(_tmp.FullName, "de");

        Assert.Null(german.Lookup("Close"));
        Assert.Null(german.Lookup("Guide"));
        Assert.Null(german.Lookup("Start"));
        Assert.Equal("Stopp", german.Lookup("Stop"));
    }

    [Fact]
    public void the_languages_on_offer_are_english_and_every_folder_with_a_file()
    {
        Write("fr", "ui.json", "{}");
        Write("de", "ui.json", "{}");
        Directory.CreateDirectory(Path.Combine(_tmp.FullName, "xx"));

        Assert.Equal(["en", "de", "fr"], Catalogue.Available(_tmp.FullName));
    }

    [Fact]
    public void without_a_folder_only_english_is_on_offer()
    {
        Assert.Equal(["en"], Catalogue.Available(null));
        Assert.Equal(["en"], Catalogue.Available(Path.Combine(_tmp.FullName, "missing")));
    }

    // Offered under its own name: someone who cannot read the language the app is in still finds
    // their own.
    [Fact]
    public void a_language_is_offered_under_its_own_name()
    {
        Assert.Equal("English", Catalogue.NativeName("en"));
        Assert.Equal("Deutsch", Catalogue.NativeName("de"));
        Assert.Equal("Français", Catalogue.NativeName("fr"));
        Assert.Equal("xx", Catalogue.NativeName("xx"));
    }
}
