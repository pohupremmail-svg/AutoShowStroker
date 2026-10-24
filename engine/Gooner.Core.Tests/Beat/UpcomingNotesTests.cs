using Gooner.Core.Beat;
using Gooner.Core.Simulation;
using Gooner.Core.Tests.Support;

namespace Gooner.Core.Tests.Beat;

// When the coming notes will fire, exactly: absolute session-clock times worked out the way the
// scheduler will work them out, whole milliseconds and all. The click is mixed a fraction of a
// second before its note comes due, so it needs the moment itself - UpcomingBeats, ported from the
// Python side for the note track, adds up unrounded gaps and drifts by up to a millisecond a note.
public sealed class UpcomingNotesTests : IDisposable
{
    private readonly FakeTime _time = new() { Now = 1000.0 };
    private readonly BeatHandler _handler;
    private readonly List<double> _fired = [];

    public UpcomingNotesTests()
    {
        _handler = new BeatHandler(random: new ScriptedRandom(), clock: _time);
        _handler.BeatHit += () => _fired.Add(_handler.ClockNow);
    }

    public void Dispose() => _handler.Stop();

    private void Pin(double beatDur = 20.0, double freq = 2.0, double pauseChance = 0.0, int pauseDur = 5, string pattern = "Standard Beat")
    {
        _handler.MinBeatDur = _handler.MaxBeatDur = beatDur;
        _handler.MinBeatFreq = _handler.MaxBeatFreq = freq;
        _handler.MinPauseDur = _handler.MaxPauseDur = pauseDur;
        _handler.PauseChance = pauseChance;
        _handler.RampingActive = false;
        _handler.SelectedBeatPatterns = [pattern];
    }

    // Runs the clock in frame-sized steps, the way the engine does.
    private void RunTo(double time)
    {
        while (_time.Now < time)
        {
            _time.Now = Math.Min(time, _time.Now + 1 / 60.0);
            _handler.Advance();
        }
    }

    private List<double> Heard(double horizon) =>
        [.. _handler.UpcomingNotes(horizon).Where(n => n.Audible).Select(n => n.At)];

    // Predicts, plays the horizon out, and hands back both.
    private (List<double> Predicted, List<double> Played) PredictThenPlay(double horizon)
    {
        var now = _time.Now;
        var predicted = Heard(horizon);
        var from = _fired.Count;
        RunTo(now + horizon);
        return (predicted, [.. _fired.Skip(from).Where(at => at <= now + horizon)]);
    }

    [Fact]
    public void the_next_notes_are_where_the_scheduler_will_put_them()
    {
        Pin(freq: 2.0);
        _handler.StartBeat();

        Assert.Equal([1000.5, 1001.0, 1001.5, 1002.0], Heard(2.0));
    }

    [Fact]
    public void the_prediction_is_what_then_plays()
    {
        Pin(freq: 3.0, pattern: "Quick Swing");
        _handler.StartBeat();
        RunTo(1003.3);

        var (predicted, played) = PredictThenPlay(2.5);

        Assert.NotEmpty(predicted);
        Assert.Equal(played, predicted);
    }

    // The reason this exists beside UpcomingBeats: a third of a second is scheduled as 333 ms.
    [Fact]
    public void gaps_are_the_whole_milliseconds_the_scheduler_uses()
    {
        Pin(freq: 3.0);
        _handler.StartBeat();

        var notes = Heard(1.1);
        var beats = _handler.UpcomingBeats(1.1).Select(b => 1000.0 + b.Offset).ToList();

        Assert.Equal([1000.333, 1000.666, 1000.999], notes, new Close(1e-9));
        Assert.NotEqual(beats[2], notes[2], 6);
    }

    [Fact]
    public void silent_steps_are_flagged_and_still_keep_their_place()
    {
        Pin(freq: 2.0, pattern: "Simple Bounce");
        _handler.StartBeat();

        var notes = _handler.UpcomingNotes(3.0);

        // [1, 1, -1, 1, 1, -1], read the way the scheduler reads it: a note takes its audibility
        // from the step after the one that set the gap to it, so a segment opens on step 1.
        Assert.Equal([true, false, true, true, false, true], notes.Take(6).Select(n => n.Audible));
        Assert.Equal(notes.Count, notes.Select(n => n.At).Distinct().Count());
    }

