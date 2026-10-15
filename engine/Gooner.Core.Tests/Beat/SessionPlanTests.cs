using System.Text.Json.Nodes;
using Gooner.Core.Beat;
using Gooner.Core.Sessions;
using Gooner.Core.Tests.Support;

namespace Gooner.Core.Tests.Beat;

// Port of tests/test_session_plan.py. The planner decides what the next segments are before they
// happen. Like the Python tests these never script the dice - they pin the settings ranges to a
// single value so a draw has only one outcome.
public sealed class SessionPlanTests : IDisposable
{
    private readonly FakeTime _time = new() { Now = 1000.0 };
    private readonly BeatHandler _handler;

    public SessionPlanTests() => _handler = new BeatHandler(random: new ScriptedRandom(), clock: _time);

    public void Dispose() => _handler.Stop();

    // Collapses every range to a point, so the draws have only one outcome.
    private void Pin(double beatDur = 20.0, double freq = 2.0, double pauseChance = 0.0, int pauseDur = 5, bool ramping = false)
    {
        _handler.MinBeatDur = _handler.MaxBeatDur = beatDur;
        _handler.MinBeatFreq = _handler.MaxBeatFreq = freq;
        _handler.MinPauseDur = _handler.MaxPauseDur = pauseDur;
        _handler.PauseChance = pauseChance;
        _handler.RampingActive = ramping;
        _handler.SelectedBeatPatterns = ["Standard Beat"];
    }

    private List<Segment> Planned() => [_handler.CurrentSegment!, .. _handler.PlannedSegments];

    // --- the buffer ---

    [Fact]
    public void start_beat_fills_the_plan_buffer()
    {
        Pin();
        _handler.StartBeat();
        Assert.Equal(BeatHandler.PlanBufferSegments, _handler.PlannedSegments.Count);
    }

    [Fact]
    public void start_beat_puts_the_first_segment_on_the_air()
    {
        Pin(freq: 3.0);
        _handler.StartBeat();
        Assert.Equal("beat", _handler.CurrentSegment!.Kind);
        Assert.Equal(3.0, _handler.CurFreq);
        Assert.Equal("Standard Beat", _handler.CurrentBeatPatternName);
    }

    [Fact]
    public void plan_refills_as_segments_are_consumed()
    {
        Pin();
        _handler.StartBeat();
        for (var i = 0; i < 4; i++)
            _handler.BeginNextSegment();
        Assert.Equal(BeatHandler.PlanBufferSegments, _handler.PlannedSegments.Count);
    }

    [Fact]
    public void segment_indices_are_monotonic()
    {
        Pin();
        _handler.StartBeat();
        var seen = new List<int> { _handler.CurrentSegment!.Index };
        for (var i = 0; i < 6; i++)
        {
            _handler.BeginNextSegment();
            seen.Add(_handler.CurrentSegment!.Index);
        }
        Assert.Equal(Enumerable.Range(0, seen.Count), seen);
    }

    [Fact]
    public void replan_does_not_reuse_indices()
    {
        Pin();
        _handler.StartBeat();
        var before = _handler.PlannedSegments.Max(s => s.Index);
        _handler.ReplanFromNextSegment();
        Assert.True(_handler.PlannedSegments.Min(s => s.Index) > before);
    }

    [Fact]
    public void plan_extended_event_carries_the_new_segments()
    {
        Pin();
        var batches = new List<IReadOnlyList<Segment>>();
        _handler.PlanExtended += batches.Add;
        _handler.StartBeat();
        Assert.Equal(BeatHandler.PlanBufferSegments, batches[0].Count);
    }

    // The whole Segment, not just its index - a consumer recording what actually played would
    // otherwise have to reach back into the handler for the rest.
    [Fact]
    public void segment_started_event_carries_the_segment()
    {
        Pin();
        _handler.StartBeat();
        var started = new List<Segment>();
        _handler.SegmentStarted += started.Add;
        _handler.BeginNextSegment();
        Assert.Equal([_handler.CurrentSegment!], started);
    }

    [Fact]
    public void session_planned_event_reports_when_the_session_started()
    {
        Pin();
        var planned = new List<(double Start, SessionScript? Script)>();
        _handler.SessionPlanned += (start, script) => planned.Add((start, script));
        _handler.StartBeat();
        Assert.Equal([(1000.0, (SessionScript?)null)], planned);
    }

