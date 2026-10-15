using System.Text.Json.Nodes;
using Gooner.Core.Beat;
using Gooner.Core.Climax;
using Gooner.Core.Data;
using Gooner.Core.Sessions;
using Gooner.Core.Settings;
using Gooner.Core.Tests.Support;
using Gooner.Core.Timing;

namespace Gooner.Core.Tests.Climax;

// Port of tests/test_climax_handler.py. Python hands in MagicMocks for the beat and the callouts;
// here they are two small fakes behind the interfaces the climax actually needs. Where Python waits
// for a real QTimer, these advance the session clock.
public sealed class ClimaxHandlerTests : IDisposable
{
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-climax-");
    private readonly FakeTime _time = new() { Now = 1000.0 };
    private readonly ClockScheduler _scheduler;
    private readonly FakeBeat _beat = new();
    private readonly FakeVoice _voice = new();
    private readonly ScriptedRandom _random = new();
    private readonly ClimaxHandler _handler;

    public ClimaxHandlerTests()
    {
        _scheduler = new ClockScheduler(_time);
        _handler = Handler();
    }

    public void Dispose() => _tmp.Delete(recursive: true);

    private ClimaxHandler Handler(ISettingsStore? settings = null) => new(_beat, _voice, settings, _random, _scheduler);

    private void Advance(double seconds)
    {
        _time.Now += seconds;
        _scheduler.Advance();
    }

    private static List<Segment> Beats(string kind, params int[] indices) =>
        [.. indices.Select(i => new Segment(kind, 20.0, 2.0, "Standard Beat", i))];

    private static List<Segment> Beats(params int[] indices) => Beats("beat", indices);

    private sealed class FakeBeat : IFinalePlanner
    {
        public List<double?> FinaleCalls { get; } = [];
        public int Holds { get; private set; }
        public int Stops { get; private set; }
        public double SessionStartTime { get; set; }
        public double? RampCompleteAt { get; set; }

        public void SetFinaleAt(double? moment) => FinaleCalls.Add(moment);
        public void HoldFinalSegment() => Holds++;
        public void EndRhythm() => Stops++;
    }

    private sealed class FakeVoice : ICalloutVoice
    {
        public List<string> Said { get; } = [];

        public void ForceOutputSentence(string category) => Said.Add(category);
    }

    [Fact]
    public void defaults_dict_matches_init_defaults()
    {
        var actual = new Dictionary<string, object>
        {
            ["climax_active"] = _handler.ClimaxActive,
            ["min_climax_after"] = _handler.MinClimaxAfter,
            ["max_climax_after"] = _handler.MaxClimaxAfter,
            ["climax_only_after_ramp"] = _handler.ClimaxOnlyAfterRamp,
            ["ruined_orgasm_active"] = _handler.RuinedOrgasmActive,
            ["ruined_orgasm_chance"] = _handler.RuinedOrgasmChance,
            ["denied_orgasm_active"] = _handler.DeniedOrgasmActive,
            ["denied_orgasm_chance"] = _handler.DeniedOrgasmChance,
            ["fake_climax_active"] = _handler.FakeClimaxActive,
            ["fake_climax_chance"] = _handler.FakeClimaxChance,
            ["min_fake_climax_delay"] = _handler.MinFakeClimaxDelay,
            ["max_fake_climax_delay"] = _handler.MaxFakeClimaxDelay,
        };
        Assert.Equal(ClimaxHandler.Defaults.OrderBy(kv => kv.Key), actual.OrderBy(kv => kv.Key));
    }

    [Fact]
    public void settings_override_defaults()
    {
        var settings = new JsonSettingsStore(new UserDataStore(Path.Combine(_tmp.FullName, "settings")));
        settings.SetValue("ClimaxHandler/climax_active", false);
        settings.SetValue("ClimaxHandler/min_climax_after", 60.0);
        settings.SetValue("ClimaxHandler/max_climax_after", 90.0);
        settings.SetValue("ClimaxHandler/ruined_orgasm_active", true);
        settings.SetValue("ClimaxHandler/ruined_orgasm_chance", 0.35);
        settings.SetValue("ClimaxHandler/denied_orgasm_active", true);
        settings.SetValue("ClimaxHandler/denied_orgasm_chance", 0.45);
        settings.SetValue("ClimaxHandler/fake_climax_active", false);
        settings.SetValue("ClimaxHandler/fake_climax_chance", 0.25);
        settings.SetValue("ClimaxHandler/min_fake_climax_delay", 1.5);
        settings.SetValue("ClimaxHandler/max_fake_climax_delay", 9.5);

        var handler = Handler(settings);

        Assert.False(handler.ClimaxActive);
        Assert.Equal(60.0, handler.MinClimaxAfter);
        Assert.Equal(90.0, handler.MaxClimaxAfter);
        Assert.True(handler.RuinedOrgasmActive);
        Assert.Equal(0.35, handler.RuinedOrgasmChance);
        Assert.True(handler.DeniedOrgasmActive);
        Assert.Equal(0.45, handler.DeniedOrgasmChance);
        Assert.False(handler.FakeClimaxActive);
        Assert.Equal(0.25, handler.FakeClimaxChance);
        Assert.Equal(1.5, handler.MinFakeClimaxDelay);
        Assert.Equal(9.5, handler.MaxFakeClimaxDelay);
    }

