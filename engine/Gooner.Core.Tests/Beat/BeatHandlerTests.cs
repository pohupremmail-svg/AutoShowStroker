using System.Globalization;
using System.Text.Json.Nodes;
using Gooner.Core.Beat;
using Gooner.Core.Data;
using Gooner.Core.Migration;
using Gooner.Core.Settings;
using Gooner.Core.Tests.Support;

namespace Gooner.Core.Tests.Beat;

// Port of tests/test_beat_handler.py. The Qt timers are clock timers the host advances, and the
// clock is a FakeTime, so where Python allows a millisecond of slack for a real timer these are
// exact. Not ported, because what they guard does not exist here: the QMutex release tests (no
// mutex - the core is driven from one thread), the PreciseTimer type, and the QSoundEffect and its
// wav path (sound is the host's). custom_patterns_migrate_out_of_settings_on_first_load runs the
// startup import (ProfileMigration) where Python's handler migrated on its first load.
public sealed class BeatHandlerTests : IDisposable
{
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-beat-");
    private readonly FakeTime _time = new();
    private readonly BeatHandler _handler;

    public BeatHandlerTests() => _handler = Handler();

    public void Dispose()
    {
        _handler.Stop();
        _tmp.Delete(recursive: true);
    }

    private BeatHandler Handler(ISettingsStore? settings = null, UserDataStore? dataStore = null) =>
        new(settings, dataStore, new ScriptedRandom(), _time);

    private JsonSettingsStore Settings() => new(new UserDataStore(Path.Combine(_tmp.FullName, "settings")));

    private UserDataStore Store() => new(Path.Combine(_tmp.FullName, "appdata"));

    private static List<T> Collect<T>(Action<Action<T>> subscribe)
    {
        var seen = new List<T>();
        subscribe(seen.Add);
        return seen;
    }

    [Fact]
    public void default_selected_patterns_is_all_patterns() =>
        Assert.Equal(BeatHandler.BeatPatternsMap.Keys.ToHashSet(), _handler.SelectedBeatPatterns.ToHashSet());

    [Fact]
    public void defaults_dict_matches_init_defaults()
    {
        var actual = new Dictionary<string, object>
        {
            ["max_beat_dur"] = _handler.MaxBeatDur,
            ["min_beat_dur"] = _handler.MinBeatDur,
            ["max_beat_freq"] = _handler.MaxBeatFreq,
            ["min_beat_freq"] = _handler.MinBeatFreq,
            ["min_pause_dur"] = _handler.MinPauseDur,
            ["max_pause_dur"] = _handler.MaxPauseDur,
            ["pause_chance"] = _handler.PauseChance,
            ["edge_relief_active"] = _handler.EdgeReliefActive,
            ["edge_pause_dur"] = _handler.EdgePauseDur,
            ["edge_cooldown_sec"] = _handler.EdgeCooldownSec,
            ["ramping_active"] = _handler.RampingActive,
            ["min_ramp_duration"] = _handler.MinRampDuration,
            ["max_ramp_duration"] = _handler.MaxRampDuration,
            ["ramp_window_width"] = _handler.RampWindowWidth,
            ["beat_loudness"] = _handler.BeatLoudness,
        };
        Assert.Equal(BeatHandler.Defaults.Keys.Order(), actual.Keys.Order());
        foreach (var (key, value) in BeatHandler.Defaults)
            Assert.True(Convert.ToDouble(value, CultureInfo.InvariantCulture) == Convert.ToDouble(actual[key], CultureInfo.InvariantCulture), key);
    }

    [Fact]
    public void settings_override_defaults()
    {
        var settings = Settings();
        settings.SetValue("BeatHandler/min_beat_freq", 2.0);
        settings.SetValue("BeatHandler/max_beat_freq", 2.0);
        settings.SetValue("BeatHandler/selected_beat_patterns", new JsonArray("Standard Beat"));
        settings.SetValue("BeatHandler/ramping_active", false);
        settings.SetValue("BeatHandler/min_ramp_duration", 111.0);
        settings.SetValue("BeatHandler/max_ramp_duration", 222.0);
        settings.SetValue("BeatHandler/ramp_window_width", 0.25);

        var handler = Handler(settings);

        Assert.Equal(2.0, handler.MinBeatFreq);
        Assert.Equal(2.0, handler.MaxBeatFreq);
        Assert.Equal(["Standard Beat"], handler.SelectedBeatPatterns);
        Assert.False(handler.RampingActive);
        Assert.Equal(111.0, handler.MinRampDuration);
        Assert.Equal(222.0, handler.MaxRampDuration);
        Assert.Equal(0.25, handler.RampWindowWidth);
    }

