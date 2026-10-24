using Gooner.Core.Randomness;

namespace Gooner.Core.Characters;

// Where the figure on the start screen stands: none picked, on its way in, on screen, or it would not
// load.
public enum FigureState
{
    None,
    Loading,
    Here,
    Failed,
}

// What she says on the start screen, the way a game character idles (the user's call): a main_menu
// line in a bubble beside her once she is there - typed in, left to be read, faded out - then a few
// minutes of quiet and the next one. Without her the line stands under the name instead.
public sealed class Greeting(Func<string?> nextLine, IRandomSource random)
{
    // A moment after she has faded in, so the bubble does not arrive with her.
    public const double FirstAfter = 1.5;

    // The quiet between two lines, drawn anew each time.
    public const double MinPause = 120, MaxPause = 240;

    private double? _next, _until;

    // The line in her bubble now, or null.
    public string? Line { get; private set; }

    // The line under the app's name is for when there is nobody to say it. While she loads it waits,
    // rather than show and then jump into her bubble.
    public static bool UnderTitle(FigureState state) => state is FigureState.None or FigureState.Failed;

    // How long a line stays up: typed in on her mouth's timeline, then a reading's worth.
    public static double Shown(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return Visemes.Duration(line) + Math.Clamp(2.5 + 0.05 * line.Length, 4, 9);
    }

    // `now` in seconds on any clock that only goes forward.
    public void Advance(double now, FigureState state)
    {
        if (state != FigureState.Here)
        {
            Line = null;
            _next = null;
            return;
        }
        _next ??= now + FirstAfter;
        if (Line is not null && now >= _until)
        {
            Line = null;
            _next = now + Pause();
        }
        if (Line is null && now >= _next)
        {
            Line = nextLine();
            if (Line is null) _next = now + Pause();
            else _until = now + Shown(Line);
        }
    }

    // The screen comes up again, or her voice changed: the line goes, a new one follows shortly.
    public void Restart(double now)
    {
        Line = null;
        _next = now + FirstAfter;
    }

    private double Pause() => MinPause + random.NextDouble() * (MaxPause - MinPause);
}