    // --- planning the climax ---

    [Fact]
    public void the_climax_is_placed_the_configured_time_into_the_session()
    {
        _handler.ClimaxActive = true;
        _handler.MinClimaxAfter = _handler.MaxClimaxAfter = 100.0;

        _handler.OnSessionPlanned(5000.0);  // the session started at 5000

        Assert.Equal(5100.0, _handler.FinaleAt);
        Assert.Equal([5100.0], _beat.FinaleCalls);
    }

    [Fact]
    public void the_time_is_drawn_from_the_configured_range()
    {
        _handler.ClimaxActive = true;
        (_handler.MinClimaxAfter, _handler.MaxClimaxAfter) = (60.0, 300.0);
        _handler.OnSessionPlanned(5000.0);
        Assert.InRange(_handler.FinaleAt!.Value, 5060.0, 5300.0);
    }

    // It is measured from the start of the session, not from the end of the ramp - so it can be
    // set to land while the ramp is still climbing, and switching ramping off does not move it.
    [Fact]
    public void the_climax_is_independent_of_the_difficulty_ramp()
    {
        _handler.ClimaxActive = true;
        _handler.MinClimaxAfter = _handler.MaxClimaxAfter = 100.0;

        _handler.OnSessionPlanned(5000.0);

        Assert.Equal(5100.0, _handler.FinaleAt);
    }

    [Fact]
    public void no_climax_is_planned_when_it_is_switched_off()
    {
        _handler.ClimaxActive = false;
        _handler.OnSessionPlanned(5000.0);
        Assert.Null(_handler.FinaleAt);
        Assert.Equal([(double?)null], _beat.FinaleCalls);
    }

    // The planner needs to know what it is building towards - a denial and a real orgasm are not
    // the same run-in.
    [Fact]
    public void the_outcome_is_decided_up_front_not_at_the_moment_it_fires()
    {
        _handler.ClimaxActive = true;
        _handler.DeniedOrgasmActive = true;
        _handler.DeniedOrgasmChance = 1.0;

        _handler.OnSessionPlanned(5000.0);

        Assert.Equal("denied", _handler.Outcome);
    }

    // --- "only after the ramp finishes" ---

    private void At100()
    {
        _handler.ClimaxActive = true;
        _handler.MinClimaxAfter = _handler.MaxClimaxAfter = 100.0;
    }

    [Fact]
    public void the_climax_waits_for_the_ramp_when_asked_to()
    {
        _beat.RampCompleteAt = 5500.0;
        At100();  // would land at 5100
        _handler.ClimaxOnlyAfterRamp = true;

        _handler.OnSessionPlanned(5000.0);

        Assert.Equal(5500.0, _handler.FinaleAt);
        Assert.Equal([5500.0], _beat.FinaleCalls);
    }

    [Fact]
    public void waiting_for_the_ramp_never_pushes_a_late_climax_earlier()
    {
        _beat.RampCompleteAt = 5200.0;
        _handler.ClimaxActive = true;
        _handler.ClimaxOnlyAfterRamp = true;
        _handler.MinClimaxAfter = _handler.MaxClimaxAfter = 600.0;

        _handler.OnSessionPlanned(5000.0);

        Assert.Equal(5600.0, _handler.FinaleAt);
    }

    [Fact]
    public void the_climax_does_not_wait_when_the_box_is_unticked()
    {
        _beat.RampCompleteAt = 5500.0;
        At100();
        _handler.ClimaxOnlyAfterRamp = false;

        _handler.OnSessionPlanned(5000.0);

        Assert.Equal(5100.0, _handler.FinaleAt);
    }