    [Fact]
    public void set_muted_mutes_and_unmutes()
    {
        _handler.SetMuted(true);
        Assert.True(_handler.IsMuted);

        _handler.SetMuted(false);
        Assert.False(_handler.IsMuted);
    }

    [Fact]
    public void beat_segment_only_picks_from_selected_patterns()
    {
        _handler.SelectedBeatPatterns = ["Standard Beat"];
        _handler.StartBeat();
        Assert.Equal(BeatHandler.BeatPatternsMap["Standard Beat"], _handler.CurrentBeatPattern);
    }

    [Fact]
    public void beat_segment_frequency_within_bounds()
    {
        _handler.MinBeatFreq = 1.0;
        _handler.MaxBeatFreq = 1.0;
        _handler.StartBeat();
        Assert.Equal(1.0, _handler.CurFreq);
    }

    [Fact]
    public void beat_segment_emits_beat_change_event()
    {
        var changes = new List<(double Freq, string Pattern)>();
        _handler.BeatChanged += (freq, pattern) => changes.Add((freq, pattern));
        _handler.StartBeat();
        Assert.Equal(_handler.CurFreq, changes[0].Freq);
    }

    [Fact]
    public void start_pause_duration_within_bounds()
    {
        _handler.MinPauseDur = 5;
        _handler.MaxPauseDur = 5;
        _handler.StartPause();
        Assert.Equal(5, _handler.CurPauseDur);
    }

    [Fact]
    public void start_pause_emits_paused_event()
    {
        var paused = 0;
        _handler.BeatPaused += () => paused++;
        _handler.StartPause();
        Assert.Equal(1, paused);
    }

    [Fact]
    public void pause_loop_counts_down_without_resuming()
    {
        _handler.CurPauseDur = 3;
        _handler.PauseLoop();
        Assert.Equal(2, _handler.CurPauseDur);
    }

    [Fact]
    public void pause_loop_resumes_and_resets_frequency()
    {
        var resumed = 0;
        _handler.BeatResumed += () => resumed++;
        _handler.CurPauseDur = 1;
        _handler.PauseLoop();
        Assert.Equal(1, resumed);
        Assert.NotEqual(0, _handler.CurFreq);  // the next planned segment goes on the air right away
    }

    [Fact]
    public void stop_reports_the_meter_idle()
    {
        var states = Collect<string>(h => _handler.MeterUpdated += h);
        _handler.Stop();
        Assert.Equal(["idle"], states);
    }

    [Fact]
    public void a_new_segment_reports_a_new_beat()
    {
        var states = Collect<string>(h => _handler.MeterUpdated += h);
        _handler.StartBeat();
        Assert.Equal("new_beat", states[0]);
    }

    // The meter gets a state, and the track draws the countdown as a bar - so what a pause has to
    // expose is its progress, which CurPauseDur alone cannot answer because it does not know the
    // total.
    [Fact]
    public void a_pause_reports_itself_and_how_far_it_has_run()
    {
        _handler.MinPauseDur = _handler.MaxPauseDur = 4;
        var states = Collect<string>(h => _handler.MeterUpdated += h);
        _handler.StartPause();
        Assert.Equal("pause", states[0]);
        Assert.Equal(1.0, _handler.PauseProgress(), 2);

        _handler.PauseLoop();
        Assert.InRange(_handler.PauseProgress(), 0.6, 0.9);

        _handler.CurPauseDur = 1;
        _handler.PauseLoop();
        Assert.Equal(0.0, _handler.PauseProgress());  // over, and nothing left to draw
    }

    [Fact]
    public void nothing_is_paused_before_a_pause_starts() => Assert.Equal(0.0, _handler.PauseProgress());

    // --- difficulty ramping ---