    // Otherwise the run-in to the climax could already be planned by the time anyone gets to say
    // where the climax is.
    [Fact]
    public void session_planned_event_fires_before_the_plan_is_built()
    {
        Pin();
        var sizes = new List<int>();
        _handler.SessionPlanned += (_, _) => sizes.Add(_handler.PlannedSegments.Count);
        _handler.StartBeat();
        Assert.Equal([0], sizes);
    }

    // --- segment contents ---

    [Fact]
    public void beat_segment_duration_has_no_tail_beyond_the_configured_range()
    {
        _handler.MinBeatDur = 10.0;
        _handler.MaxBeatDur = 12.0;
        _handler.PauseChance = 0.0;
        _handler.SelectedBeatPatterns = ["Standard Beat"];
        _handler.StartBeat();
        var durations = new List<double> { _handler.CurrentSegment!.DurationSec };
        for (var i = 0; i < 40; i++)
        {
            _handler.BeginNextSegment();
            durations.Add(_handler.CurrentSegment!.DurationSec);
        }
        Assert.All(durations, d => Assert.InRange(d, 10.0, 12.0));
    }

    [Fact]
    public void pause_segments_use_the_pause_range()
    {
        Pin(pauseChance: 1.0, pauseDur: 7);
        _handler.StartBeat();
        var pause = _handler.PlannedSegments.First(s => s.Kind == "pause");
        Assert.Equal(7, pause.DurationSec);
    }

    // A session that begins by telling the user to take their hands off would be a new and
    // unwelcome behaviour.
    [Fact]
    public void a_session_never_opens_on_a_pause()
    {
        Pin(pauseChance: 1.0);
        _handler.StartBeat();
        Assert.Equal("beat", _handler.CurrentSegment!.Kind);
    }

    [Fact]
    public void a_pause_is_never_followed_by_another_pause()
    {
        Pin(pauseChance: 1.0);
        _handler.StartBeat();
        var kinds = Planned().Select(s => s.Kind).ToList();
        Assert.Contains("pause", kinds);
        Assert.DoesNotContain(kinds.Zip(kinds.Skip(1)), p => p.First == "pause" && p.Second == "pause");
    }

    // The whole point of planning ahead: the ramp becomes a curve the plan walks along, not a
    // corridor sampled at whatever moment the dice happened to be thrown.
    [Fact]
    public void ramping_is_evaluated_at_each_segments_planned_start()
    {
        _handler.MinBeatFreq = 1.0;
        _handler.MaxBeatFreq = 5.0;
        _handler.MinBeatDur = _handler.MaxBeatDur = 20.0;
        _handler.MinRampDuration = _handler.MaxRampDuration = 400.0;
        _handler.RampWindowWidth = 0.02;  // a near-point window, so the draw is effectively fixed
        _handler.RampingActive = true;
        _handler.PauseChance = 0.0;
        _handler.SelectedBeatPatterns = ["Standard Beat"];

        _handler.StartBeat();
        var freqs = Planned().Select(s => s.Freq!.Value).ToList();
        Assert.Equal(freqs.Order(), freqs);
        Assert.True(freqs[^1] > freqs[0]);
    }

    // --- the finale ---

    // The planned segment spanning `moment`, or null if the plan does not reach it.
    private Segment? SegmentCovering(double moment, double sessionStart = 1000.0)
    {
        var start = sessionStart;
        foreach (var segment in Planned())
        {
            var end = start + segment.DurationSec;
            if (start <= moment && moment < end) return segment;
            start = end;
        }
        return null;
    }

    [Fact]
    public void no_finale_is_planned_when_none_is_requested()
    {
        Pin();
        _handler.StartBeat();
        Assert.DoesNotContain(_handler.PlannedSegments, s => s.Kind == "finale");
    }

    [Fact]
    public void the_finale_covers_the_moment_the_climax_is_announced()
    {
        Pin(beatDur: 20.0);
        _handler.StartBeat();
        _handler.SetFinaleAt(1000.0 + 95.0);

        var starts = 1000.0;
        (double Start, double End)? finale = null;
        foreach (var segment in Planned())
        {
            if (segment.Kind == "finale")
            {
                finale = (starts, starts + segment.DurationSec);
                break;
            }
            starts += segment.DurationSec;
        }
        Assert.True(finale is not null, "the planner never marked a run-in to the climax");
        Assert.True(finale.Value.Start <= 1095.0 && 1095.0 < finale.Value.End);
    }