    // Ramping switched off means RampCompleteAt is null - the box has nothing to wait for, and the
    // Ramp duration sliders must not reach the climax through the back door.
    [Fact]
    public void the_climax_does_not_wait_when_there_is_no_ramp()
    {
        _beat.RampCompleteAt = null;
        At100();
        _handler.ClimaxOnlyAfterRamp = true;

        _handler.OnSessionPlanned(5000.0);

        Assert.Equal(5100.0, _handler.FinaleAt);
    }

    [Fact]
    public void ticking_the_box_mid_session_pushes_the_climax_past_the_ramp()
    {
        _beat.RampCompleteAt = 5500.0;
        _beat.SessionStartTime = 5000.0;
        At100();
        _handler.ClimaxOnlyAfterRamp = false;
        _handler.OnSessionPlanned(5000.0);
        Assert.Equal(5100.0, _handler.FinaleAt);

        _handler.ClimaxOnlyAfterRamp = true;
        _handler.SettingsChanged();

        Assert.Equal(5500.0, _handler.FinaleAt);
        Assert.Equal(5500.0, _beat.FinaleCalls[^1]);
    }

    // The drawn moment is kept, so the box is a clamp that can be lifted again rather than a
    // one-way re-roll.
    [Fact]
    public void unticking_the_box_mid_session_restores_the_drawn_time()
    {
        _beat.RampCompleteAt = 5500.0;
        _beat.SessionStartTime = 5000.0;
        At100();
        _handler.ClimaxOnlyAfterRamp = true;
        _handler.OnSessionPlanned(5000.0);
        Assert.Equal(5500.0, _handler.FinaleAt);

        _handler.ClimaxOnlyAfterRamp = false;
        _handler.SettingsChanged();

        Assert.Equal(5100.0, _handler.FinaleAt);
    }

    // --- firing the climax ---

    [Fact]
    public void the_climax_fires_when_its_moment_arrives()
    {
        _handler.ClimaxActive = true;
        _handler.OnSessionPlanned(_time.Now + 100);

        _handler.OnClimaxDue();

        Assert.Equal(["climax_real"], _voice.Said);
        Assert.True(_handler.ClimaxTriggered);
    }

    [Fact]
    public void the_climax_fires_by_itself_on_the_session_clock()
    {
        _handler.ClimaxActive = true;
        _handler.MinClimaxAfter = _handler.MaxClimaxAfter = 0.05;

        _handler.OnSessionPlanned(_time.Now);
        Advance(0.04);
        Assert.False(_handler.ClimaxTriggered);
        Advance(0.02);

        Assert.True(_handler.ClimaxTriggered);
        Assert.Equal(["climax_real"], _voice.Said);
    }

    [Fact]
    public void the_climax_only_fires_once()
    {
        _handler.ClimaxActive = true;
        _handler.OnSessionPlanned(_time.Now + 100);

        _handler.OnClimaxDue();
        _handler.OnClimaxDue();

        Assert.Single(_voice.Said);
    }

    [Fact]
    public void the_climax_emits_its_outcome()
    {
        _handler.ClimaxActive = true;
        _handler.OnSessionPlanned(_time.Now + 100);
        var outcomes = new List<string>();
        _handler.OutcomeDecided += outcomes.Add;

        _handler.OnClimaxDue();

        Assert.Equal(["real"], outcomes);
    }

    [Theory]
    [InlineData("ruined", "ruined")]
    [InlineData("denied", "denied")]
    public void the_climax_emits_the_status_for_its_outcome(string flag, string status)
    {
        _handler.ClimaxActive = true;
        if (flag == "ruined")
        {
            _handler.RuinedOrgasmActive = true;
            _handler.RuinedOrgasmChance = 1.0;
        }
        else
        {
            _handler.DeniedOrgasmActive = true;
            _handler.DeniedOrgasmChance = 1.0;
        }
        _handler.OnSessionPlanned(_time.Now + 100);
        var statuses = new List<string>();
        _handler.StatusChanged += statuses.Add;

        _handler.OnClimaxDue();

        Assert.Equal([status], statuses);
    }

    [Fact]
    public void a_real_outcome_emits_the_cum_status()
    {
        _handler.ClimaxActive = true;
        _handler.OnSessionPlanned(_time.Now + 100);
        var statuses = new List<string>();
        _handler.StatusChanged += statuses.Add;

        _handler.OnClimaxDue();

        Assert.Equal(["cum"], statuses);
    }

