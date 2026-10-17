using Gooner.Core.Localization;

namespace Gooner.Core.Tests.Localization;

// The words the app shows: written in English where they are used, and looked up in the catalogue
// of the language the user picked - English wherever that catalogue has nothing.
public sealed class L10nTests : IDisposable
{
    public void Dispose() => L10n.Use(null);

    private static Catalogue German(params (string English, string Translation)[] entries) =>
        new("de", entries.ToDictionary(e => e.English, e => e.Translation));

    [Fact]
    public void without_a_catalogue_the_app_speaks_english()
    {
        L10n.Use(null);

        Assert.Equal("en", L10n.Language);
        Assert.Equal("Start Session", L10n.T("Start Session"));
    }

    [Fact]
    public void a_catalogue_says_it_in_its_own_language()
    {
        L10n.Use(German(("Start Session", "Session starten")));

        Assert.Equal("de", L10n.Language);
        Assert.Equal("Session starten", L10n.T("Start Session"));
    }

    [Fact]
    public void what_a_catalogue_lacks_stays_english()
    {
        L10n.Use(German(("Start Session", "Session starten")));

        Assert.Equal("Saved Sessions", L10n.T("Saved Sessions"));
    }

    // The format is what gets translated, so a translation can put the numbers where its own
    // grammar wants them.
    [Fact]
    public void a_format_is_filled_in_after_it_is_translated()
    {
        L10n.Use(German(("{0} of {1} unlocked", "{1} gibt es, {0} davon freigeschaltet")));

        Assert.Equal("20 gibt es, 3 davon freigeschaltet", L10n.F("{0} of {1} unlocked", 3, 20));
        L10n.Use(null);
        Assert.Equal("3 of 20 unlocked", L10n.F("{0} of {1} unlocked", 3, 20));
    }

    // The numbers the app writes look the same in every language, as they always have: a decimal
    // point, never a comma that would read as a list in the middle of "1,5, 2,0 Hz".
    [Fact]
    public void numbers_are_written_the_same_way_in_every_language()
    {
        L10n.Use(German(("{0} Hz", "{0} Hz")));

        Assert.Equal("1.5 Hz", L10n.F("{0} Hz", 1.5));
    }

    [Fact]
    public void a_count_takes_the_singular_only_for_one_in_english_and_german()
    {
        Assert.Equal("0 sessions", L10n.P(0, "{0} session", "{0} sessions"));
        Assert.Equal("1 session", L10n.P(1, "{0} session", "{0} sessions"));
        Assert.Equal("2 sessions", L10n.P(2, "{0} session", "{0} sessions"));

        L10n.Use(German(("{0} session", "{0} Session"), ("{0} sessions", "{0} Sessions")));

        Assert.Equal("0 Sessions", L10n.P(0, "{0} session", "{0} sessions"));
        Assert.Equal("1 Session", L10n.P(1, "{0} session", "{0} sessions"));
    }

    // French counts nothing as one of a thing: "0 session", not "0 sessions".
    [Fact]
    public void french_takes_the_singular_for_zero_too()
    {
        L10n.Use(new Catalogue("fr", new Dictionary<string, string> { ["{0} session"] = "{0} session", ["{0} sessions"] = "{0} sessions" }));

        Assert.Equal("0 session", L10n.P(0, "{0} session", "{0} sessions"));
        Assert.Equal("1 session", L10n.P(1, "{0} session", "{0} sessions"));
        Assert.Equal("2 sessions", L10n.P(2, "{0} session", "{0} sessions"));
    }

    // A name that is also what gets stored - a rhythm, a settings tab - is marked for the catalogues
    // where it is written and translated only where it is shown.
    [Fact]
    public void a_marked_name_stays_english_until_it_is_shown()
    {
        L10n.Use(German(("Standard Beat", "Standard-Takt")));

        Assert.Equal("Standard Beat", L10n.N("Standard Beat"));
        Assert.Equal("Standard-Takt", L10n.T(L10n.N("Standard Beat")));
    }

    // What the tests switch to when they want to see which texts went through a catalogue: every one
    // that did comes out bracketed.
    [Fact]
    public void the_pseudo_catalogue_brackets_everything_it_is_asked_for()
    {
        L10n.Use(Catalogue.Pseudo);

        Assert.Equal("⟦Close⟧", L10n.T("Close"));
        Assert.Equal("⟦3 saved⟧", L10n.F("{0} saved", 3));
        Assert.Equal("⟦2 times⟧", L10n.P(2, "once", "{0} times"));
        Assert.Equal("Standard Beat", L10n.N("Standard Beat"));
    }
}