    [Fact]
    public void the_finale_runs_at_the_top_of_its_frequency_window()
    {
        _handler.MinBeatFreq = 1.0;
        _handler.MaxBeatFreq = 5.0;
        _handler.MinBeatDur = _handler.MaxBeatDur = 20.0;
        _handler.RampingActive = false;
        _handler.PauseChance = 0.0;
        _handler.SelectedBeatPatterns = ["Standard Beat"];
        _handler.StartBeat();
        _handler.SetFinaleAt(1000.0 + 95.0);

        Assert.Equal(5.0, _handler.PlannedSegments.First(s => s.Kind == "finale").Freq);
    }

    // The climax can be set to land while the ramp is still climbing. "Fast" then means the
    // fastest the ramp currently allows, not the absolute maximum.
    [Fact]
    public void a_finale_inside_the_ramp_runs_at_the_top_of_the_window_it_is_in()
    {
        _handler.MinBeatFreq = 1.0;
        _handler.MaxBeatFreq = 5.0;
        _handler.MinBeatDur = _handler.MaxBeatDur = 20.0;
        _handler.MinRampDuration = _handler.MaxRampDuration = 1000.0;  // barely started
        _handler.RampWindowWidth = 0.4;
        _handler.RampingActive = true;
        _handler.PauseChance = 0.0;
        _handler.SelectedBeatPatterns = ["Standard Beat"];
        _handler.StartBeat();
        _handler.SetFinaleAt(1095.0);

        var finale = _handler.PlannedSegments.First(s => s.Kind == "finale");
        Assert.True(finale.Freq < _handler.MaxBeatFreq);
        var (_, windowMax) = _handler.CurrentFreqRange(atTime: 1080.0);
        Assert.Equal(windowMax, finale.Freq!.Value, 0.2);
    }

    // Even with a pause due at every boundary, the moment the climax is announced is covered by a
    // rhythm - being told to cum with your hands off would be nonsense.
    [Fact]
    public void the_finale_is_never_a_pause()
    {
        Pin(beatDur: 20.0, pauseChance: 1.0);
        _handler.StartBeat();
        _handler.SetFinaleAt(1060.0);

        var covering = SegmentCovering(1060.0);
        Assert.NotNull(covering);
        Assert.Equal("finale", covering.Kind);
    }

    // 4 x 20s lands on 80s, leaving 5s before an 85s climax - too short for a segment of its own,
    // so the run-in is extended over it instead of a runt being planned.
    [Fact]
    public void the_finale_swallows_a_leftover_too_short_to_stand_alone()
    {
        Pin(beatDur: 20.0);
        _handler.StartBeat();
        _handler.SetFinaleAt(1000.0 + 85.0);

        var planned = Planned();
        Assert.All(planned, s => Assert.True(s.DurationSec >= _handler.MinBeatDur));
        Assert.True(planned.First(s => s.Kind == "finale").DurationSec > 20.0);
    }

    [Fact]
    public void only_one_finale_is_planned()
    {
        Pin(beatDur: 20.0);
        _handler.StartBeat();
        _handler.SetFinaleAt(1000.0 + 45.0);
        Assert.Single(Planned(), s => s.Kind == "finale");
    }

    // Segments start at the first beat tick past their planned end, so they run late.
    // Re-deriving the segment to account for that used to re-draw its pattern and frequency, and
    // the beat track had already painted several of its notes.
    [Fact]
    public void a_segment_starts_exactly_as_it_was_planned()
    {
        Pin(beatDur: 4.0);
        _handler.StartBeat();
        _handler.SetFinaleAt(1015.0);
        var nextPlanned = _handler.PlannedSegments[0];

        _time.Now = 1010.0;  // six seconds late
        _handler.BeginNextSegment();

        Assert.Equal(nextPlanned, _handler.CurrentSegment);
    }