    // No new beat, no pause and no beat-change callout on top of the moment the whole session was
    // built towards.
    [Fact]
    public void the_climax_holds_the_rhythm_it_was_announced_over()
    {
        _handler.ClimaxActive = true;
        _handler.OnSessionPlanned(_time.Now + 100);

        _handler.OnClimaxDue();

        Assert.Equal(1, _beat.Holds);
    }

    // The session carries on after the reveal, so the plan has to carry on too.
    [Fact]
    public void a_fake_climax_does_not_hold_the_rhythm()
    {
        _handler.FakeClimaxActive = true;
        _handler.FakeClimaxChance = 1.0;
        _handler.OnPlanExtended(Beats(7));

        _handler.OnSegmentStarted(Beats(7)[0]);

        Assert.Equal(0, _beat.Holds);
    }

    // --- fake climaxes ---

    [Fact]
    public void fakes_are_planned_onto_newly_planned_boundaries()
    {
        _handler.FakeClimaxActive = true;
        _handler.FakeClimaxChance = 1.0;

        _handler.OnPlanExtended(Beats(3, 4, 5));

        Assert.Equal(new HashSet<int> { 3, 4, 5 }, _handler.PlannedFakeBoundaries);
    }

    [Fact]
    public void no_fakes_are_planned_when_they_are_switched_off()
    {
        _handler.FakeClimaxActive = false;
        _handler.FakeClimaxChance = 1.0;
        _handler.OnPlanExtended(Beats(3, 4, 5));
        Assert.Empty(_handler.PlannedFakeBoundaries);
    }

    // Faking the climax at the exact moment the real one is due would be indistinguishable from a
    // bug, and the reveal would land on top of the real announcement.
    [Fact]
    public void the_finale_is_never_planned_as_a_fake()
    {
        _handler.FakeClimaxActive = true;
        _handler.FakeClimaxChance = 1.0;

        _handler.OnPlanExtended([.. Beats(3), .. Beats("finale", 4), .. Beats(5)]);

        Assert.Equal(new HashSet<int> { 3, 5 }, _handler.PlannedFakeBoundaries);
    }

    [Fact]
    public void a_planned_fake_fires_when_its_segment_starts()
    {
        _handler.FakeClimaxActive = true;
        _handler.FakeClimaxChance = 1.0;
        _handler.OnPlanExtended(Beats(7));

        _handler.OnSegmentStarted(Beats(7)[0]);

        Assert.Equal(["climax_real"], _voice.Said);
        Assert.True(_handler.FakeClimaxPending);
    }

    [Fact]
    public void a_segment_with_no_planned_fake_does_nothing()
    {
        _handler.OnPlanExtended(Beats(7));
        _handler.OnSegmentStarted(Beats(7)[0]);
        Assert.Empty(_voice.Said);
    }

    [Fact]
    public void a_fake_fires_only_once_for_its_boundary()
    {
        _handler.FakeClimaxActive = true;
        _handler.FakeClimaxChance = 1.0;
        _handler.OnPlanExtended(Beats(7));

        _handler.OnSegmentStarted(Beats(7)[0]);
        _handler.RevealFakeClimax();
        _voice.Said.Clear();
        _handler.OnSegmentStarted(Beats(7)[0]);

        Assert.Empty(_voice.Said);
    }

    [Fact]
    public void a_fake_does_not_fire_once_the_real_climax_has_happened()
    {
        _handler.ClimaxActive = true;
        _handler.FakeClimaxActive = true;
        _handler.FakeClimaxChance = 1.0;
        _handler.OnSessionPlanned(_time.Now + 100);
        _handler.OnPlanExtended(Beats(7));
        _handler.OnClimaxDue();
        _voice.Said.Clear();

        _handler.OnSegmentStarted(Beats(7)[0]);

        Assert.Empty(_voice.Said);
    }

    // Otherwise the fake's "only joking" would land seconds after the real announcement.
    [Fact]
    public void the_real_climax_cancels_a_pending_fake_reveal()
    {
        _handler.ClimaxActive = true;
        _handler.FakeClimaxActive = true;
        _handler.FakeClimaxChance = 1.0;
        _handler.MinFakeClimaxDelay = _handler.MaxFakeClimaxDelay = 0.05;
        _handler.OnSessionPlanned(_time.Now + 100);
        _handler.OnPlanExtended(Beats(7));
        _handler.OnSegmentStarted(Beats(7)[0]);

        _handler.OnClimaxDue();
        Advance(0.3);

        Assert.False(_handler.FakeClimaxPending);
        Assert.Equal("climax_real", _voice.Said[^1]);
    }