    [Fact]
    public void current_freq_range_full_corridor_when_ramping_inactive()
    {
        _handler.MinBeatFreq = 1.0;
        _handler.MaxBeatFreq = 5.0;
        _handler.RampingActive = false;
        Assert.Equal((1.0, 5.0), _handler.CurrentFreqRange());
    }

    [Fact]
    public void current_freq_range_full_corridor_before_start_beat_called()
    {
        _handler.MinBeatFreq = 1.0;
        _handler.MaxBeatFreq = 5.0;
        _handler.RampingActive = true;
        Assert.Equal(0.0, _handler.RampTargetDuration);
        Assert.Equal((1.0, 5.0), _handler.CurrentFreqRange());
    }

    [Fact]
    public void current_freq_range_collapses_when_corridor_has_zero_width()
    {
        _handler.MinBeatFreq = 3.0;
        _handler.MaxBeatFreq = 3.0;
        _handler.RampingActive = true;
        _handler.MinRampDuration = 10.0;
        _handler.MaxRampDuration = 10.0;
        _handler.StartBeat();
        Assert.Equal((3.0, 3.0), _handler.CurrentFreqRange());
    }

    private void Ramp100()
    {
        _handler.MinBeatFreq = 1.0;
        _handler.MaxBeatFreq = 5.0;
        _handler.RampingActive = true;
        _handler.RampWindowWidth = 0.4;
        _handler.MinRampDuration = 100.0;
        _handler.MaxRampDuration = 100.0;
        _time.Now = 1000.0;
        _handler.StartBeat();
    }

    [Fact]
    public void current_freq_range_sits_at_bottom_at_ramp_start()
    {
        Ramp100();

        var (windowMin, windowMax) = _handler.CurrentFreqRange();
        Assert.Equal(1.0, windowMin, 9);
        Assert.Equal(2.6, windowMax, 9);
    }

    [Fact]
    public void current_freq_range_slides_up_partway_through_ramp()
    {
        Ramp100();

        _time.Now = 1050.0;  // halfway (progress=0.5)
        var (windowMin, windowMax) = _handler.CurrentFreqRange();
        var corridor = 4.0;
        var width = 0.4 * corridor;
        var expectedMin = 1.0 + 0.5 * (corridor - width);
        Assert.Equal(expectedMin, windowMin, 9);
        Assert.Equal(expectedMin + width, windowMax, 9);
    }

    [Fact]
    public void current_freq_range_caps_at_top_once_ramp_target_elapsed()
    {
        Ramp100();

        _time.Now = 5000.0;  // way past target
        var (windowMin, windowMax) = _handler.CurrentFreqRange();
        Assert.Equal(5.0, windowMax, 9);
        Assert.Equal(3.4, windowMin, 9);
    }

    [Fact]
    public void start_beat_draws_ramp_target_duration_from_configured_range()
    {
        _handler.MinRampDuration = 42.0;
        _handler.MaxRampDuration = 42.0;
        _handler.StartBeat();
        Assert.Equal(42.0, _handler.RampTargetDuration);
    }

    [Fact]
    public void beat_interval_matches_freq_for_standard_beat()
    {
        _handler.SelectedBeatPatterns = ["Standard Beat"];  // [1]
        _handler.MinBeatFreq = 2.0;
        _handler.MaxBeatFreq = 2.0;
        _handler.StartBeat();
        Assert.Equal((int)(1000 / 2.0), _handler.NoteTimer.IntervalMs);
    }

    [Fact]
    public void beat_interval_normalizes_audible_rate_for_sparse_pattern()
    {
        _handler.SelectedBeatPatterns = ["Slow Pulse"];  // [1, 1, -1, -1, -1, -1, -1, -1]
        _handler.MinBeatFreq = 2.0;
        _handler.MaxBeatFreq = 2.0;
        _handler.StartBeat();

        var audibleCount = 2;
        var invSum = 8;  // all eight steps have abs value 1
        var baseStepSec = audibleCount / (2.0 * invSum);
        var expectedMs = (int)(baseStepSec * 1000 / 1);  // position 0 has value 1
        Assert.Equal(expectedMs, _handler.NoteTimer.IntervalMs);
    }

    // --- custom beat patterns ---