    [Fact]
    public void the_finale_segment_also_starts_exactly_as_it_was_planned()
    {
        Pin(beatDur: 4.0);
        _handler.StartBeat();
        _handler.SetFinaleAt(1015.0);
        var plannedFinale = _handler.PlannedSegments.First(s => s.Kind == "finale");

        _time.Now = 1010.0;
        _handler.BeginNextSegment();
        _time.Now = 1016.0;
        _handler.BeginNextSegment();

        Assert.Equal(plannedFinale, _handler.CurrentSegment);
        Assert.Equal(plannedFinale.PatternName, _handler.CurrentBeatPatternName);
    }

    [Fact]
    public void only_one_finale_survives_a_late_start()
    {
        Pin(beatDur: 4.0);
        _handler.StartBeat();
        _handler.SetFinaleAt(1015.0);

        _time.Now = 1010.0;
        _handler.BeginNextSegment();

        Assert.Single(Planned(), s => s.Kind == "finale");
    }

    [Fact]
    public void planning_continues_normally_past_the_finale()
    {
        Pin(beatDur: 20.0);
        _handler.StartBeat();
        _handler.SetFinaleAt(1000.0 + 45.0);
        var planned = Planned();
        var after = planned.Skip(planned.FindIndex(s => s.Kind == "finale") + 1).ToList();
        Assert.NotEmpty(after);
        Assert.All(after, s => Assert.Equal("beat", s.Kind));
    }

    // --- replanning ---

    [Fact]
    public void replan_leaves_the_running_segment_alone()
    {
        Pin(beatDur: 20.0);
        _handler.StartBeat();
        var running = _handler.CurrentSegment;
        _handler.MinBeatDur = _handler.MaxBeatDur = 40.0;
        _handler.ReplanFromNextSegment();
        Assert.Same(running, _handler.CurrentSegment);
        Assert.All(_handler.PlannedSegments, s => Assert.Equal(40.0, s.DurationSec));
    }

    [Fact]
    public void replan_keeps_the_finale_where_it_was()
    {
        Pin(beatDur: 20.0);
        _handler.StartBeat();
        _handler.SetFinaleAt(1000.0 + 95.0);
        _handler.ReplanFromNextSegment();
        Assert.Equal(1095.0, _handler.FinaleAt);
        Assert.Contains(_handler.PlannedSegments, s => s.Kind == "finale");
    }

    [Fact]
    public void replan_before_a_session_starts_does_nothing()
    {
        _handler.ReplanFromNextSegment();
        Assert.Empty(_handler.PlannedSegments);
    }

    [Fact]
    public void stop_clears_the_plan()
    {
        Pin();
        _handler.StartBeat();
        _handler.Stop();
        Assert.Empty(_handler.PlannedSegments);
        Assert.Null(_handler.CurrentSegment);
    }

    // --- when the ramp tops out ---

    [Fact]
    public void ramp_complete_at_is_none_before_the_session_starts() => Assert.Null(_handler.RampCompleteAt);

    [Fact]
    public void ramp_complete_at_reports_the_end_of_the_ramp()
    {
        Pin(ramping: true);
        _handler.MinRampDuration = _handler.MaxRampDuration = 600.0;
        _handler.StartBeat();
        Assert.Equal(1600.0, _handler.RampCompleteAt);
    }

    // There is no ramp to wait for - which is what keeps the Ramp duration sliders from
    // influencing anything while their own checkbox is unticked.
    [Fact]
    public void ramp_complete_at_is_none_while_ramping_is_switched_off()
    {
        Pin(ramping: false);
        _handler.MinRampDuration = _handler.MaxRampDuration = 600.0;
        _handler.StartBeat();
        Assert.Null(_handler.RampCompleteAt);
    }

    // --- holding the last segment once the climax has landed ---

    // Once she has told you to cum, the rhythm she said it over is the one that stays.
    [Fact]
    public void holding_the_final_segment_stops_segment_changes()
    {
        Pin(beatDur: 20.0);
        _handler.StartBeat();
        var running = _handler.CurrentSegment;

        _handler.HoldFinalSegment();
        _time.Now = 1000.0 + 600.0;  // long past when the segment would have ended
        _handler.ResetBeatTimer();

        Assert.Same(running, _handler.CurrentSegment);
        Assert.True(_handler.NoteTimer.Active);
    }