    [Fact]
    public void fake_climax_triggered_event_is_emitted()
    {
        _handler.FakeClimaxActive = true;
        _handler.FakeClimaxChance = 1.0;
        _handler.OnPlanExtended(Beats(7));
        var fired = 0;
        _handler.FakeClimaxTriggered += () => fired++;

        _handler.OnSegmentStarted(Beats(7)[0]);

        Assert.Equal(1, fired);
    }

    [Fact]
    public void the_real_climax_does_not_emit_the_fake_event()
    {
        _handler.ClimaxActive = true;
        _handler.OnSessionPlanned(_time.Now + 100);
        var fired = 0;
        _handler.FakeClimaxTriggered += () => fired++;

        _handler.OnClimaxDue();

        Assert.Equal(0, fired);
    }

    [Fact]
    public void a_fake_prompt_emits_the_cum_status()
    {
        _handler.FakeClimaxActive = true;
        _handler.FakeClimaxChance = 1.0;
        _handler.OnPlanExtended(Beats(7));
        var statuses = new List<string>();
        _handler.StatusChanged += statuses.Add;

        _handler.OnSegmentStarted(Beats(7)[0]);

        Assert.Equal(["cum"], statuses);
    }

    [Fact]
    public void fake_climax_reveal_fires_and_resets_pending()
    {
        _handler.FakeClimaxPending = true;

        _handler.RevealFakeClimax();

        Assert.Equal(["fake_climax_reveal"], _voice.Said);
        Assert.False(_handler.FakeClimaxPending);
    }

    [Fact]
    public void fake_climax_reveal_emits_neutral_status()
    {
        _handler.FakeClimaxPending = true;
        var statuses = new List<string>();
        _handler.StatusChanged += statuses.Add;

        _handler.RevealFakeClimax();

        Assert.Equal(["neutral"], statuses);
    }

    [Fact]
    public void fake_climax_reveal_fires_on_the_session_clock()
    {
        _handler.FakeClimaxActive = true;
        _handler.FakeClimaxChance = 1.0;
        _handler.MinFakeClimaxDelay = _handler.MaxFakeClimaxDelay = 0.05;
        _handler.OnPlanExtended(Beats(7));

        _handler.OnSegmentStarted(Beats(7)[0]);
        Assert.Equal(["climax_real"], _voice.Said);

        Advance(0.05);

        Assert.False(_handler.FakeClimaxPending);
        Assert.Equal("fake_climax_reveal", _voice.Said[^1]);
    }

    // --- mid-session settings changes ---

    // Re-drawing it would make "open Settings and save" a lever for a different climax.
    [Fact]
    public void a_settings_save_keeps_the_climax_where_it_was()
    {
        At100();
        _handler.OnSessionPlanned(5000.0);

        _handler.MinClimaxAfter = _handler.MaxClimaxAfter = 5.0;
        _handler.SettingsChanged();

        Assert.Equal(5100.0, _handler.FinaleAt);
    }

    // Otherwise switching denial on mid-session would quietly do nothing.
    [Fact]
    public void a_settings_save_re_resolves_the_outcome()
    {
        _handler.ClimaxActive = true;
        _handler.OnSessionPlanned(_time.Now + 100);
        Assert.Equal("real", _handler.Outcome);

        _handler.DeniedOrgasmActive = true;
        _handler.DeniedOrgasmChance = 1.0;
        _handler.SettingsChanged();

        Assert.Equal("denied", _handler.Outcome);
    }

    [Fact]
    public void switching_the_climax_off_mid_session_withdraws_it()
    {
        _handler.ClimaxActive = true;
        _handler.OnSessionPlanned(_time.Now + 100);

        _handler.ClimaxActive = false;
        _handler.SettingsChanged();

        Assert.Null(_handler.FinaleAt);
        Assert.Null(_beat.FinaleCalls[^1]);
    }

    [Fact]
    public void switching_the_climax_on_mid_session_plans_one()
    {
        _beat.SessionStartTime = 5000.0;
        _handler.ClimaxActive = false;
        _handler.OnSessionPlanned(5000.0);

        At100();
        _handler.SettingsChanged();

        Assert.Equal(5100.0, _handler.FinaleAt);
    }

