using Gooner.Core.Beat;
using Gooner.Core.Data;
using Gooner.Core.Panels;
using Gooner.Core.Settings;

namespace Gooner.Core.Tests.Panels;

// Port of tests/test_pattern_step_widget.py: one step of a pattern - a length and whether it
// sounds - encoded as the signed weight BeatHandler reads. The bar is 100 px high in the dialog;
// where the Python test dragged it, this asks what a drag to that height means.
public sealed class PatternStepTests
{
    private const double BarHeight = 100;

    [Fact]
    public void default_value_roundtrips() => Assert.Equal(2, new PatternStep(2).Value);

    [Fact]
    public void set_value_updates_audible_and_weight()
    {
        var step = new PatternStep(1);
        step.Value = -3;
        Assert.Equal(-3, step.Value);
    }

    [Fact]
    public void set_value_clamps_weight_to_valid_range()
    {
        var step = new PatternStep(1);
        step.Value = 9;
        Assert.Equal(PatternStep.MaxWeight, step.Value);
        step.Value = -9;
        Assert.Equal(-PatternStep.MaxWeight, step.Value);
    }

    // audible = value > 0: a zero is a pause, of the longest length.
    [Fact]
    public void a_zero_is_a_pause_of_the_longest_length() => Assert.Equal(-PatternStep.MinWeight, new PatternStep(0).Value);

    [Fact]
    public void pause_button_reflects_initial_muted_state()
    {
        Assert.False(new PatternStep(2).Muted);
        Assert.True(new PatternStep(-2).Muted);
    }

    [Fact]
    public void clicking_pause_button_toggles_muted_state_and_preserves_weight()
    {
        var step = new PatternStep(2);

        step.Muted = true;
        Assert.Equal(-2, step.Value);
        step.Muted = false;
        Assert.Equal(2, step.Value);
    }

    [Fact]
    public void pause_button_text_reflects_state()
    {
        var step = new PatternStep(1);
        Assert.Equal("Beat", step.ButtonText);

        step.Muted = true;
        Assert.Equal("Pause", step.ButtonText);
    }

    [Fact]
    public void set_value_updates_pause_button_state()
    {
        var step = new PatternStep(1);
        step.Value = -1;
        Assert.True(step.Muted);
        Assert.Equal("Pause", step.ButtonText);
    }

    // The core fix: adjusting a paused step's length must not flip it back to audible just
    // because the bar was touched.
    [Fact]
    public void dragging_bar_sets_weight_without_changing_muted_state()
    {
        var step = new PatternStep(-3);

        step.Weight = PatternStep.WeightAt(2, BarHeight);

        Assert.Equal(-PatternStep.MinWeight, step.Value);
    }

    [Fact]
    public void drag_near_top_of_bar_sets_min_weight_ie_longest_step() =>
        Assert.Equal(PatternStep.MinWeight, PatternStep.WeightAt(2, BarHeight));

    [Fact]
    public void drag_near_bottom_of_bar_sets_max_weight_ie_shortest_step() =>
        Assert.Equal(PatternStep.MaxWeight, PatternStep.WeightAt(118, BarHeight));

    // round() on the Python side: half to even, so the exact middle of a level goes to the even one.
    [Theory]
    [InlineData(0, 1)]
    [InlineData(16.6, 1)]
    [InlineData(50, 3)]
    [InlineData(83.4, 4)]
    [InlineData(-5, 1)]
    public void a_height_reads_as_the_nearest_level(double y, int weight) =>
        Assert.Equal(weight, PatternStep.WeightAt(y, BarHeight));

    // Weight 1, the longest step, draws tallest; the shortest still shows a sliver.
    [Fact]
    public void the_bar_is_tallest_for_the_longest_step()
    {
        Assert.Equal(1.0, new PatternStep(1).BarRatio);
        Assert.Equal(0.0, new PatternStep(4).BarRatio);
    }