    // Nothing further is decided, so nothing further is announced - which is what stops fake
    // climaxes being rolled onto segments that will never start.
    [Fact]
    public void holding_the_final_segment_empties_the_plan()
    {
        Pin();
        _handler.StartBeat();
        Assert.NotEmpty(_handler.PlannedSegments);

        _handler.HoldFinalSegment();

        Assert.Empty(_handler.PlannedSegments);
    }

    [Fact]
    public void holding_the_final_segment_plans_no_further_pause()
    {
        Pin(beatDur: 20.0, pauseChance: 1.0);
        _handler.StartBeat();

        _handler.HoldFinalSegment();
        _time.Now = 1000.0 + 600.0;
        _handler.ResetBeatTimer();

        Assert.False(_handler.IsPaused());
    }

    [Fact]
    public void a_new_session_lifts_the_hold()
    {
        Pin();
        _handler.StartBeat();
        _handler.HoldFinalSegment();

        _handler.StartBeat();

        Assert.NotEmpty(_handler.PlannedSegments);
    }

    // A held segment runs past its planned end, which made the lookahead treat every note as a
    // boundary it could not cross - the track emptied out right when the climax was on screen.
    [Fact]
    public void the_track_stays_full_while_the_final_segment_is_held()
    {
        Pin(beatDur: 20.0, freq: 4.0);
        _handler.StartBeat();
        var before = _handler.UpcomingBeats(2.5).Count;
        Assert.True(before > 1);

        _handler.HoldFinalSegment();
        // Long past where the segment would have ended. Python moves only the handler's idea of
        // the time and leaves Qt's timer where it was; here they are one clock, so the notes in
        // between are played, as they would be.
        _time.Now = 1000.0 + 600.0;
        _handler.Advance();

        Assert.Equal(before, _handler.UpcomingBeats(2.5).Count);
    }

    // --- replaying a saved session ---

    private static SessionScript Script(JsonArray segments, JsonObject? customPatterns = null) =>
        new(new JsonObject
        {
            ["duration_sec"] = segments.Sum(s => (double)s!["duration_sec"]!),
            ["segments"] = segments,
            ["custom_patterns"] = customPatterns ?? [],
            ["climax"] = null,
            ["fake_climaxes"] = new JsonArray(),
            ["media"] = new JsonArray(),
        });

    private static JsonObject Seg(string kind, string? pattern, double? freq, double duration) => new()
    {
        ["kind"] = kind,
        ["pattern"] = pattern,
        ["freq"] = freq,
        ["duration_sec"] = duration,
    };

    // The whole point of the rebuild: the planner reads a plan instead of drawing one.
    [Fact]
    public void a_scripted_session_replays_the_recorded_segments()
    {
        Pin();
        var script = Script([
            Seg("beat", "Quick Swing", 2.3, 30.0),
            Seg("pause", null, null, 12.0),
            Seg("beat", "Slow Pulse", 4.1, 25.0),
        ]);

        _handler.StartBeat(script);

        Assert.Equal(
            [("beat", "Quick Swing", 2.3, 30.0), ("pause", null, null, 12.0), ("beat", "Slow Pulse", 4.1, 25.0)],
            Planned().Take(3).Select(s => (s.Kind, s.PatternName, s.Freq, s.DurationSec)));
    }

    // A replay is the recorded session, not the recorded session filtered through whatever the
    // sliders happen to say today.
    [Fact]
    public void a_scripted_session_ignores_the_configured_ranges()
    {
        Pin(beatDur: 99.0, freq: 1.0);
        _handler.StartBeat(Script([Seg("beat", "Standard Beat", 3.3, 7.0)]));

        Assert.Equal(7.0, _handler.CurrentSegment!.DurationSec);
        Assert.Equal(3.3, _handler.CurFreq);
    }

    // A replay that outlives its recording keeps playing rather than stopping dead.
    [Fact]
    public void the_planner_takes_over_once_the_script_is_spent()
    {
        Pin(beatDur: 20.0);
        _handler.StartBeat(Script([Seg("beat", "Standard Beat", 2.0, 5.0)]));

        var drawn = _handler.PlannedSegments;
        Assert.NotEmpty(drawn);
        Assert.All(drawn, s => Assert.Equal(20.0, s.DurationSec));
    }