    [Fact]
    public void a_settings_save_after_the_climax_changes_nothing()
    {
        _handler.ClimaxActive = true;
        _handler.OnSessionPlanned(_time.Now + 100);
        _handler.OnClimaxDue();
        var outcome = _handler.Outcome;

        _handler.DeniedOrgasmActive = true;
        _handler.DeniedOrgasmChance = 1.0;
        _handler.SettingsChanged();

        Assert.Equal(outcome, _handler.Outcome);
    }

    // --- outcome resolution ---

    [Fact]
    public void resolve_outcome_returns_real_without_random_call_when_no_extras_active()
    {
        _random.Uniform = () => throw new InvalidOperationException("nothing should be drawn");
        _handler.RuinedOrgasmActive = false;
        _handler.DeniedOrgasmActive = false;

        Assert.Equal("real", _handler.ResolveOutcome());
    }

    private static void Weights((double Real, double Ruined, double Denied) expected, (double Real, double Ruined, double Denied) actual)
    {
        Assert.Equal(expected.Real, actual.Real, 9);
        Assert.Equal(expected.Ruined, actual.Ruined, 9);
        Assert.Equal(expected.Denied, actual.Denied, 9);
    }

    [Fact]
    public void resolve_outcome_chances_always_sum_to_one()
    {
        _handler.RuinedOrgasmActive = true;
        _handler.RuinedOrgasmChance = 0.3;
        _handler.DeniedOrgasmActive = true;
        _handler.DeniedOrgasmChance = 0.2;

        Weights((0.5, 0.3, 0.2), _handler.OutcomeWeights());
    }

    [Fact]
    public void resolve_outcome_normalizes_when_ruined_and_denied_exceed_one()
    {
        _handler.RuinedOrgasmActive = true;
        _handler.RuinedOrgasmChance = 0.8;
        _handler.DeniedOrgasmActive = true;
        _handler.DeniedOrgasmChance = 0.8;

        var weights = _handler.OutcomeWeights();

        Assert.Equal(1.0, weights.Real + weights.Ruined + weights.Denied, 9);
        Assert.Equal(0.0, weights.Real, 9);
    }

    [Fact]
    public void resolve_outcome_ruined_only()
    {
        _handler.RuinedOrgasmActive = true;
        _handler.RuinedOrgasmChance = 1.0;
        _handler.DeniedOrgasmActive = false;

        Weights((0.0, 1.0, 0.0), _handler.OutcomeWeights());
        Assert.Equal("ruined", _handler.ResolveOutcome());
    }

    [Fact]
    public void session_started_resets_state()
    {
        _handler.ClimaxTriggered = true;
        _handler.FakeClimaxPending = true;
        _handler.FinaleAt = 123.0;

        _handler.SessionStarted();

        Assert.False(_handler.ClimaxTriggered);
        Assert.False(_handler.FakeClimaxPending);
        Assert.Null(_handler.FinaleAt);
        Assert.Empty(_handler.PlannedFakeBoundaries);
    }

    // --- replaying a saved session ---

    private static SessionScript Script(double? climaxAt = 180.0, string outcome = "ruined", double[]? fakes = null,
        int segments = 6, double duration = 200.0)
    {
        var recorded = new JsonArray();
        for (var i = 0; i < segments; i++)
            recorded.Add(new JsonObject { ["kind"] = "beat", ["pattern"] = "Standard Beat", ["freq"] = 2.0, ["duration_sec"] = 20.0 });
        return new SessionScript(new JsonObject
        {
            ["duration_sec"] = duration,
            ["segments"] = recorded,
            ["custom_patterns"] = new JsonObject(),
            ["media"] = new JsonArray(),
            ["climax"] = climaxAt is null ? null : new JsonObject { ["at_sec"] = climaxAt, ["outcome"] = outcome },
            ["fake_climaxes"] = new JsonArray([.. (fakes ?? [40.0, 120.0]).Select(f => (JsonNode)f)]),
        });
    }

    [Fact]
    public void a_replay_takes_the_recorded_climax_time_and_outcome()
    {
        _handler.ClimaxActive = true;
        _handler.MinClimaxAfter = _handler.MaxClimaxAfter = 999.0;  // would land nowhere near

        _handler.OnSessionPlanned(5000.0, Script(climaxAt: 180.0, outcome: "denied"));

        Assert.Equal(5180.0, _handler.FinaleAt);
        Assert.Equal("denied", _handler.Outcome);
        Assert.Equal([5180.0], _beat.FinaleCalls);
    }

