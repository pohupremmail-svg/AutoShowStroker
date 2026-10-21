namespace Gooner.Core.Characters;

// The mouth at one moment of a line: a VRM viseme (aa, ee, ih, oh, ou), or null for closed.
public sealed record MouthShape(double At, string? Viseme);

// The mouth while a line is spoken. There is no voice yet, so the text is the timing: one shape per
// letter at an even pace, the vowels open in their own shape, everything else closed, and a breath
// after the end of a sentence.
public static class Visemes
{
    public const double LettersPerSecond = 14;

    // How many letters' worth of time a full stop, a ! or a ? holds the mouth shut.
    public const int BreathLetters = 3;

    public static IReadOnlyList<MouthShape> Timeline(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var shapes = new List<MouthShape>(text.Length);
        var at = 0.0;
        foreach (var letter in text)
        {
            shapes.Add(new MouthShape(at, Of(letter)));
            at += Length(letter);
        }
        return shapes;
    }

    public static double Duration(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Sum(Length);
    }

    // How many letters of the line have been said `seconds` into it: what her speech bubble shows,
    // typed in with her mouth.
    public static int Shown(string text, double seconds)
    {
        ArgumentNullException.ThrowIfNull(text);
        var shown = 0;
        var at = 0.0;
        foreach (var letter in text)
        {
            // A hair's leeway: a letter is due at a sum of steps, the moment asked about may be a product.
            if (at > seconds + 1e-9) break;
            shown++;
            at += Length(letter);
        }
        return shown;
    }

    private static double Length(char letter) =>
        (".!?…".Contains(letter, StringComparison.Ordinal) ? 1 + BreathLetters : 1) / LettersPerSecond;

    private static string? Of(char letter) => char.ToLowerInvariant(letter) switch
    {
        'a' or 'à' or 'á' or 'â' or 'ä' or 'æ' => "aa",
        'e' or 'é' or 'è' or 'ê' or 'ë' => "ee",
        'i' or 'í' or 'î' or 'ï' or 'y' => "ih",
        'o' or 'ó' or 'ô' or 'ö' or 'œ' => "oh",
        'u' or 'ú' or 'ù' or 'û' or 'ü' => "ou",
        _ => null,
    };
}
