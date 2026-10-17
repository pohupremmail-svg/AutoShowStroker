using Gooner.Core.Tests.Support;

namespace Gooner.Core.Tests.Localization;

// The scan LocaleFilesTests relies on. A text it misses goes untranslated without a word, so what it
// reads is pinned here.
public class SourceTextsTests
{
    private static List<string> Found(string source) => [.. SourceTexts.ReadSource(source, "x.cs").Texts.Select(t => t.Value)];

    [Fact]
    public void it_finds_what_each_call_hands_over()
    {
        var texts = Found("""
            var a = L10n.T("Close");
            var b = L10n.N("Standard Beat");
            var c = L10n.F("{0} of {1} unlocked", 3, 20);
            var d = L10n.P(count, "once", "{0} times");
            """);

        Assert.Equal(["Close", "Standard Beat", "{0} of {1} unlocked", "once", "{0} times"], texts);
    }

    [Fact]
    public void literals_joined_with_a_plus_are_one_text()
    {
        Assert.Equal(["One long sentence, written over two lines."], Found("""
            L10n.T("One long sentence, " +
                "written over two lines.");
            """));
    }

    [Fact]
    public void escapes_read_as_what_they_stand_for()
    {
        Assert.Equal(["a \"quote\"\nand \U0001f3c6 \u00e9 \\"], Found("""L10n.T("a \"quote\"\nand \U0001f3c6 \u00e9 \\");"""));
        Assert.Equal(["C:\\path \"quoted\""], Found(""""L10n.T(@"C:\path ""quoted""");""""));
    }

    [Fact]
    public void a_text_translated_here_but_written_elsewhere_is_not_a_text_of_this_call()
    {
        Assert.Empty(Found("L10n.T(row.Text); L10n.T(Labels[name]); L10n.P(n, one, other);"));
    }

    [Fact]
    public void comments_and_other_strings_are_not_texts()
    {
        var texts = Found("""
            // L10n.T("in a comment")
            /* L10n.T("in a block comment") */
            var url = "https://example.invalid/L10n.T(\"no\")";
            var hole = $"{(x ? "L10n.T(" : "b")}";
            var c = '"';
            L10n.T("real");
            """);

        Assert.Equal(["real"], texts);
    }

    // A P whose count is an expression with commas of its own still finds both forms.
    [Fact]
    public void the_count_of_a_plural_may_be_any_expression()
    {
        Assert.Equal(["{0} file", "{0} files"], Found("""L10n.P(Math.Max(a, b), "{0} file", "{0} files");"""));
    }

    [Fact]
    public void an_interpolated_text_is_reported_rather_than_skipped()
    {
        var scan = SourceTexts.ReadSource("""L10n.T($"{count} saved");""", "x.cs");

        Assert.Empty(scan.Texts);
        Assert.Single(scan.Problems);
        Assert.StartsWith("x.cs:1:", scan.Problems[0], StringComparison.Ordinal);
    }

    [Fact]
    public void a_literal_joined_with_something_else_is_reported()
    {
        var scan = SourceTexts.ReadSource("""L10n.T("Hello " + name);""", "x.cs");

        Assert.Single(scan.Problems);
    }
}