    // The time was recorded, not negotiated - holding it back would replay a different session
    // than the one that was saved.
    [Fact]
    public void a_replay_is_not_clamped_to_the_ramp()
    {
        _beat.RampCompleteAt = 5900.0;
        _handler.ClimaxActive = true;
        _handler.ClimaxOnlyAfterRamp = true;

        _handler.OnSessionPlanned(5000.0, Script(climaxAt: 100.0));

        Assert.Equal(5100.0, _handler.FinaleAt);
    }

    // A recording that stops before the climax is almost always one the user did not last through
    // - replaying it to try again has to be a session that can actually be finished.
    [Fact]
    public void a_replay_of_a_session_that_never_climaxed_draws_one()
    {
        _handler.ClimaxActive = true;
        _handler.MinClimaxAfter = _handler.MaxClimaxAfter = 500.0;

        _handler.OnSessionPlanned(5000.0, Script(climaxAt: null));

        Assert.Equal(5500.0, _handler.FinaleAt);
        Assert.Contains(_handler.Outcome, (string[])["real", "ruined", "denied"]);
        Assert.Equal([5500.0], _beat.FinaleCalls);
    }

    // Otherwise the retry would end sooner than the session it is retrying.
    [Fact]
    public void the_drawn_climax_never_lands_inside_the_recording()
    {
        _handler.ClimaxActive = true;
        _handler.MinClimaxAfter = _handler.MaxClimaxAfter = 30.0;

        _handler.OnSessionPlanned(5000.0, Script(climaxAt: null, duration: 200.0));

        Assert.True(_handler.FinaleAt >= 5200.0);
    }

    [Fact]
    public void a_replay_of_a_session_without_a_climax_still_obeys_the_climax_switch()
    {
        _handler.ClimaxActive = false;

        _handler.OnSessionPlanned(5000.0, Script(climaxAt: null));

        Assert.Null(_handler.FinaleAt);
        Assert.Equal([(double?)null], _beat.FinaleCalls);
    }

    [Fact]
    public void a_replay_pins_the_recorded_fake_outs()
    {
        _handler.FakeClimaxActive = true;
        _handler.FakeClimaxChance = 0.0;  // would never roll one live

        _handler.OnSessionPlanned(5000.0, Script(fakes: [40.0, 120.0]));

        Assert.Equal(2, _handler.ScriptedFakeCount);
    }

    [Fact]
    public void a_replay_does_not_roll_fake_outs_of_its_own()
    {
        _handler.FakeClimaxActive = true;
        _handler.FakeClimaxChance = 1.0;  // would roll one on every boundary live

        _handler.OnSessionPlanned(5000.0, Script(fakes: [], segments: 6));
        _handler.OnPlanExtended(Beats(3, 4, 5));  // all still inside the recording

        Assert.Empty(_handler.PlannedFakeBoundaries);
    }

    // The stretch after the recording is an ordinary session, and an ordinary session has
    // fake-outs in it.
    [Fact]
    public void fake_outs_start_again_past_the_end_of_the_recording()
    {
        _handler.FakeClimaxActive = true;
        _handler.FakeClimaxChance = 1.0;

        _handler.OnSessionPlanned(5000.0, Script(climaxAt: null, fakes: [], segments: 4));
        _handler.OnPlanExtended(Beats(2, 3, 4, 5));

        Assert.Equal(new HashSet<int> { 4, 5 }, _handler.PlannedFakeBoundaries);
    }

    [Fact]
    public void a_scripted_fake_out_fires_when_its_moment_arrives()
    {
        _handler.FakeClimaxActive = true;
        _handler.MinFakeClimaxDelay = _handler.MaxFakeClimaxDelay = 0.05;

        _handler.OnSessionPlanned(_time.Now, Script(climaxAt: null, fakes: [0.05]));
        Advance(0.06);

        Assert.Equal("climax_real", _voice.Said[0]);
    }

    [Fact]
    public void a_new_live_session_forgets_the_script()
    {
        At100();
        _handler.OnSessionPlanned(5000.0, Script(climaxAt: 180.0));

        _handler.SessionStarted();
        _handler.OnSessionPlanned(6000.0);

        Assert.Equal(6100.0, _handler.FinaleAt);
    }

    // --- a denial ends the rhythm on the spot ---