    [Fact]
    public void bar_tooltip_explains_duration_semantics() =>
        Assert.Contains("duration", new PatternStep(1).Tooltip, StringComparison.OrdinalIgnoreCase);
}

// Port of tests/test_pattern_editor_dialog.py: the step sequencer for the user's own rhythms,
// without its widgets. The preview is the step it is on and how long to wait for the next; the
// engine runs the timer and plays the click.
public sealed class PatternEditorTests : IDisposable
{
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-patterns-");
    private readonly BeatHandler _beat;

    public PatternEditorTests()
    {
        var data = new UserDataStore(_tmp.FullName);
        _beat = new BeatHandler(new JsonSettingsStore(data), data);
    }

    public void Dispose() => _tmp.Delete(recursive: true);

    private PatternEditor Editor() => new(_beat);

    [Fact]
    public void pattern_list_only_shows_custom_patterns()
    {
        _beat.AddOrUpdateCustomPattern("My Pattern", [1, -1]);

        Assert.Equal(["My Pattern"], Editor().PatternNames);
    }

    [Fact]
    public void new_pattern_starts_with_default_steps()
    {
        var editor = Editor();

        Assert.Equal(PatternEditor.DefaultSteps, editor.CurrentSteps);
        Assert.Equal("", editor.Name);
        Assert.Null(editor.Selected);
    }

    [Fact]
    public void save_creates_new_custom_pattern()
    {
        var editor = Editor();
        editor.Name = "My Pattern";
        editor.Steps[0].Value = -2;

        Assert.True(editor.Save());

        Assert.Equal(-2, _beat.CustomBeatPatterns["My Pattern"][0]);
        Assert.Equal(["My Pattern"], editor.PatternNames);
        Assert.Equal("My Pattern", editor.Selected);
    }

    [Fact]
    public void save_with_empty_name_shows_error_and_does_not_persist()
    {
        var editor = Editor();
        editor.Name = "";

        Assert.False(editor.Save());

        Assert.Empty(_beat.CustomBeatPatterns);
        Assert.NotEqual("", editor.Error);
    }

    [Fact]
    public void save_with_no_audible_steps_shows_error()
    {
        var editor = Editor();
        editor.Name = "All Muted";
        foreach (var step in editor.Steps) step.Muted = true;

        editor.Save();

        Assert.DoesNotContain("All Muted", _beat.CustomBeatPatterns.Keys);
        Assert.NotEqual("", editor.Error);
    }

    [Fact]
    public void save_with_name_colliding_with_builtin_shows_error()
    {
        var editor = Editor();
        editor.Name = "Standard Beat";

        editor.Save();

        Assert.NotEqual("", editor.Error);
        Assert.Equal([1], _beat.AvailableBeatPatterns["Standard Beat"]);
    }

    [Fact]
    public void a_saved_pattern_clears_the_last_error()
    {
        var editor = Editor();
        editor.Save();

        editor.Name = "Fine";
        editor.Save();

        Assert.Equal("", editor.Error);
    }

    [Fact]
    public void selecting_existing_pattern_loads_it_into_editor()
    {
        _beat.AddOrUpdateCustomPattern("My Pattern", [2, -3, 1]);
        var editor = Editor();

        editor.Select("My Pattern");

        Assert.Equal("My Pattern", editor.Name);
        Assert.Equal([2, -3, 1], editor.CurrentSteps);
    }

    [Fact]
    public void editing_existing_pattern_updates_in_place()
    {
        _beat.AddOrUpdateCustomPattern("My Pattern", [1, 1]);
        var editor = Editor();
        editor.Select("My Pattern");

        editor.Steps[0].Value = 4;
        editor.Save();

        Assert.Equal(["My Pattern"], _beat.CustomBeatPatterns.Keys);
        Assert.Equal([4, 1], _beat.CustomBeatPatterns["My Pattern"]);
        Assert.Equal(["My Pattern"], editor.PatternNames); // no duplicate entry
    }

