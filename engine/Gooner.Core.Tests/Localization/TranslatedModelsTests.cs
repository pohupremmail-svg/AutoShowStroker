using System.Text.Json.Nodes;
using Gooner.Core.Achievements;
using Gooner.Core.App;
using Gooner.Core.Beat;
using Gooner.Core.Callouts;
using Gooner.Core.Data;
using Gooner.Core.Formatting;
using Gooner.Core.Localization;
using Gooner.Core.Logging;
using Gooner.Core.Panels;
using Gooner.Core.Sessions;
using Gooner.Core.Settings;
using Gooner.Core.Tests.Support;
using Gooner.Core.Timing;

namespace Gooner.Core.Tests.Localization;

// The models the panels draw say what they say in the language in use. Run under the pseudo
// catalogue, which brackets every text that went through one: an unbracketed text is one that
// stays English in every language. What the code keeps as a name - a settings tab, a rhythm - stays
// English underneath and is translated where it is shown.
public sealed class TranslatedModelsTests : IDisposable
{
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-translated-");
    private readonly JsonSettingsStore _settings;
    private readonly GoonerApp _app;

    public TranslatedModelsTests()
    {
        var data = new UserDataStore(Path.Combine(_tmp.FullName, "data"));
        _settings = new JsonSettingsStore(data);
        _app = new GoonerApp(new FakeStage(), new FrameClock(1_800_000_000.0), new GoonerAppOptions
        {
            Settings = _settings,
            DataStore = data,
            CalloutDir = Repo.PathTo(Path.Combine("res", "callouts")),
            Random = new ScriptedRandom(1),
        });
        L10n.Use(Catalogue.Pseudo);
    }

    public void Dispose()
    {
        L10n.Use(null);
        AppLog.Configure(false, null);
        _tmp.Delete(recursive: true);
    }

    [Fact]
    public void the_settings_are_written_in_it()
    {
        var form = new SettingsForm(_app);

        Assert.Equal("⟦Min. duration (s):⟧", form.Numbers["min_dur"].Label);
        Assert.Equal("⟦Climax prompts active⟧", form.Toggles["climax_active"].Label);
        Assert.StartsWith("⟦", form.Toggles["ask_for_outcome"].Tooltip, StringComparison.Ordinal);
        Assert.Contains(form.Tabs[0].Rows.OfType<SectionRow>(), section => section.Title == "⟦Sound⟧");
        Assert.Equal("⟦Highway⟧", form.Choices["note_track"].Options[0].Label);
        Assert.Equal("Playback", form.Tabs[0].Title);
        Assert.Equal("⟦Playback⟧", form.Tabs[0].DisplayTitle);
    }

    [Fact]
    public void a_refusal_of_the_settings_is_written_in_it()
    {
        var form = new SettingsForm(_app);
        form.Numbers["min_dur"].Value = 30;
        form.Numbers["max_dur"].Value = 10;

        Assert.Equal("⟦⟦Slideshow duration⟧: the minimum must not be higher than the maximum.⟧", form.ValidationError());
    }

    [Fact]
    public void the_recap_is_written_in_it()
    {
        var stats = new JsonObject
        {
            ["total_dur_sec"] = 125.0, ["pause_dur_sec"] = 10.0, ["average_pause_dur_sec"] = 5.0, ["total_num_pauses"] = 2,
            ["total_num_beat"] = 50, ["total_num_beat_change"] = 5, ["average_beat_speed"] = 0.4, ["average_beat_speed_active"] = 0.43,
            ["most_used_pattern"] = "Standard Beat", ["skips"] = 1, ["repeats"] = 0, ["climax_outcome"] = "denied", ["fakeout_count"] = 3,
        };
        var report = new StatisticsReport(stats, new Dictionary<string, double>(), null, [AchievementCatalogue.All[0]]);

        Assert.Equal("⟦Session over.\nNot today - no cumming for you.⟧", report.Title);
        Assert.All(report.Rows, row => Assert.StartsWith("⟦", row.Metric, StringComparison.Ordinal));
        Assert.StartsWith("⟦You survived a total of", report.Conclusion, StringComparison.Ordinal);
        Assert.Contains("⟦Standard Beat⟧", report.Conclusion, StringComparison.Ordinal);
        Assert.Equal($"⟦✦ Achievement unlocked: ⟦{AchievementCatalogue.All[0].Name}⟧⟧", report.AchievementCards[0].Title);
    }

    [Fact]
    public void the_achievements_are_written_in_it()
    {
        var overview = new AchievementsOverview(new AchievementTracker(dataStore: null), []);

        var endurance = overview.Tiles[0];
        Assert.Equal("⟦Endurance⟧", endurance.Title);
        Assert.Equal($"⟦{AchievementCatalogue.All[0].Description}⟧", endurance.Detail);
        Assert.StartsWith("⟦", overview.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void the_guide_is_written_in_it()
    {
        var shown = Guide.Translated();

        Assert.Equal(Guide.Topics.Select(t => $"⟦{t.Title}⟧"), shown.Select(t => t.Title));
        Assert.Equal(string.Concat(RichText.Blocks(Guide.Topics[0].Html).Select(block => $"⟦{block}⟧")), shown[0].Html);
    }

    [Fact]
    public void the_privacy_categories_are_written_in_it()
    {
        var privacy = new PrivacyData(_app);

        Assert.All(PrivacyData.Categories, category => Assert.StartsWith("⟦", privacy.LabelFor(category.Key), StringComparison.Ordinal));
    }

    [Fact]
    public void the_windows_own_words_are_written_in_it()
    {
        Assert.Equal("⟦I reached my Edge⟧", _app.EdgeButtonText);
        Assert.Equal("⟦Flirty⟧", CalloutHandler.ToneLabel("flirty"));
        Assert.Equal("⟦2 Min⟧", Clock.FormatDuration(120));
    }

    // A built-in rhythm is shown under its translated name; a pattern the user named is shown as
    // they named it. Underneath both keep the name they are stored and replayed under.
    [Fact]
    public void a_built_in_rhythm_is_shown_translated_and_a_custom_one_as_named()
    {
        Assert.Equal("⟦Standard Beat⟧", BeatPatterns.DisplayName("Standard Beat"));
        Assert.Equal("My Own Groove", BeatPatterns.DisplayName("My Own Groove"));
        Assert.Contains("Standard Beat", BeatPatterns.BuiltIn.Keys);
    }
}