    [Fact]
    public void scripted_segments_keep_the_handlers_own_indices()
    {
        Pin();
        _handler.StartBeat(Script([Seg("beat", "Standard Beat", 2.0, 10.0), Seg("beat", "Standard Beat", 2.0, 10.0)]));

        var planned = Planned();
        Assert.Equal(Enumerable.Range(0, planned.Count), planned.Select(s => s.Index));
    }

    // Otherwise the replay reaches a rhythm the machine has never heard of and falls back to
    // something else entirely.
    [Fact]
    public void a_scripted_custom_pattern_is_registered_before_it_plays()
    {
        Pin();
        _handler.StartBeat(Script([Seg("beat", "Borrowed", 2.0, 10.0)], new JsonObject { ["Borrowed"] = new JsonArray(1, -1, 2) }));

        Assert.Equal("Borrowed", _handler.CurrentBeatPatternName);
        Assert.Equal([1, -1, 2], _handler.CurrentBeatPattern!);
    }

    // A replay borrows someone else's rhythm for the session; it does not adopt it.
    [Fact]
    public void a_scripted_custom_pattern_is_not_saved_to_the_users_library()
    {
        Pin();
        _handler.StartBeat(Script([Seg("beat", "Borrowed", 2.0, 10.0)], new JsonObject { ["Borrowed"] = new JsonArray(1, -1, 2) }));

        Assert.False(_handler.CustomBeatPatterns.ContainsKey("Borrowed"));
    }

    [Fact]
    public void starting_a_normal_session_afterwards_drops_the_script()
    {
        Pin(beatDur: 20.0);
        _handler.StartBeat(Script([Seg("beat", "Standard Beat", 3.3, 7.0)]));

        _handler.StartBeat();

        Assert.Equal(20.0, _handler.CurrentSegment!.DurationSec);
    }

    // The climax learns a session is a replay from this event and nowhere else.
    [Fact]
    public void session_planned_event_carries_the_script_of_a_replay()
    {
        Pin();
        var script = Script([Seg("beat", "Standard Beat", 2.0, 10.0)]);
        SessionScript? carried = null;
        _handler.SessionPlanned += (_, s) => carried = s;
        _handler.StartBeat(script);
        Assert.Same(script, carried);
    }

    // --- relief on demand ---

    [Fact]
    public void an_edge_cuts_the_running_segment_short_for_a_pause()
    {
        Pin(beatDur: 60.0);
        _handler.EdgePauseDur = 12;
        _handler.StartBeat();
        Assert.Equal("beat", _handler.CurrentSegment!.Kind);

        var given = _handler.EdgeRelief();

        Assert.Equal(12.0, given);
        Assert.Equal("pause", _handler.CurrentSegment!.Kind);
        Assert.True(_handler.IsPaused());
    }

    // A gap on its own is not relief - what follows has to be gentler than what drove the user to
    // press the button.
    [Fact]
    public void the_rhythm_behind_an_edge_comes_back_at_the_bottom_of_the_window()
    {
        Pin();
        _handler.MinBeatFreq = 1.0;
        _handler.MaxBeatFreq = 5.0;
        _handler.StartBeat();

        _handler.EdgeRelief();

        Assert.Equal(1.0, _handler.PlannedSegments[0].Freq!.Value, 9);
    }

    [Fact]
    public void only_the_first_segment_after_an_edge_is_slowed()
    {
        Pin();
        _handler.MinBeatFreq = _handler.MaxBeatFreq = 1.0;
        _handler.StartBeat();
        _handler.EdgeRelief();
        _handler.MinBeatFreq = _handler.MaxBeatFreq = 4.0;

        // Everything queued behind the relief segment is drawn normally again.
        _handler.Plan.Clear();
        _handler.ExtendPlan();

        Assert.Equal(4.0, _handler.PlannedSegments[0].Freq!.Value, 9);
    }

    // Nothing to be relieved of - and it would hand out a free second pause.
    [Fact]
    public void an_edge_during_a_pause_does_nothing()
    {
        Pin(pauseChance: 1.0, pauseDur: 10);
        _handler.StartBeat();
        _handler.BeginNextSegment();
        Assert.True(_handler.IsPaused());

        Assert.Equal(0.0, _handler.EdgeRelief());
    }