    [Fact]
    public void renaming_existing_pattern_removes_old_name()
    {
        _beat.AddOrUpdateCustomPattern("Old Name", [1, 1]);
        var editor = Editor();
        editor.Select("Old Name");

        editor.Name = "New Name";
        editor.Save();

        Assert.DoesNotContain("Old Name", _beat.CustomBeatPatterns.Keys);
        Assert.Equal([1, 1], _beat.CustomBeatPatterns["New Name"]);
    }

    [Fact]
    public void delete_removes_selected_pattern()
    {
        _beat.AddOrUpdateCustomPattern("My Pattern", [1, 1]);
        var editor = Editor();
        editor.Select("My Pattern");

        editor.DeleteSelected();

        Assert.DoesNotContain("My Pattern", _beat.CustomBeatPatterns.Keys);
        Assert.Empty(editor.PatternNames);
        Assert.Equal(PatternEditor.DefaultSteps, editor.CurrentSteps);
    }

    [Fact]
    public void deleting_with_nothing_selected_does_nothing()
    {
        _beat.AddOrUpdateCustomPattern("My Pattern", [1, 1]);

        Editor().DeleteSelected();

        Assert.Contains("My Pattern", _beat.CustomBeatPatterns.Keys);
    }

    [Fact]
    public void add_step_appends_a_step_widget()
    {
        var editor = Editor();
        var before = editor.Steps.Count;

        editor.AddStep();

        Assert.Equal(before + 1, editor.Steps.Count);
        Assert.Equal(1, editor.Steps[^1].Value);
    }

    [Fact]
    public void add_step_is_capped_at_max_steps()
    {
        var editor = Editor();
        for (var i = 0; i < PatternEditor.MaxSteps + 5; i++) editor.AddStep();

        Assert.Equal(PatternEditor.MaxSteps, editor.Steps.Count);
    }

    [Fact]
    public void remove_step_cannot_go_below_one_step()
    {
        var editor = Editor();
        for (var i = 0; i < 10; i++) editor.RemoveStep();

        Assert.Single(editor.Steps);
    }

    // --- preview ---

    [Fact]
    public void preview_tick_plays_sound_only_for_audible_steps()
    {
        var editor = Editor();
        editor.SetSteps([1, -1]);
        editor.StartPreview();

        var first = editor.PreviewTick(); // step 0 is audible
        var second = editor.PreviewTick(); // step 1 is muted

        Assert.True(first.Click);
        Assert.False(second.Click);
    }

    // Weight is an inverse-duration multiplier: a "4" is a quarter as long as a "1". The preview
    // must reflect that instead of ticking at a flat interval.
    [Fact]
    public void preview_tick_schedules_interval_inversely_proportional_to_weight()
    {
        var editor = Editor();
        editor.SetSteps([1, 4]);
        editor.StartPreview();

        var forWeight1 = editor.PreviewTick().NextInMs;
        var forWeight4 = editor.PreviewTick().NextInMs;

        Assert.Equal(PatternEditor.PreviewBaseStepMs, forWeight1);
        Assert.Equal(PatternEditor.PreviewBaseStepMs / 4, forWeight4);
    }

    [Fact]
    public void the_preview_lights_the_step_it_is_on_and_goes_round()
    {
        var editor = Editor();
        editor.SetSteps([1, 2]);
        editor.StartPreview();

        Assert.Equal(0, editor.PreviewTick().Step);
        Assert.Equal(1, editor.PreviewTick().Step);
        Assert.Equal(0, editor.PreviewTick().Step);
        Assert.True(editor.Previewing);
    }

    // Loading another pattern, closing the panel: the preview stops, or it would go on clicking.
    [Fact]
    public void loading_a_pattern_stops_the_preview()
    {
        _beat.AddOrUpdateCustomPattern("My Pattern", [1, 1]);
        var editor = Editor();
        editor.StartPreview();

        editor.Select("My Pattern");

        Assert.False(editor.Previewing);
    }
}
