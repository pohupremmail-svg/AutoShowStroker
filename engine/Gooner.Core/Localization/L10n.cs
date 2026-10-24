using System.Globalization;

namespace Gooner.Core.Localization;

// The words the app shows, in the language the user picked. English is the source: every text is
// written in English where it is used and looked up in the catalogue of the language in use, and
// whatever that catalogue lacks stays English - so the English app is exactly the code.
//
// Only literals go in: Gooner.Core.Tests reads every literal handed to T, N, F and P out of the
// source and holds each shipped catalogue to them (LocaleFilesTests). A text built first and
// translated afterwards could never be found in a catalogue, so a value goes into a format (F)
// instead. Log lines are never translated - they are for whoever reads the report, not the user.
//
// One language at a time for the whole app, set where the app is built and when the settings
// change it - like the colour scheme, everything reads it when it draws or builds.
public static class L10n
{
    private static volatile Catalogue _current = Catalogue.English;

    public static Catalogue Current => _current;

    public static string Language => _current.Code;

    // Null is English.
    public static void Use(Catalogue? catalogue) => _current = catalogue ?? Catalogue.English;

    public static string T(string english)
    {
        ArgumentNullException.ThrowIfNull(english);
        return _current.Lookup(english) ?? english;
    }

    // The format is translated, then filled in - so a translation puts the values where its own
    // grammar wants them. Numbers are written the same in every language, with a decimal point.
    public static string F(string english, params object?[] args) =>
        string.Format(CultureInfo.InvariantCulture, T(english), args);

    // A count, in the form its language takes for it; {0} is the count. Which form is the
    // language's rule - French takes the singular for none as well as for one, so a singular that
    // names the number has to say {0} rather than 1.
    public static string P(long count, string one, string other) =>
        F(_current.TakesSingular(count) ? one : other, count);

    // A name that is also what gets stored or compared - a settings tab, a rhythm - marked for the
    // catalogues where it is written and left English; it is translated (T) where it is shown.
    public static string N(string english) => english;
}