    [Fact]
    public void an_edge_after_the_climax_does_nothing()
    {
        Pin();
        _handler.StartBeat();
        _handler.HoldFinalSegment();

        Assert.Equal(0.0, _handler.EdgeRelief());
    }

    // The pause is a real segment - the recorder and the explorer both index by it.
    [Fact]
    public void an_edge_keeps_the_segment_counter_climbing()
    {
        Pin();
        _handler.StartBeat();
        var before = _handler.CurrentSegment!.Index;

        _handler.EdgeRelief();

        Assert.True(_handler.CurrentSegment!.Index > before);
    }

    [Fact]
    public void an_edge_announces_its_pause_like_any_other_segment()
    {
        Pin();
        _handler.StartBeat();
        var started = new List<Segment>();
        _handler.SegmentStarted += started.Add;

        _handler.EdgeRelief();

        Assert.Equal("pause", started[0].Kind);
    }

    // The run-in is rebuilt from the replan - a pause pressed just before the climax must not
    // swallow it.
    [Fact]
    public void an_edge_still_leaves_a_fast_run_in_to_the_climax()
    {
        Pin(beatDur: 20.0);
        _handler.MinBeatFreq = 1.0;
        _handler.MaxBeatFreq = 5.0;
        _handler.EdgePauseDur = 5;
        _handler.StartBeat();
        _handler.SetFinaleAt(_handler.SessionStartTime + 40.0);

        _handler.EdgeRelief();

        Assert.Contains(_handler.PlannedSegments, s => s.Kind == "finale");
    }

    // The plan buffer holds segments already taken off the script, and the script cursor never
    // rewinds - clearing the queue silently skipped recorded segments.
    [Fact]
    public void an_edge_in_a_replay_keeps_the_recording_that_was_already_queued()
    {
        Pin();
        _handler.StartBeat(Script([
            Seg("beat", "Standard Beat", 2.0, 5.0),
            Seg("beat", "Quick Swing", 3.0, 6.0),
            Seg("beat", "Double Tap", 4.0, 7.0),
        ]));

        _handler.EdgeRelief();

        // The break itself is on the air; the rest of the recording is untouched behind it.
        Assert.Equal("pause", _handler.CurrentSegment!.Kind);
        Assert.Equal([("Quick Swing", (double?)3.0), ("Double Tap", 4.0)], _handler.PlannedSegments.Take(2).Select(s => (s.PatternName, s.Freq)));
    }

    // Once the script is spent the planner is drawing again, so relief applies as usual.
    [Fact]
    public void an_edge_past_the_end_of_a_recording_still_slows_the_beat_down()
    {
        Pin();
        _handler.MinBeatFreq = 1.0;
        _handler.MaxBeatFreq = 5.0;
        _handler.StartBeat(Script([Seg("beat", "Standard Beat", 2.0, 5.0)]));

        _handler.EdgeRelief();

        Assert.Equal(1.0, _handler.PlannedSegments[0].Freq!.Value, 9);
    }

    // The pause displaces the whole plan, so "be fast here" has to travel with it.
    [Fact]
    public void an_edge_moves_the_finale_marker_along_with_everything_else()
    {
        Pin(beatDur: 10.0);
        _handler.EdgePauseDur = 8;
        _handler.StartBeat();
        _handler.SetFinaleAt(_handler.SessionStartTime + 40.0);

        _handler.EdgeRelief();

        Assert.Equal(48.0, _handler.FinaleAt!.Value - _handler.SessionStartTime, 9);
    }

    // Revalidating the finale rebuilds the queue by *drawing* - which a replay can never survive,
    // because the script cursor does not rewind. A recorded queue is already right.
    [Fact]
    public void an_edge_in_a_replay_keeps_the_recorded_run_in_too()
    {
        Pin();
        _handler.StartBeat(Script([
            Seg("beat", "Standard Beat", 2.0, 5.0),
            Seg("beat", "Quick Swing", 3.0, 6.0),
            Seg("finale", "Double Tap", 5.0, 9.0),
        ]));
        _handler.SetFinaleAt(_handler.SessionStartTime + 14.0);

        _handler.EdgeRelief();

        Assert.Equal([("beat", "Quick Swing"), ("finale", "Double Tap")], _handler.PlannedSegments.Take(2).Select(s => (s.Kind, s.PatternName)));
    }
}