    [Fact]
    public void custom_pattern_merges_into_available_patterns()
    {
        _handler.AddOrUpdateCustomPattern("My Pattern", [1, -1, 2]);
        Assert.Equal([1, -1, 2], _handler.AvailableBeatPatterns["My Pattern"]);
        Assert.True(_handler.AvailableBeatPatterns.ContainsKey("Standard Beat"));  // built-ins untouched
    }

    [Fact]
    public void add_custom_pattern_auto_selects_it()
    {
        _handler.AddOrUpdateCustomPattern("My Pattern", [1, -1, 2]);
        Assert.Contains("My Pattern", _handler.SelectedBeatPatterns);
    }

    [Fact]
    public void add_custom_pattern_persists_to_the_data_store()
    {
        var settings = Settings();
        var store = Store();
        var handler = Handler(settings, store);

        handler.AddOrUpdateCustomPattern("My Pattern", [1, -1, 2]);

        var saved = JsonNode.Parse(File.ReadAllText(store.PathFor("custom_patterns")))!.AsObject();
        Assert.Equal("{\"My Pattern\":[1,-1,2]}", saved.ToJsonString());
        // The *selection* of which rhythms are active stays config, so it stays in the settings.
        Assert.Contains("My Pattern", settings.GetStringList("BeatHandler/selected_beat_patterns")!);
    }

    [Fact]
    public void update_existing_custom_pattern_does_not_duplicate_selection()
    {
        _handler.AddOrUpdateCustomPattern("My Pattern", [1, -1, 2]);
        _handler.AddOrUpdateCustomPattern("My Pattern", [2, -2]);
        Assert.Equal([2, -2], _handler.AvailableBeatPatterns["My Pattern"]);
        Assert.Single(_handler.SelectedBeatPatterns, name => name == "My Pattern");
    }

    [Fact]
    public void custom_patterns_loaded_from_the_data_store()
    {
        var store = Store();
        store.Save("custom_patterns", new JsonObject { ["Loaded"] = new JsonArray(1, -1) });

        var handler = Handler(dataStore: store);

        Assert.Equal([1, -1], handler.AvailableBeatPatterns["Loaded"]);
    }

    [Fact]
    public void custom_patterns_migrate_out_of_settings_on_first_load()
    {
        var registry = new FakeLegacySettings().Set("BeatHandler/custom_patterns", """{"Legacy": [1, -1]}""");
        var store = Store();

        ProfileMigration.Run(store, registry, roamingDir: null);
        var handler = Handler(dataStore: store);

        Assert.Equal([1, -1], handler.AvailableBeatPatterns["Legacy"]);
        Assert.True(File.Exists(store.PathFor("custom_patterns")));
        Assert.False(registry.Contains("BeatHandler/custom_patterns"));
        handler.Stop();
    }

    [Fact]
    public void delete_custom_pattern_removes_it_everywhere()
    {
        _handler.AddOrUpdateCustomPattern("My Pattern", [1, -1, 2]);
        _handler.DeleteCustomPattern("My Pattern");
        Assert.False(_handler.AvailableBeatPatterns.ContainsKey("My Pattern"));
        Assert.DoesNotContain("My Pattern", _handler.SelectedBeatPatterns);
        Assert.False(_handler.CustomBeatPatterns.ContainsKey("My Pattern"));
    }

    private static readonly int[] NoAudibleStep = [-1, -2];
    private static readonly int[] ZeroStep = [0, 1];
    private static readonly int[] OutOfRange = [1, 5];

    public static TheoryData<int[]> InvalidSteps() => new()
    {
        Array.Empty<int>(),
        NoAudibleStep,
        ZeroStep,  // zero not allowed
        OutOfRange,  // magnitude out of 1-4 range
    };

    [Theory]
    [MemberData(nameof(InvalidSteps))]
    public void add_custom_pattern_rejects_invalid_steps(int[] steps)
    {
        Assert.Throws<ArgumentException>(() => _handler.AddOrUpdateCustomPattern("Bad Pattern", steps));
        Assert.False(_handler.AvailableBeatPatterns.ContainsKey("Bad Pattern"));
    }

    [Fact]
    public void add_custom_pattern_rejects_name_colliding_with_builtin() =>
        Assert.Throws<ArgumentException>(() => _handler.AddOrUpdateCustomPattern("Standard Beat", [1, -1]));

