using Gooner.Core.App;
using Gooner.Core.Panels;

namespace Gooner.Core.Tests.Panels;

// The Guide's topics (ported from the Python app's tests/test_help_dialog.py, which read the same
// text off the dialog's labels).
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

    [Fact]
    public void the_tabs_in_their_order() =>
        Assert.Equal(["Beats & Rhythm", "On-Screen Display", "Characters", "Languages & Tones", "Keyboard Shortcuts", "Privacy"], Titles);

    // Translated a block at a time - a heading, a paragraph, a list - so a sentence reworded costs
    // the translation of its paragraph rather than of the whole topic.
    [Fact]
    public void every_topic_is_cut_into_its_blocks_without_a_character_lost()
    {
        foreach (var topic in Guide.Topics)
        {
            var blocks = RichText.Blocks(topic.Html);

            Assert.Equal(topic.Html, string.Concat(blocks));
            Assert.All(blocks, block => Assert.Matches(new System.Text.RegularExpressions.Regex(@"^<(h3|p|ul|ol)>.*</\1>$", System.Text.RegularExpressions.RegexOptions.Singleline), block));
        }
    }

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
        foreach (var expected in new[] { "Session Timer", "Top-left", "Record-Chase", "Top-center", "personal record", "Playback" })
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

    // Every track the Look tab offers, under the name it offers it by.
    [Fact]
    public void the_note_track_is_explained_in_every_style()
    {
        var text = Text("On-Screen Display");
        Assert.Contains("Settings &gt; Look", text);
        foreach (var track in Look.Choices["note_track"])
            Assert.Contains(track.Label, text);
    }

    // Where a model goes is the one thing someone with a .vrm needs to know, and the app has no
    // other place that says it.
    [Fact]
    public void characters_tab_says_where_models_go()
    {
        var text = Text("Characters");
        foreach (var expected in new[] { ".vrm", "Settings &gt; Look", "Your own models", "Choose Folder...", "res\\characters", "VRoid Studio" })
            Assert.Contains(expected, text);
    }

    // A model is somebody's work, and the app does not read its terms for the user.
    [Fact]
    public void characters_tab_says_the_model_licence_is_the_users_to_check() =>
        Assert.Contains("terms of use", Text("Characters"));

    [Fact]
    public void characters_tab_explains_what_happens_to_a_model_that_crashes() =>
        Assert.Contains("switched off", Text("Characters"));

    // --- this app, not the Python one ---

    // Settings are in a file next to everything else, not in the registry.
    [Fact]
    public void the_engine_says_where_its_settings_are()
    {
        var text = Text("Privacy");
        Assert.DoesNotContain("HKEY_CURRENT_USER", text);
        Assert.Contains("settings.json", text);
    }

    // It ships as a folder, not one .exe - and its data is one place, not two.
    [Fact]
    public void the_engine_speaks_of_the_folder_it_ships_as()
    {
        var text = Text("Privacy");
        Assert.DoesNotContain(".exe", text);
        Assert.Contains("deleting its folder", text);
    }

    // The graphics cache is the one thing written that is not the app's own data.
    [Fact]
    public void the_privacy_tab_names_the_graphics_cache() =>
        Assert.Contains("graphics cache", Text("Privacy"));

    // There is no menu bar, no footer and no button row: what was under Help, Sessions and
    // Statistics is on the start screen (and the dock's menu), and the buttons are the dock's.
    [Fact]
    public void the_engine_sends_you_nowhere_its_window_does_not_have()
    {
        foreach (var topic in Guide.Topics)
        {
            foreach (var gone in new[] { "Help &gt;", "Sessions &gt;", "Statistics &gt;", "Skip &gt;&gt;", "&lt;&lt; Previous", "next to Mute", "next to Skip", "footer", "along the bottom", "Python" })
                Assert.DoesNotContain(gone, topic.Html);
        }
        Assert.Contains("start screen", Text("Privacy"));
    }
}
