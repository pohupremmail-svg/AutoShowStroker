using Gooner.Core.Beat;

namespace Gooner.Core.Track;

// The beat the Look tab's live preview plays (the user's pick of the redesign mock, 2026-10-24): no
// session behind it, so a steady rhythm of its own, handed to the real note track the way
// BeatHandler.Forecast hands it - so the preview is the track itself, not a drawing of it.
public static class PreviewBeat
{
    // Two notes a second, the default beat's middle.
    public const double Gap = 0.5;

    public static TrackForecast Forecast(double now, double horizonSec)
    {
        var notes = new List<ScheduledNote>();
        // On a fixed grid from zero, so a note keeps its moment from one frame to the next.
        for (var at = Math.Ceiling(now / Gap) * Gap; at <= now + horizonSec; at += Gap)
            notes.Add(new ScheduledNote(Math.Round(at, 9), true, 1));
        var start = Math.Floor(now / 60) * 60;
        return new TrackForecast(notes, [new ForecastSpan("rhythm", "Standard Beat", start, start + 120)]);
    }
}