    [Fact]
    public void add_custom_pattern_rejects_empty_name() =>
        Assert.Throws<ArgumentException>(() => _handler.AddOrUpdateCustomPattern("", [1, -1]));

    [Fact]
    public void beat_segment_emits_pattern_name()
    {
        _handler.SelectedBeatPatterns = ["Standard Beat"];
        var names = new List<string>();
        _handler.BeatChanged += (_, pattern) => names.Add(pattern);
        _handler.StartBeat();
        Assert.Equal("Standard Beat", names[0]);
    }

    // --- beat lookahead (drives the animated note highway) ---

    // Puts the handler in a deterministic 'mid-session' state for lookahead tests.
    private void Arm(int[] pattern, double freq = 1.0, int position = 0, int remainingMs = 100)
    {
        _handler.CurrentBeatPattern = pattern;
        _handler.CurrentBeatPatternName = "Test";
        _handler.CurFreq = freq;
        _handler.CurrentBeatPosition = position;
        _handler.PatternAudibleCount = pattern.Count(v => v > 0);
        _handler.PatternInvSum = pattern.Sum(v => 1.0 / Math.Abs(v));
        _handler.NoteTimer.Start(remainingMs);
    }

    private List<double> Offsets(double horizon = 2.0) => [.. _handler.UpcomingBeats(horizon).Select(n => n.Offset)];

    // As Arm, plus a running segment that ends in `endsIn` seconds and a plan holding `next`
    // behind it.
    private void ArmBeforeABoundary(Segment next, double endsIn = 0.4, int[]? pattern = null, double freq = 1.0, int remainingMs = 100)
    {
        Arm(pattern ?? [1], freq: freq, remainingMs: remainingMs);
        _handler.CurrentSegment = new Segment("beat", 10.0, freq, "Test", 0);
        _handler.CurrentSegmentEnd = _time.Now + endsIn;
        _handler.Plan.Clear();
        _handler.Plan.Add(next);
    }

    private static void Close(IEnumerable<double> expected, IEnumerable<double> actual)
    {
        var e = expected.ToList();
        var a = actual.ToList();
        Assert.True(e.Count == a.Count && e.Zip(a).All(p => Math.Abs(p.First - p.Second) < 1e-9),
            $"expected [{string.Join(", ", e)}], actual [{string.Join(", ", a)}]");
    }

    [Fact]
    public void upcoming_beats_empty_when_timer_inactive()
    {
        Arm([1]);
        _handler.NoteTimer.Stop();
        Assert.Empty(_handler.UpcomingBeats(2.0));
    }

    [Fact]
    public void upcoming_beats_empty_before_any_pattern_selected() => Assert.Empty(_handler.UpcomingBeats(2.0));

    [Fact]
    public void upcoming_beats_empty_when_freq_is_zero()
    {
        Arm([1], freq: 0);
        Assert.Empty(_handler.UpcomingBeats(2.0));
    }

    [Fact]
    public void upcoming_beats_first_entry_tracks_remaining_time()
    {
        Arm([1], freq: 1.0, remainingMs: 250);
        var upcoming = _handler.UpcomingBeats(2.0);
        Assert.NotEmpty(upcoming);
        Assert.Equal(0.25, upcoming[0].Offset, 9);
    }

    [Fact]
    public void upcoming_beats_spacing_matches_frequency()
    {
        // Standard Beat at 1 Hz: one audible step per second.
        Arm([1], freq: 1.0, remainingMs: 0);
        var times = _handler.UpcomingBeats(3.5).Select(n => n.Offset).ToList();
        Assert.Equal(4, times.Count);  // 0.0, 1.0, 2.0, 3.0
        foreach (var (earlier, later) in times.Zip(times.Skip(1)))
            Assert.Equal(1.0, later - earlier, 9);
    }

    // Weight 2 lasts half as long as weight 1 - the inverse-duration encoding.
    [Fact]
    public void upcoming_beats_heavier_weight_is_a_shorter_step()
    {
        Arm([1, 2], freq: 1.0, position: 0, remainingMs: 0);
        var times = _handler.UpcomingBeats(3.0).Select(n => n.Offset).ToList();
        var firstGap = times[1] - times[0];  // follows the weight-1 step
        var secondGap = times[2] - times[1];  // follows the weight-2 step
        Assert.Equal(firstGap / 2, secondGap, 9);
    }

