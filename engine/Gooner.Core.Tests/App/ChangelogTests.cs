using Gooner.Core.App;
using Gooner.Core.Tests.Support;

namespace Gooner.Core.Tests.App;

// Port of tests/test_changelog.py, plus the rich text the engine draws the entries with.
public sealed class ChangelogTests
{
    private static List<ChangelogEntry> Log(params string[] versions) =>
        [.. versions.Select((v, i) => new ChangelogEntry(v, $"text {i}"))];

    private static List<string> Versions(IEnumerable<ChangelogEntry> entries) => [.. entries.Select(e => e.Version)];

    [Fact]
    public void parse_version_splits_into_ints() =>
        Assert.Equal([1, 2, 3], Changelog.ParseVersion("1.2.3"));

    [Fact]
    public void parse_version_handles_two_part_versions() =>
        Assert.Equal([0, 1], Changelog.ParseVersion("0.1"));

    [Fact]
    public void entries_since_empty_last_seen_returns_everything_up_to_current() =>
        Assert.Equal(["0.1.0", "0.2.0"], Versions(Changelog.EntriesSince("", "0.2.0", Log("0.1.0", "0.2.0", "0.3.0"))));

    [Fact]
    public void entries_since_returns_only_versions_newer_than_last_seen() =>
        Assert.Equal(["0.2.0", "0.3.0"], Versions(Changelog.EntriesSince("0.1.0", "0.3.0", Log("0.1.0", "0.2.0", "0.3.0"))));

    [Fact]
    public void entries_since_same_version_returns_nothing() =>
        Assert.Empty(Changelog.EntriesSince("0.2.0", "0.2.0", Log("0.1.0", "0.2.0")));

    // "0.10.0" > "0.9.0" numerically, even though it sorts before it as a plain string.
    [Fact]
    public void entries_since_compares_numerically_not_lexically() =>
        Assert.Equal(["0.10.0"], Versions(Changelog.EntriesSince("0.9.0", "0.10.0", Log("0.9.0", "0.10.0"))));

    [Fact]
    public void entries_since_preserves_changelog_order() =>
        Assert.Equal(["0.1.0", "0.2.0", "0.3.0"], Versions(Changelog.EntriesSince("", "0.3.0", Log("0.1.0", "0.2.0", "0.3.0"))));

    // Python compares the tuples: a shorter one that is a prefix of a longer one is the smaller.
    [Fact]
    public void a_two_part_version_comes_before_its_three_part_continuation() =>
        Assert.Equal(["0.1.0"], Versions(Changelog.EntriesSince("0.1", "0.1.0", Log("0.1", "0.1.0"))));

    // Catches exactly the mistake that happened live in this project once already: a PR bumped
    // VERSION without adding the matching CHANGELOG entry. 0.0.0 is the one exempt placeholder,
    // for a fresh checkout with no releases cut yet.
    [Fact]
    public void version_file_has_a_matching_changelog_entry()
    {
        var version = File.ReadAllText(Path.Combine(Repo.Root, "VERSION")).Trim();
        if (version == "0.0.0") return;
        Assert.Contains(version, Versions(Changelog.Entries));
    }

    [Fact]
    public void every_version_parses_and_none_is_listed_twice()
    {
        foreach (var entry in Changelog.Entries)
            Assert.NotEmpty(Changelog.ParseVersion(entry.Version));
        Assert.Equal(Changelog.Entries.Count, Changelog.Entries.Select(e => e.Version).Distinct().Count());
    }

    // --- the rich text ---

    [Fact]
    public void bold_italic_and_code_become_their_bbcode()
    {
        Assert.Equal("[b]a[/b] [i]b[/i] [code]c[/code]", RichText.ToBbcode("<b>a</b> <i>b</i> <code>c</code>"));
    }

    // The lead sentence, the bullets, the closing hook - each on its own line, as Qt laid them out.
    [Fact]
    public void a_list_becomes_a_bulleted_block_between_lead_and_hook()
    {
        Assert.Equal("Lead.\n[ul]one\ntwo[/ul]\nHook.", RichText.ToBbcode("Lead.<ul><li>one</li><li>two</li></ul>Hook."));
    }

    [Fact]
    public void entities_become_their_characters()
    {
        Assert.Equal("Settings > Beat & Rhythm \"x\" <y> 'z'",
            RichText.ToBbcode("Settings &gt; Beat &amp; Rhythm &quot;x&quot; &lt;y&gt; &#39;z&#39;"));
    }

    // A bracket in the text would otherwise open a tag of its own.
    [Fact]
    public void a_square_bracket_in_the_text_stays_text()
    {
        Assert.Equal("[lb]1[rb] and [b]x[/b]", RichText.ToBbcode("[1] and <b>x</b>"));
    }

    [Fact]
    public void every_shipped_entry_converts_without_a_tag_left_over()
    {
        foreach (var entry in Changelog.Entries)
        {
            var bbcode = RichText.ToBbcode(entry.Html);
            Assert.DoesNotContain("<", bbcode);
            Assert.DoesNotContain("&amp;", bbcode);
        }
    }
}
