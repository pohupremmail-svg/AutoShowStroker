using Gooner.Core.Beat;
using Gooner.Core.Simulation;
using Gooner.Core.Tests.Support;

namespace Gooner.Core.Tests.Beat;

// What the note track draws: the coming notes as UpcomingNotes has them, and the stretches of the
// plan they belong to - so a pause still to come flies in like the notes do, and a change of
// rhythm arrives at the moment it happens. Unlike the click's prediction it does not stop at a
// coming pause: the pause's length is planned, so the notes behind it are known too.
public sealed class ForecastTests : IDisposable
{
    private readonly FakeTime _time = new() { Now = 1000.0 };
    private readonly BeatHandler _handler;
    private readonly List<double> _fired = [];

    public ForecastTests()
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

    private void RunTo(double time)
    {
        while (_time.Now < time)
        {
            _time.Now = Math.Min(time, _time.Now + 1 / 60.0);
            _handler.Advance();
        }
    }

    private List<double> Heard(double horizon) =>
        [.. _handler.Forecast(horizon).Notes.Where(n => n.Audible).Select(n => n.At)];

    [Fact]
    public void up_to_a_pause_it_holds_exactly_the_notes_the_click_gets()
    {
        Pin(freq: 3.0, pattern: "Quick Swing");
        _handler.StartBeat();
        RunTo(1003.3);

        Assert.Equal(_handler.UpcomingNotes(2.5), _handler.Forecast(2.5).Notes);
    }

    [Fact]
    public void a_pause_still_to_come_is_in_it_and_so_are_the_notes_behind_it()
    {
        Pin(beatDur: 2.0, freq: 2.0, pauseChance: 1.0, pauseDur: 2);
        _handler.StartBeat();
        RunTo(1001.2);

        var forecast = _handler.Forecast(5.0);
        var predicted = Heard(5.0);
        var from = _fired.Count;
        RunTo(1006.2);

        // The rhythm ends on its note at 1002.0, the pause runs its two whole seconds from there.
        Assert.Contains(new ForecastSpan("pause", null, 1002.0, 1004.0), forecast.Spans);
        Assert.Contains(predicted, at => at > 1004.0);
        Assert.Equal(_fired.Skip(from), predicted);
    }

    [Fact]
    public void a_running_pause_spans_from_its_start_to_when_the_beat_returns()
    {
        Pin(beatDur: 2.0, freq: 2.0, pauseChance: 1.0, pauseDur: 2);
        _handler.StartBeat();
        RunTo(1002.6);
        Assert.True(_handler.IsPaused());

        Assert.Equal(new ForecastSpan("pause", null, 1002.0, 1004.0), _handler.Forecast(2.5).Spans[0]);
    }

    // The new rhythm takes over on the last note of the old one - when BeatChanged fires.
    [Fact]
    public void a_change_of_rhythm_starts_its_span_on_the_note_that_ends_the_last()
    {
        Pin(beatDur: 2.0, freq: 2.0);
        _handler.StartBeat();
        RunTo(1001.2);

        var spans = _handler.Forecast(2.5).Spans;

        Assert.Equal(new ForecastSpan("beat", "Standard Beat", 1000.0, 1002.0), spans[0]);
        Assert.Equal(new ForecastSpan("beat", "Standard Beat", 1002.0, 1004.0), spans[1]);
    }

    [Fact]
    public void a_held_final_segment_has_no_end_in_sight()
    {
        Pin(beatDur: 2.0, freq: 2.0, pauseChance: 1.0, pauseDur: 2);
        _handler.StartBeat();
        _handler.HoldFinalSegment();
        RunTo(1001.2);

        var forecast = _handler.Forecast(3.0);

        Assert.Equal(double.PositiveInfinity, Assert.Single(forecast.Spans).End);
        Assert.Equal([1001.5, 1002.0, 1002.5, 1003.0, 1003.5, 1004.0], Heard(3.0));
    }

    [Fact]
    public void nothing_is_forecast_before_the_beat_starts()
    {
        var forecast = _handler.Forecast(2.0);

        Assert.Empty(forecast.Notes);
        Assert.Empty(forecast.Spans);
    }

    [Fact]
    public void nothing_is_forecast_once_the_rhythm_stopped()
    {
        Pin();
        _handler.StartBeat();
        _handler.Stop();

        Assert.Empty(_handler.Forecast(2.0).Spans);
    }

    // Whole sessions with real dice: every note it forecast, pauses in between and all, then played.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void in_a_whole_session_every_forecast_comes_true(int seed)
    {
        const double start = 1_800_000_000.0;
        var session = new HeadlessSession(new HeadlessOptions { Seed = seed, StartAt = start, ClimaxActive = false });
        // Short, frequent pauses, so the horizon reaches past them.
        session.Beat.PauseChance = 0.5;
        session.Beat.MinPauseDur = session.Beat.MaxPauseDur = 1;
        var fired = new List<double>();
        session.Beat.BeatHit += () => fired.Add(session.Beat.ClockNow);
        session.Start();
        var behindAPause = 0;

        for (var t = 1.0; t < 600; t += 3.7)
        {
            session.AdvanceTo(t);
            var now = session.Clock.Now;
            var forecast = session.Beat.Forecast(2.5);
            var predicted = forecast.Notes.Where(n => n.Audible).Select(n => n.At).ToList();
            var from = fired.Count;
            session.AdvanceTo(now - start + 2.5 + 1 / 30.0);
            var played = fired.Skip(from).Where(at => at <= now + 2.5).ToList();

            Assert.Equal(played, predicted);
            if (forecast.Spans.Any(s => s.Kind == "pause" && s.Start > now))
                behindAPause += predicted.Count(at => at > forecast.Spans.First(s => s.Kind == "pause" && s.Start > now).End);
        }
        Assert.True(behindAPause > 0, "no forecast ever reached past a coming pause");
    }
}