    // Audibility of the note landing at t is pattern[CurrentBeatPosition], and the interval after
    // it comes from that same index (the existing off-by-one).
    [Fact]
    public void upcoming_beats_audibility_follows_current_position()
    {
        Arm([1, -1, 1], position: 1, remainingMs: 0);
        var audible = _handler.UpcomingBeats(5.0).Select(n => n.Audible).ToList();
        Assert.False(audible[0]);  // pattern[1] == -1
        Assert.True(audible[1]);   // pattern[2] == 1
        Assert.True(audible[2]);   // wraps to pattern[0] == 1
    }

    [Fact]
    public void upcoming_beats_reports_step_weight()
    {
        Arm([1, -3], position: 0, remainingMs: 0);
        var weights = _handler.UpcomingBeats(5.0).Select(n => n.Weight).ToList();
        Assert.Equal(1, weights[0]);
        Assert.Equal(3, weights[1]);
    }

    [Fact]
    public void upcoming_beats_respects_horizon()
    {
        Arm([1], freq: 1.0, remainingMs: 0);
        Assert.Equal(2, _handler.UpcomingBeats(1.5).Count);  // 0.0 and 1.0 only
        Assert.Single(_handler.UpcomingBeats(0.5));
    }

    [Fact]
    public void upcoming_beats_does_not_mutate_handler_state()
    {
        Arm([1, 2, -1], position: 1, remainingMs: 50);
        var before = _handler.CurrentBeatPosition;

        _handler.UpcomingBeats(10.0);

        Assert.Equal(before, _handler.CurrentBeatPosition);
    }

    // A very high frequency over a long horizon must not produce an unbounded list.
    [Fact]
    public void upcoming_beats_is_capped_for_pathological_input()
    {
        Arm([4], freq: 200.0, remainingMs: 0);
        Assert.True(_handler.UpcomingBeats(1000.0).Count <= BeatHandler.MaxLookaheadNotes);
    }

    // --- lookahead across a segment boundary ---

    // The note that ends a segment is the last one at the old spacing; the gap after it already
    // belongs to the next segment.
    [Fact]
    public void upcoming_beats_spaces_the_next_segment_by_its_own_frequency()
    {
        ArmBeforeABoundary(new Segment("beat", 10.0, 4.0, "Standard Beat", 1), endsIn: 0.4);

        // 0.1 and 1.1 at 1 Hz (1.1 is the note that trips the boundary), then 0.25s apart.
        Close([0.1, 1.1, 1.35, 1.6, 1.85], Offsets());
    }

    // The note landing on the boundary is the old rhythm's. Reading it off the next segment
    // instead drew a marker on the hit zone for a step that is actually silent.
    [Fact]
    public void upcoming_beats_keeps_the_boundary_note_on_the_old_pattern()
    {
        // [1, -1] at 1 Hz: audible at 0.1, silent at 0.6 - and 0.6 is the boundary note.
        ArmBeforeABoundary(new Segment("beat", 10.0, 1.0, "Standard Beat", 1), endsIn: 0.4, pattern: [1, -1], freq: 1.0);

        var upcoming = _handler.UpcomingBeats(2.0);

        Close([0.1, 0.6, 1.6], upcoming.Select(n => n.Offset));
        Assert.Equal([true, false, true], upcoming.Select(n => n.Audible));
    }

    [Fact]
    public void upcoming_beats_stops_at_a_planned_pause()
    {
        ArmBeforeABoundary(new Segment("pause", 5.0, null, null, 1), endsIn: 0.4);
        Close([0.1, 1.1], Offsets());
    }

    [Fact]
    public void upcoming_beats_stops_where_the_plan_runs_out()
    {
        Arm([1], freq: 1.0);
        _handler.CurrentSegment = new Segment("beat", 10.0, 1.0, "Test", 0);
        _handler.CurrentSegmentEnd = _time.Now + 0.4;
        _handler.Plan.Clear();
        Close([0.1, 1.1], Offsets());
    }

