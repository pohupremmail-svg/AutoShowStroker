using Gooner.Core.Characters;
using Gooner.Core.Randomness;

namespace Gooner.Core.Tests.Characters;

// What she says on the start screen, the way a game character idles: a line in a bubble beside her
// once she is there, typed in, left to be read, faded out - then minutes of quiet and the next one.
public sealed class GreetingTests
{
    private readonly Queue<string> _lines = new(["first", "second", "third"]);

    private Greeting Make(int seed = 1) => new(() => _lines.Count > 0 ? _lines.Dequeue() : null, new SystemRandomSource(new Random(seed)));

    // The line under the name is for when there is nobody to say it: no figure picked, or hers would
    // not load. While she loads it waits, so it does not show and then jump into her bubble.
    [Theory]
    [InlineData(FigureState.None, true)]
    [InlineData(FigureState.Failed, true)]
    [InlineData(FigureState.Loading, false)]
    [InlineData(FigureState.Here, false)]
    public void the_line_under_the_name_only_without_her(FigureState state, bool under) =>
        Assert.Equal(under, Greeting.UnderTitle(state));

    [Fact]
    public void nothing_is_said_until_she_is_here()
    {
        var greeting = Make();

        foreach (var at in new[] { 0.0, 5, 60, 600 })
        {
            greeting.Advance(at, FigureState.Loading);
            Assert.Null(greeting.Line);
        }
        Assert.Equal(3, _lines.Count);
    }

    [Fact]
    public void she_greets_a_moment_after_she_appears()
    {
        var greeting = Make();

        greeting.Advance(10, FigureState.Here);
        Assert.Null(greeting.Line);
        greeting.Advance(10 + Greeting.FirstAfter, FigureState.Here);
        Assert.Equal("first", greeting.Line);
    }

    // Long enough to type it in and read it, then gone.
    [Fact]
    public void a_line_stays_while_it_is_typed_and_read_and_then_goes()
    {
        var greeting = Make();
        greeting.Advance(0, FigureState.Here);
        greeting.Advance(Greeting.FirstAfter, FigureState.Here);
        var shown = Greeting.Shown("first");

        Assert.True(shown > Visemes.Duration("first") + 3);
        greeting.Advance(Greeting.FirstAfter + shown - 0.01, FigureState.Here);
        Assert.Equal("first", greeting.Line);
        greeting.Advance(Greeting.FirstAfter + shown, FigureState.Here);
        Assert.Null(greeting.Line);
    }

    // About three quarters of a minute of quiet between two lines (the user's call - a few minutes was
    // too long), and long enough up to be read at ease even when short.
    [Fact]
    public void she_speaks_again_after_about_45_seconds_and_lets_a_line_hang()
    {
        Assert.InRange((Greeting.MinPause + Greeting.MaxPause) / 2, 40, 50);
        Assert.True(Greeting.MinPause >= 30 && Greeting.MaxPause <= 60);
        Assert.True(Greeting.Shown("Hi.") - Visemes.Duration("Hi.") >= 7);
    }

    // Quiet between two lines - drawn anew each time, so it never ticks like a clock.
    [Fact]
    public void the_next_line_comes_after_a_pause_of_minutes()
    {
        var gone = Greeting.FirstAfter + Greeting.Shown("first");
        var pauses = new HashSet<double>();
        foreach (var seed in Enumerable.Range(1, 20))
        {
            _lines.Clear();
            foreach (var line in new[] { "first", "second" }) _lines.Enqueue(line);
            var greeting = Make(seed);
            greeting.Advance(0, FigureState.Here);
            greeting.Advance(Greeting.FirstAfter, FigureState.Here);
            greeting.Advance(gone, FigureState.Here);

            greeting.Advance(gone + Greeting.MinPause - 0.01, FigureState.Here);
            Assert.Null(greeting.Line);
            var at = gone + Greeting.MinPause;
            while (greeting.Line is null && at <= gone + Greeting.MaxPause)
            {
                greeting.Advance(at, FigureState.Here);
                at += 0.5;
            }
            Assert.Equal("second", greeting.Line);
            pauses.Add(Math.Round(at));
        }
        Assert.True(pauses.Count > 1, "the pause is the same every time");
    }

    // A session or a closed window in between: she starts over when she is back.
    [Fact]
    public void she_starts_over_when_she_comes_back()
    {
        var greeting = Make();
        greeting.Advance(0, FigureState.Here);
        greeting.Advance(Greeting.FirstAfter, FigureState.Here);

        greeting.Advance(3, FigureState.None);
        Assert.Null(greeting.Line);
        greeting.Advance(500, FigureState.Here);
        Assert.Null(greeting.Line);
        greeting.Advance(500 + Greeting.FirstAfter, FigureState.Here);
        Assert.Equal("second", greeting.Line);
    }

    // The screen comes up again, or she is to speak in another voice: the line she is on goes, and a
    // new one follows a moment later.
    [Fact]
    public void a_restart_brings_a_new_line_a_moment_later()
    {
        var greeting = Make();
        greeting.Advance(0, FigureState.Here);
        greeting.Advance(Greeting.FirstAfter, FigureState.Here);

        greeting.Restart(2);
        Assert.Null(greeting.Line);
        greeting.Advance(2 + Greeting.FirstAfter, FigureState.Here);
        Assert.Equal("second", greeting.Line);
    }

    // No line in the phrase files for the voice picked: quiet, and asked again after the next pause.
    [Fact]
    public void no_line_is_quiet_until_the_next_pause()
    {
        var greeting = new Greeting(() => null, new SystemRandomSource(new Random(1)));

        greeting.Advance(0, FigureState.Here);
        greeting.Advance(Greeting.FirstAfter, FigureState.Here);

        Assert.Null(greeting.Line);
    }
}
