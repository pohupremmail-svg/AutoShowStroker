using System.Text.Json.Nodes;
using Gooner.Core.Beat;
using Gooner.Core.Devices;
using Gooner.Core.Sessions;
using Gooner.Core.Tests.Support;

namespace Gooner.Core.Tests.Devices;

// Port of tests/test_plugin_intiface_beat_sync.py: the plugin's own prediction. BeatHandler
// contributes one neutral event (NoteScheduled) and UpcomingBeats; everything device-shaped lives in
// BeatSync. The rhythm's clock is a FakeTime that never moves on its own, as the Qt timers never
// fired without an event loop; deadlines are on the monotonic clock, as the device needs them.
public sealed class BeatSyncTests : IDisposable
{
    private readonly FakeTime _time = new();
    private readonly BeatHandler _beat;
    private readonly FakeTarget _target = new();
    private readonly BeatSync _sync;

    public BeatSyncTests()
    {
        _beat = new BeatHandler(null, null, new ScriptedRandom(), _time)
        {
            RampingActive = false,
            MinBeatFreq = 2.0,
            MaxBeatFreq = 2.0,
            MinBeatDur = 30.0,
            MaxBeatDur = 30.0,
            PauseChance = 0,
            SelectedBeatPatterns = ["Standard Beat"],
        };
        _sync = new BeatSync(_beat, _target);
        _sync.Attach();
        _sync.Arm();
    }

    public void Dispose() => _beat.Stop();

    private static double Now => Monotonic.Now;

    [Fact]
    public void the_first_target_is_sent_before_the_first_beat()
    {
        var now = Now;

        _beat.StartBeat();

        var (_, deadline) = Assert.Single(_target.Targets);
        Assert.InRange(deadline - now, 0.45, 0.55);
    }

    [Fact]
    public void one_target_per_note_however_often_the_rhythm_reschedules()
    {
        _beat.StartBeat();
        _sync.OnNoteScheduled();
        _sync.OnNoteScheduled();
        Assert.Single(_target.Targets);

        _beat.Beat();

        Assert.Equal(2, _target.Targets.Count);
    }

    // Which way a stroker travels is not something a rhythm engine should have an opinion about:
    // odd notes pull, even notes push, counted here.
    [Fact]
    public void targets_alternate_across_a_whole_session()
    {
        _beat.StartBeat();
        for (var i = 0; i < 12; i++) _beat.Beat();

        var directions = _target.Targets.Select(t => t.Up).ToList();
        Assert.True(directions.Count >= 12);
        Assert.All(directions.Zip(directions.Skip(1)), pair => Assert.NotEqual(pair.First, pair.Second));
    }

    [Fact]
    public void a_rhythm_pause_cancels_whatever_the_device_is_doing()
    {
        _beat.StartBeat();

        _beat.StartPause();

        Assert.True(_target.Cancelled > 0);
    }

    [Fact]
    public void a_silent_step_extends_the_target_to_the_next_audible_note()
    {
        _beat.StartBeat();
        _beat.CurrentBeatPattern = [1, -1, -1, 1];
        _beat.CurrentBeatPosition = 1;
        _beat.PatternAudibleCount = 2;
        _beat.PatternInvSum = 4.0;
        _beat.NoteTimer.Start(250);
        _target.Targets.Clear();

        _sync.Arm();

        var (_, deadline) = Assert.Single(_target.Targets);
        Assert.InRange(deadline - Now, 0.70, 0.76);
    }

    [Fact]
    public void nothing_is_sent_while_the_rhythm_is_paused()
    {
        _beat.StartBeat();
        _target.Targets.Clear();
        _beat.StartPause();

        _sync.OnNoteScheduled();

        Assert.Empty(_target.Targets);
    }

    [Fact]
    public void nothing_is_sent_once_the_beat_has_stopped()
    {
        _beat.StartBeat();
        _beat.Stop();
        _target.Targets.Clear();

        _sync.OnNoteScheduled();

        Assert.Empty(_target.Targets);
    }

    [Fact]
    public void nothing_is_sent_until_the_sync_is_armed()
    {
        var target = new FakeTarget();
        var sync = new BeatSync(_beat, target);
        sync.Attach();
        _sync.Disarm();

        _beat.StartBeat();
        Assert.Empty(target.Targets);

        sync.Arm();
        Assert.Single(target.Targets);
    }

    [Fact]
    public void disarming_stops_further_targets()
    {
        _beat.StartBeat();
        _sync.Disarm();
        _target.Targets.Clear();

        _beat.Beat();

        Assert.Empty(_target.Targets);
    }

    [Fact]
    public void arming_mid_note_re_sends_the_note_already_running()
    {
        _beat.StartBeat();
        _sync.Disarm();
        _target.Targets.Clear();

        _sync.Arm();

        Assert.Single(_target.Targets);
    }

    private static SessionScript Script(params JsonObject[] segments) => new(new JsonObject
    {
        ["duration_sec"] = segments.Sum(s => (double)s["duration_sec"]!),
        ["segments"] = new JsonArray(segments),
        ["custom_patterns"] = new JsonObject(),
        ["climax"] = null,
        ["fake_climaxes"] = new JsonArray(),
        ["media"] = new JsonArray(),
    });

    // A replay reads its segments instead of drawing them; the device follows it the same way, at
    // the recorded frequency rather than whatever the settings say today.
    [Fact]
    public void a_replayed_session_drives_the_device_off_its_recorded_rhythm()
    {
        _beat.MinBeatFreq = _beat.MaxBeatFreq = 9.0;
        var now = Now;

        _beat.StartBeat(Script(new JsonObject { ["kind"] = "beat", ["pattern"] = "Standard Beat", ["freq"] = 1.0, ["duration_sec"] = 30.0 }));

        var (_, deadline) = Assert.Single(_target.Targets);
        Assert.InRange(deadline - now, 0.9, 1.1);
    }

    [Fact]
    public void a_replayed_pause_stops_the_device_and_the_rhythm_behind_it_starts_it()
    {
        _beat.StartBeat(Script(
            new JsonObject { ["kind"] = "pause", ["pattern"] = null, ["freq"] = null, ["duration_sec"] = 2.0 },
            new JsonObject { ["kind"] = "beat", ["pattern"] = "Standard Beat", ["freq"] = 2.0, ["duration_sec"] = 30.0 }));

        Assert.True(_sync.Paused);
        Assert.True(_target.Cancelled > 0);
        Assert.Empty(_target.Targets);

        _beat.PauseTimer.Stop();
        _beat.CurPauseDur = 0;
        _beat.PauseLoop();

        Assert.False(_sync.Paused);
        Assert.Single(_target.Targets);
    }

    private sealed class FakeTarget : IDeviceTarget
    {
        public List<(bool Up, double Deadline)> Targets { get; } = [];

        public int Cancelled { get; private set; }

        public void OnTarget(bool up, double deadline) => Targets.Add((up, deadline));

        public void CancelMotion() => Cancelled++;
    }
}