    [Fact]
    public void upcoming_beats_skips_a_segment_whose_pattern_was_deleted()
    {
        ArmBeforeABoundary(new Segment("beat", 10.0, 4.0, "Ghost Pattern", 1), endsIn: 0.4);
        Close([0.1, 1.1], Offsets());
    }

    // --- crash paths: an unusable selection, inverted bounds, a corrupt pattern file ---

    [Fact]
    public void beat_segment_falls_back_when_nothing_is_selected()
    {
        _handler.SelectedBeatPatterns = [];
        _handler.StartBeat();
        Assert.True(BeatHandler.BeatPatternsMap.ContainsKey(_handler.CurrentBeatPatternName!));
    }

    [Fact]
    public void beat_segment_skips_selected_patterns_that_no_longer_exist()
    {
        _handler.SelectedBeatPatterns = ["Ghost Pattern", "Standard Beat"];
        _handler.StartBeat();
        var planned = new[] { _handler.CurrentSegment! }.Concat(_handler.PlannedSegments);
        Assert.Equal(["Standard Beat"], planned.Where(s => s.Kind != "pause").Select(s => s.PatternName).Distinct());
    }

    [Fact]
    public void beat_segment_falls_back_when_every_selected_pattern_is_gone()
    {
        _handler.SelectedBeatPatterns = ["Ghost Pattern"];
        _handler.StartBeat();
        Assert.True(BeatHandler.BeatPatternsMap.ContainsKey(_handler.CurrentBeatPatternName!));
    }

    // A one-element list can come over from QSettings as a bare string.
    [Fact]
    public void beat_segment_treats_a_single_stored_string_as_one_name()
    {
        var settings = Settings();
        settings.SetValue("BeatHandler/selected_beat_patterns", "Standard Beat");
        var handler = Handler(settings);
        handler.StartBeat();
        Assert.Equal("Standard Beat", handler.CurrentBeatPatternName);
        handler.Stop();
    }

    [Fact]
    public void start_beat_survives_an_empty_selection()
    {
        _handler.SelectedBeatPatterns = [];
        _handler.StartBeat();
        Assert.True(_handler.NoteTimer.Active);
    }

    [Fact]
    public void start_pause_survives_inverted_pause_bounds()
    {
        _handler.MinPauseDur = 30;
        _handler.MaxPauseDur = 5;
        _handler.StartPause();
        Assert.InRange(_handler.CurPauseDur!.Value, 5, 30);
    }

    public static TheoryData<string> BadSteps() => new()
    {
        "[1, 0, 2]",  // a zero step divides by zero
        "[1, \"x\"]",  // non-numeric
        "[1, null]",
        "[]",  // an empty pattern indexes out of range
        "[-1, -2]",  // no audible step at all
        "[1, 9]",  // weight out of the 1..4 range the editor enforces
        "5",  // not a list
        "\"nope\"",
        "{\"a\": 1}",
        "[1, true]",  // a bool is not a weight
    };

    [Theory]
    [MemberData(nameof(BadSteps))]
    public void invalid_custom_patterns_are_dropped_on_load(string badSteps)
    {
        var store = Store();
        store.Save("custom_patterns", new JsonObject { ["Bad"] = JsonNode.Parse(badSteps), ["Good"] = new JsonArray(1, -1) });

        var handler = Handler(dataStore: store);

        Assert.False(handler.AvailableBeatPatterns.ContainsKey("Bad"));
        Assert.Equal([1, -1], handler.AvailableBeatPatterns["Good"]);
    }

    [Fact]
    public void non_dict_custom_patterns_file_is_ignored()
    {
        var store = Store();
        store.Save("custom_patterns", new JsonArray("not", "a", "dict"));

        var handler = Handler(dataStore: store);

        Assert.Equal(BeatHandler.BeatPatternsMap.Keys, handler.AvailableBeatPatterns.Keys);
    }

    [Fact]
    public void start_beat_survives_a_corrupt_custom_patterns_file()
    {
        var store = Store();
        store.Save("custom_patterns", new JsonObject { ["Broken"] = new JsonArray(1, 0) });
        var handler = Handler(dataStore: store);
        handler.SelectedBeatPatterns = ["Broken"];

        handler.StartBeat();

        Assert.True(BeatHandler.BeatPatternsMap.ContainsKey(handler.CurrentBeatPatternName!));
        handler.Stop();
    }

