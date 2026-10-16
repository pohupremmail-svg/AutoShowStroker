using Gooner.Core.Panels;

namespace Gooner.Core.Tests.Panels;

// Port of tests/test_help_dialog.py: the Guide's topics, as the engine shows them. The Python test
// read the labels' rich text; these read the same text off the topics.
public sealed class GuideTests
{
    private static List<string> Titles => [.. Guide.Topics.Select(t => t.Title)];

    private static string Text(string title) => Guide.Topics.Single(t => t.Title == title).Html;

    [Fact]
    public void has_a_tab_per_help_topic()
    {
        Assert.Contains(Titles, t => t.Contains("Beats", StringComparison.Ordinal) && t.Contains("Rhythm", StringComparison.Ordinal));
        Assert.Contains(Titles, t => t.Contains("Language", StringComparison.Ordinal));
    }

    // Qt took a lone & for a mnemonic marker and needed && for one; a tab here needs no escaping.
    [Fact]
    public void a_tab_title_shows_a_single_ampersand() =>
        Assert.Equal(["Beats & Rhythm", "On-Screen Display", "Languages & Tones", "Keyboard Shortcuts", "Privacy"], Titles);

    [Fact]
    public void beats_tab_explains_pattern_number_meaning()
    {
        var text = Text("Beats & Rhythm");
        Assert.Contains("audible beat", text);
        Assert.Contains("silent step", text);
        Assert.Contains("1 is the longest", text);
        Assert.Contains("4 is the shortest", text);
    }

    [Fact]
    public void languages_tab_explains_adding_a_language()
    {
        var text = Text("Languages & Tones");
        Assert.Contains("res/callouts/", text);
        Assert.Contains("Trigger Key", text);
        Assert.Contains("Manage Custom Phrase Files", text);
    }

    // The folder-per-language/file-per-tone layout is the one thing a phrase author has to get
    // right, and the Guide is the only place in the app that says what it is.
    [Fact]
    public void languages_tab_explains_the_tone_axis()
    {
        var text = Text("Languages & Tones");
        Assert.Contains("tone", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Girlfriend Experience", text);
        Assert.Contains("Degrading", text);
    }

    [Fact]
    public void languages_tab_says_tones_can_be_mixed() =>
        Assert.Contains("mix", Text("Languages & Tones"), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void on_screen_display_tab_documents_the_overlays()
    {
        Assert.Equal(1, Titles.IndexOf("On-Screen Display"));
        var text = Text("On-Screen Display");
        foreach (var expected in new[] { "Session Timer", "Top-left", "Record-Chase", "Top-right", "personal record", "Playback" })
            Assert.Contains(expected, text);
    }

    [Fact]
    public void shortcuts_tab_lists_every_shortcut()
    {
        var text = Text("Keyboard Shortcuts");
        foreach (var expected in new[] { "Ctrl+O", "Right Arrow", "Left Arrow", "Ctrl+Space", "Toggle mute", "Panic", "F11", "Escape", "Ctrl+S", "F1", "Ctrl+Q" })
            Assert.Contains(expected, text);
    }

    [Fact]
    public void privacy_tab_states_everything_stays_local()
    {
        var text = Text("Privacy");
        foreach (var expected in new[] { "100%", "locally", "no telemetry", "no account", "Check for Updates", "only when you click it" })
            Assert.Contains(expected, text);
    }

    // --- where the engine differs ---

    // Every edit names the exact sentence it replaces in the Python text. When that sentence
    // changes there, the edit stops matching and this fails, so the difference is looked at again
    // rather than silently left behind.
    [Fact]
    public void every_engine_edit_finds_its_sentence_exactly_once()
    {
        foreach (var edit in Guide.EngineEdits)
        {
            var source = Guide.PythonTopics.Single(t => Guide.DisplayTitle(t.Title) == edit.Topic).Html;
            var count = (source.Length - source.Replace(edit.Old, "", StringComparison.Ordinal).Length) / edit.Old.Length;
            Assert.True(count == 1, $"{edit.Topic}: \"{edit.Old}\" found {count} times");
        }
    }

    // The engine keeps its settings in a file next to everything else, not in the registry.
    [Fact]
    public void the_engine_says_where_its_settings_are()
    {
        var text = Text("Privacy");
        Assert.DoesNotContain("HKEY_CURRENT_USER", text);
        Assert.Contains("settings.json", text);
    }

    // The engine ships as a folder, not one .exe - and its data is one place now, not two.
    [Fact]
    public void the_engine_speaks_of_the_folder_it_ships_as()
    {
        var text = Text("Privacy");
        Assert.DoesNotContain(".exe", text);
        Assert.Contains("deleting its folder", text);
    }

    [Fact]
    public void only_the_edits_differ_from_the_python_text()
    {
        foreach (var (python, engine) in Guide.PythonTopics.Zip(Guide.Topics))
        {
            var expected = Guide.EngineEdits.Where(e => e.Topic == Guide.DisplayTitle(python.Title))
                .Aggregate(python.Html, (html, edit) => html.Replace(edit.Old, edit.New, StringComparison.Ordinal));
            Assert.Equal(expected, engine.Html);
        }
    }
}