    [Fact]
    public void across_a_change_of_rhythm_it_carries_on_into_the_next_segment()
    {
        Pin(beatDur: 4.0, freq: 2.0);
        _handler.StartBeat();
        _handler.MinBeatFreq = _handler.MaxBeatFreq = 4.0;
        _handler.ReplanFromNextSegment();
        RunTo(1002.9);

        var (predicted, played) = PredictThenPlay(2.5);

        // Both rhythms are in it: half-second gaps, then quarter-second ones.
        Assert.Contains(predicted.Zip(predicted.Skip(1)), p => Math.Abs(p.Second - p.First - 0.25) < 1e-9);
        Assert.Equal(played, predicted);
    }

    [Fact]
    public void through_a_running_pause_the_notes_behind_it_are_predicted()
    {
        Pin(beatDur: 2.0, freq: 2.0, pauseChance: 1.0, pauseDur: 2);
        _handler.StartBeat();
        RunTo(1002.6);
        Assert.True(_handler.IsPaused());

        var (predicted, played) = PredictThenPlay(2.5);

        Assert.NotEmpty(predicted);
        Assert.Equal(played, predicted);
    }

    // What UpcomingBeats does too: nothing is drawn through a pause still to come. Its notes are
    // predicted once it runs, which is long enough ahead for any click.
    [Fact]
    public void it_stops_at_a_pause_still_to_come()
    {
        Pin(beatDur: 2.0, freq: 2.0, pauseChance: 1.0, pauseDur: 2);
        _handler.StartBeat();
        RunTo(1001.2);

        var predicted = Heard(5.0);

        // The notes up to the one that ends the rhythm, and none behind the pause.
        Assert.Equal([1001.5, 1002.0], predicted);
    }

    [Fact]
    public void a_held_final_segment_runs_on_past_its_planned_end()
    {
        Pin(beatDur: 2.0, freq: 2.0, pauseChance: 1.0, pauseDur: 2);
        _handler.StartBeat();
        _handler.HoldFinalSegment();
        RunTo(1001.2);

        var (predicted, played) = PredictThenPlay(3.0);

        Assert.Equal([1001.5, 1002.0, 1002.5, 1003.0, 1003.5, 1004.0], predicted);
        Assert.Equal(played, predicted);
    }

    [Fact]
    public void nothing_is_predicted_before_the_beat_starts() => Assert.Empty(_handler.UpcomingNotes(2.0));

    [Fact]
    public void nothing_is_predicted_once_the_rhythm_stopped()
    {
        Pin();
        _handler.StartBeat();
        _handler.Stop();

        Assert.Empty(_handler.UpcomingNotes(2.0));
    }

    // Whole sessions with real dice, sampled every few seconds: whatever it predicted is exactly
    // what then played. No climax - a finale may re-plan what is queued, which is a change of plan,
    // not a wrong prediction.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void in_a_whole_session_every_prediction_comes_true(int seed)
    {
        const double start = 1_800_000_000.0;
        var session = new HeadlessSession(new HeadlessOptions { Seed = seed, StartAt = start, ClimaxActive = false });
        var fired = new List<double>();
        session.Beat.BeatHit += () => fired.Add(session.Beat.ClockNow);
        session.Start();
        var predictedAny = 0;

        for (var t = 1.0; t < 600; t += 3.7)
        {
            session.AdvanceTo(t);
            var now = session.Clock.Now;
            var predicted = session.Beat.UpcomingNotes(2.5).Where(n => n.Audible).Select(n => n.At).ToList();
            var from = fired.Count;
            // A frame past the horizon, wherever in its frame the prediction was taken.
            session.AdvanceTo(now - start + 2.5 + 1 / 30.0);
            var played = fired.Skip(from).Where(at => at <= now + 2.5).ToList();

            // A prediction may stop short at a pause still to come; what it does say is exact.
            Assert.Equal(played.Take(predicted.Count), predicted);
            predictedAny += predicted.Count;
        }
        Assert.True(predictedAny > 100);
    }

    private sealed class Close(double tolerance) : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) <= tolerance;

        public int GetHashCode(double obj) => 0;
    }
}