    [Fact]
    public void beat_loudness_is_restored_from_settings()
    {
        var settings = Settings();
        settings.SetValue("BeatHandler/beat_loudness", 0.3);

        Assert.Equal(0.3, Handler(settings).BeatLoudness);
    }

    [Fact]
    public void clear_custom_patterns_removes_them_but_keeps_the_builtins()
    {
        var store = Store();
        var handler = Handler(dataStore: store);
        handler.AddOrUpdateCustomPattern("Mine", [1, -1]);

        handler.ClearCustomPatterns();

        Assert.Empty(handler.CustomBeatPatterns);
        Assert.False(handler.AvailableBeatPatterns.ContainsKey("Mine"));
        Assert.DoesNotContain("Mine", handler.SelectedBeatPatterns);
        Assert.True(handler.AvailableBeatPatterns.ContainsKey("Standard Beat"));
        Assert.False(File.Exists(store.PathFor("custom_patterns")));
    }

    // SettingsDialog once derived the settings key from the class name, so renaming the class
    // silently orphaned every user's saved values.
    [Fact]
    public void settings_group_is_an_explicit_constant() => Assert.Equal("BeatHandler", BeatHandler.SettingsGroup);

    [Fact]
    public void is_paused_reports_the_pause_phase()
    {
        Assert.False(_handler.IsPaused());

        _handler.StartPause();
        Assert.True(_handler.IsPaused());

        _handler.PauseTimer.Stop();
        Assert.False(_handler.IsPaused());
    }

    // A replayed pause carries the length it was *measured* at, and truncating 1.9987 to 1 made
    // every replayed pause up to a second shorter than the one it was replaying.
    [Fact]
    public void a_pause_lasting_a_hair_under_a_whole_second_is_not_cut_short()
    {
        _handler.CurrentSegment = new Segment("pause", 1.9987, null, null, 0);

        _handler.StartPause();

        Assert.Equal(2, _handler.CurPauseDur);
    }

    // Truncating a sub-second measurement gave 0, and the pause would end before it began.
    [Fact]
    public void a_pause_never_counts_down_from_zero()
    {
        _handler.CurrentSegment = new Segment("pause", 0.4, null, null, 0);

        _handler.StartPause();

        Assert.Equal(1, _handler.CurPauseDur);
    }

    // The one hook device output hangs off. It says nothing about what the note is -
    // UpcomingBeats answers that - only that the last prediction is now stale.
    [Fact]
    public void note_scheduled_event_fires_for_every_note_the_rhythm_commits_to()
    {
        _handler.RampingActive = false;
        _handler.MinBeatFreq = _handler.MaxBeatFreq = 4.0;
        _handler.MinBeatDur = _handler.MaxBeatDur = 30.0;
        _handler.PauseChance = 0;
        _handler.SelectedBeatPatterns = ["Standard Beat"];
        var scheduled = 0;
        _handler.NoteScheduled += () => scheduled++;
        _handler.StartBeat();
        Assert.Equal(1, scheduled);
        _handler.Beat();
        _handler.Beat();
        Assert.Equal(3, scheduled);
    }

    // --- driven by the clock instead of a timer chain ---

    // What the Qt timers did on their own, the host now does by advancing: every note that has
    // come due fires, in order, at the moment it was due.
    [Fact]
    public void advancing_the_clock_plays_the_notes_that_came_due()
    {
        _handler.RampingActive = false;
        _handler.MinBeatFreq = _handler.MaxBeatFreq = 2.0;
        _handler.MinBeatDur = _handler.MaxBeatDur = 30.0;
        _handler.PauseChance = 0;
        _handler.SelectedBeatPatterns = ["Standard Beat"];
        var hits = 0;
        _handler.BeatHit += () => hits++;
        _time.Now = 1000.0;
        _handler.StartBeat();

        _time.Now = 1002.2;
        _handler.Advance();

        Assert.Equal(4, hits);  // at 1000.5, 1001.0, 1001.5 and 1002.0
        Assert.Equal(0.3, _handler.UpcomingBeats(1.0)[0].Offset, 9);
    }
}