    // There is nothing left to stroke to, and that is the point: carrying on after a denial has to
    // be the user's own doing, not the app still driving them.
    [Fact]
    public void a_denied_climax_stops_the_beat_immediately()
    {
        _handler.ClimaxActive = true;
        _handler.DeniedOrgasmActive = true;
        _handler.DeniedOrgasmChance = 1.0;
        _handler.RuinedOrgasmActive = false;

        _handler.OnSessionPlanned(_time.Now - 1);
        _handler.OnClimaxDue();

        Assert.Equal("denied", _handler.Outcome);
        Assert.Equal(1, _beat.Stops);
        Assert.Equal(0, _beat.Holds);
    }

    [Fact]
    public void a_real_climax_still_carries_the_beat_through_it()
    {
        _handler.ClimaxActive = true;
        _handler.RuinedOrgasmActive = false;
        _handler.DeniedOrgasmActive = false;

        _handler.OnSessionPlanned(_time.Now - 1);
        _handler.OnClimaxDue();

        Assert.Equal("real", _handler.Outcome);
        Assert.Equal(1, _beat.Holds);
        Assert.Equal(0, _beat.Stops);
    }

    // --- an edge pushes everything back ---

    // Without this an edge pause would silently cost the user the rhythm leading up to the climax
    // rather than buying them time - the climax sits on an absolute clock.
    [Fact]
    public void postponing_moves_the_climax_and_tells_the_planner()
    {
        At100();
        _handler.OnSessionPlanned(5000.0);
        _beat.FinaleCalls.Clear();

        _handler.Postpone(20.0);

        Assert.Equal(5120.0, _handler.FinaleAt);
        Assert.Equal([5120.0], _beat.FinaleCalls);
    }

    // The pre-clamp moment has to move too, or saving Settings snaps the climax back to where it
    // was before the edge.
    [Fact]
    public void postponing_survives_a_later_settings_change()
    {
        At100();
        _handler.OnSessionPlanned(5000.0);

        _handler.Postpone(20.0);
        _handler.SettingsChanged();

        Assert.Equal(5120.0, _handler.FinaleAt);
    }

    // Everything after the edge plays as recorded, just later - the fakes included.
    [Fact]
    public void postponing_shifts_a_replays_recorded_fake_outs_too()
    {
        _handler.FakeClimaxActive = true;
        _handler.OnSessionPlanned(_time.Now, Script(climaxAt: 200.0, fakes: [60.0]));
        var before = _handler.ScriptedFakeTimers[0].RemainingMs;

        _handler.Postpone(20.0);

        Assert.True(_handler.ScriptedFakeTimers[0].RemainingMs >= before + 19000);
    }

    [Fact]
    public void postponing_does_nothing_once_the_climax_has_already_landed()
    {
        At100();
        _handler.OnSessionPlanned(5000.0);
        _handler.ClimaxTriggered = true;

        _handler.Postpone(20.0);

        Assert.Equal(5100.0, _handler.FinaleAt);
    }

    [Fact]
    public void postponing_a_session_without_a_climax_is_harmless()
    {
        _handler.ClimaxActive = false;
        _handler.OnSessionPlanned(5000.0);

        _handler.Postpone(20.0);

        Assert.Null(_handler.FinaleAt);
    }

    // A new session must not open under the previous session's verdict.
    [Fact]
    public void session_start_clears_a_status_left_over_from_the_last_climax()
    {
        var seen = new List<string>();
        _handler.StatusChanged += seen.Add;

        _handler.Outcome = "real";
        _handler.TriggerRealClimax();
        Assert.Equal("cum", seen[^1]);  // precondition: a real climax puts a verdict on screen

        _handler.SessionStarted();

        Assert.Equal("neutral", seen[^1]);
    }

    [Fact]
    public void session_start_clears_a_denied_status_too()
    {
        var seen = new List<string>();
        _handler.StatusChanged += seen.Add;

        _handler.Outcome = "denied";
        _handler.TriggerRealClimax();
        Assert.Equal("denied", seen[^1]);

        _handler.SessionStarted();

        Assert.Equal("neutral", seen[^1]);
    }

    // --- what the beat and the callouts are to the climax ---

    [Fact]
    public void the_real_handlers_are_what_the_climax_talks_to()
    {
        Assert.IsAssignableFrom<IFinalePlanner>(new BeatHandler(random: new ScriptedRandom(), clock: _time));
        Assert.IsAssignableFrom<ICalloutVoice>(new Gooner.Core.Callouts.CalloutHandler(Path.Combine(_tmp.FullName, "none")));
    }
}
