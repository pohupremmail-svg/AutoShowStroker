using Gooner.Core.Sessions;
using Gooner.Core.Simulation;
using Gooner.Core.Tests.Support;

namespace Gooner.Core.Tests.Simulation;

// A whole session played without a screen: the beat, the climax, the callouts, the score and the
// recorder wired the way GoonerApp wires them, on a simulated clock that runs as fast as the
// machine can. What the console harness runs, and what holds the pieces to working together.
public class HeadlessSessionTests
{
    private static readonly string Callouts = Repo.PathTo("res/callouts");

    private static HeadlessSession Session(int seed, double climaxAfter = 90.0) =>
        new(new HeadlessOptions
        {
            Seed = seed,
            StartAt = 1_800_000_000.0,
            CalloutDir = Callouts,
            MinClimaxAfter = climaxAfter,
            MaxClimaxAfter = climaxAfter + 30.0,
            // The ramp takes 10 to 30 minutes by default, and the climax would wait for it.
            ClimaxOnlyAfterRamp = false,
        });

    [Fact]
    public void a_session_plays_through_to_its_climax_without_a_screen()
    {
        var session = Session(seed: 1);

        session.Start();
        session.RunFor(maxSeconds: 600);

        Assert.True(session.Ended);
        Assert.Contains(session.Events, e => e.What == "outcome");
        Assert.True(session.Score.BeatCount > 0);
        Assert.NotEmpty(session.Recorder.Timeline().Segments);
        Assert.NotNull(session.Recorder.Timeline().ClimaxAt);
        // Ended a short while after the climax, as a user would stop it.
        var outcomeAt = session.Events.First(e => e.What == "outcome").At;
        Assert.InRange(session.Events[^1].At - outcomeAt, 0, HeadlessOptions.DefaultLingerAfterClimax + 1);
    }

    [Fact]
    public void the_same_seed_plays_the_same_session()
    {
        var first = Session(seed: 7);
        first.Start();
        first.RunFor(maxSeconds: 600);
        var second = Session(seed: 7);
        second.Start();
        second.RunFor(maxSeconds: 600);

        Assert.Equal(first.Events, second.Events);
        Assert.Equal(first.Score.BeatCount, second.Score.BeatCount);
    }

    [Fact]
    public void a_different_seed_plays_a_different_session() =>
        Assert.NotEqual(Played(seed: 3).Events, Played(seed: 4).Events);

    private static HeadlessSession Played(int seed)
    {
        var session = Session(seed);
        session.Start();
        session.RunFor(maxSeconds: 600);
        return session;
    }

    // The replay contract end to end: what a session saves is what its replay plays - the same
    // rhythms, the same climax moment and outcome, the same fake-outs - whatever the dice say the
    // second time round.
    [Fact]
    public void a_saved_session_replays_the_same_beat_and_climax()
    {
        var original = Played(seed: 11);
        var saved = original.Save();
        Assert.Empty(SessionFiles.ValidateSession(saved));

        var replay = Session(seed: 999);
        replay.Start(new SessionScript(saved));
        replay.RunFor(maxSeconds: 600);

        static List<(string Kind, string? Pattern, double? Freq)> Rhythms(HeadlessSession s) =>
            [.. s.Recorder.Timeline().Segments.Select(seg => (seg.Kind, seg.Pattern, seg.Freq))];
        var played = Rhythms(original);
        Assert.Equal(played, Rhythms(replay).Take(played.Count));

        var originalTimeline = original.Recorder.Timeline();
        var replayTimeline = replay.Recorder.Timeline();
        Assert.Equal(originalTimeline.ClimaxOutcome, replayTimeline.ClimaxOutcome);
        // The climax timer counts whole milliseconds, on both sides of a save.
        Assert.Equal(originalTimeline.ClimaxAt!.Value - originalTimeline.StartedAt!.Value,
            replayTimeline.ClimaxAt!.Value - replayTimeline.StartedAt!.Value, 0.002);
        Assert.Equal(originalTimeline.FakeClimaxes.Count, replayTimeline.FakeClimaxes.Count);
    }

    // An edge takes a pause and pushes the climax back by as long - the three things GoonerApp
    // moves together.
    [Fact]
    public void an_edge_pauses_the_beat_and_pushes_the_climax_back()
    {
        var session = Session(seed: 5, climaxAfter: 200.0);
        session.Start();
        session.AdvanceTo(30);
        // An edge is refused during a pause - nothing to be relieved of.
        for (var t = 31; session.Beat.IsPaused(); t++)
            session.AdvanceTo(t);
        var before = session.Climax.FinaleAt!.Value;

        var given = session.EdgeRelief();

        Assert.True(given > 0);
        Assert.True(session.Beat.IsPaused());
        Assert.Equal(before + given, session.Climax.FinaleAt!.Value, 9);
        Assert.Equal(1, session.Score.EdgeCount);
    }

    // Running out of time is an ending too - the session does not hang without a climax.
    [Fact]
    public void a_session_without_a_climax_ends_when_its_time_is_up()
    {
        var session = new HeadlessSession(new HeadlessOptions { Seed = 2, StartAt = 1_800_000_000.0, ClimaxActive = false });

        session.Start();
        session.RunFor(maxSeconds: 120);

        Assert.True(session.Ended);
        Assert.DoesNotContain(session.Events, e => e.What == "outcome");
        Assert.Equal(120.0, session.Score.TotalRunTime!.Value, 0.05);
    }

    // The score is judged at the end like GoonerApp does, against a history that holds this one.
    [Fact]
    public void the_ended_session_is_in_the_history_and_judged()
    {
        var session = Played(seed: 1);

        Assert.Single(session.Score.GetHistory());
        Assert.NotNull(session.Unlocked);
    }
}
