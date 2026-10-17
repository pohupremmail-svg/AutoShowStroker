using System.Globalization;
using Gooner.Core.Formatting;
using Gooner.Core.Media;
using Gooner.Core.Sessions;

namespace Gooner.Core.Panels;

// A segment as the bar draws it: its kind, and where it starts and ends, in seconds into the session.
public sealed record ExplorerSegment(string Kind, double Start, double End);

// What a scrub found: the line under the preview, what was on screen (null for nothing yet), whether
// that is a video, and whether its frame has to be grabbed - only the first time a clip is reached.
public sealed record ExplorerMoment(string Text, string? Path, bool IsVideo, bool RequestFrame);

// Port of src/SessionExplorerDialog.py without its widgets: scrub back through the session that
// just ended. The timeline runs along the bottom the way a video's seek bar does: move along it and
// the preview shows whatever was on screen at that moment, with the rhythm that was playing. The
// point is finding a particular picture again, so its folder is one click away.
//
// The timeline is handed in and never stored anywhere: it holds media paths, which the app keeps
// out of its data directory and out of its log - unless the user saves the session, from here.
public sealed class SessionExplorer
{
    // How close the playhead has to be to a fake-out for the line to name it. A fake lasts only
    // until its reveal a few seconds later, so this is the window it was "happening" in.
    public const double FakeWindowSec = 4.0;

    public const string SavedMessage =
        "Saved under Saved Sessions on the start screen, where it can be played again.\n\n" +
        "It records the paths of the media it showed, so replaying finds them again. Delete saved sessions " +
        "under Privacy & Data, and leave the paths out when exporting one to somebody else.";

    public const string SaveFailedMessage = "This session could not be saved. There may be no room left on the disk.";

    public static IReadOnlyDictionary<string, string> KindLabels { get; } = new Dictionary<string, string>
    {
        ["beat"] = "Beat",
        ["pause"] = "Pause",
        ["finale"] = "Finale",
    };

    public static IReadOnlyDictionary<string, string> OutcomeLabels { get; } = new Dictionary<string, string>
    {
        ["real"] = "Climax",
        ["ruined"] = "Ruined climax",
        ["denied"] = "Denied",
    };

    private readonly double _start;
    private readonly SessionTimeline _timeline;
    private readonly List<RecordedMedium> _media;
    private readonly HashSet<string> _framesRequested = [];
    private readonly Func<bool>? _save;

    // `save` is GoonerApp.SaveCurrentSession; null when the session has nowhere to go.
    public SessionExplorer(SessionTimeline timeline, Func<bool>? save = null)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        _timeline = timeline;
        _save = save;
        _start = timeline.StartedAt ?? 0.0;
        Duration = Math.Max(0.0, (timeline.EndedAt ?? _start) - _start);
        // One entry per medium, in order: the explorer asks what was on screen at a moment, not
        // which segment it was filed under, so the carried-over listings drop out.
        _media = [.. timeline.Segments.SelectMany(s => s.Media).Where(m => !m.CarriedOver)];
        Segments = [.. timeline.Segments.Select(s => new ExplorerSegment(s.Kind, s.Start - _start, s.End - _start))];
        ClimaxOffset = timeline.ClimaxAt - _start;
        FakeOffsets = [.. timeline.FakeClimaxes.Select(at => at - _start)];
        Heading = HeadingFor(timeline);
    }

    public string Heading { get; }

    public double Duration { get; }

    public IReadOnlyList<ExplorerSegment> Segments { get; }

    public double? ClimaxOffset { get; }

    public string? Outcome => _timeline.ClimaxOutcome;

    public IReadOnlyList<double> FakeOffsets { get; }

    public double? PlayheadOffset { get; private set; }

    public string? SelectedPath { get; private set; }

    // --- the bar ---

    public double TimeAtX(double x, double width)
    {
        if (Duration <= 0 || width <= 0) return 0.0;
        return Math.Clamp(x / width * Duration, 0.0, Duration);
    }

    public double XForTime(double offset, double width) => Duration <= 0 ? 0.0 : offset / Duration * width;

    // --- scrubbing ---

    public ExplorerMoment ScrubTo(double offset)
    {
        PlayheadOffset = offset;
        var moment = _start + offset;
        SelectedPath = _media.FirstOrDefault(m => m.Start <= moment && moment < m.End)?.Path;
        var isVideo = SelectedPath is { } path && MediaKinds.Of(path) == MediaKind.Video;
        // Images and GIFs decode straight off disk; a clip's frame is grabbed once and kept, so
        // scrubbing back and forth over it does not decode it every time.
        var request = isVideo && _framesRequested.Add(SelectedPath!);
        return new ExplorerMoment(Describe(offset, moment), SelectedPath, isVideo, request);
    }

    public bool CanReveal => SelectedPath is not null;

    // The containing folder rather than the file: opening the file would launch it in whatever is
    // registered for the type, which is what Open in player is for.
    public string? RevealFolder => SelectedPath is { } path ? System.IO.Path.GetDirectoryName(path) : null;

    private string Describe(double offset, double moment)
    {
        var parts = new List<string> { Clock.FormatClock(offset) };
        var segment = _timeline.Segments.FirstOrDefault(s => s.Start <= moment && moment < s.End)
            ?? (_timeline.Segments.Count > 0 ? _timeline.Segments[^1] : null);
        if (segment is null) return parts[0];
        parts.Add(KindLabels.GetValueOrDefault(segment.Kind, segment.Kind));
        if (segment.Freq is { } freq && freq != 0) parts.Add(freq.ToString("F2", CultureInfo.InvariantCulture) + " Hz");
        if (!string.IsNullOrEmpty(segment.Pattern)) parts.Add(segment.Pattern);
        if (_timeline.FakeClimaxes.Any(at => Math.Abs(moment - at) <= FakeWindowSec)) parts.Add("- Fake-out");
        if (_timeline.ClimaxAt is { } climax && segment.Start <= climax && climax < segment.End)
            parts.Add($"- {OutcomeLabel(_timeline.ClimaxOutcome)} landed here");
        return string.Join("   ", parts);
    }

    private static string OutcomeLabel(string? outcome) =>
        outcome is null ? "None" : OutcomeLabels.GetValueOrDefault(outcome, outcome);

    private static string HeadingFor(SessionTimeline timeline)
    {
        var duration = Math.Max(0.0, (timeline.EndedAt ?? 0.0) - (timeline.StartedAt ?? 0.0));
        var parts = new List<string> { $"{timeline.Segments.Count} segments over {Clock.FormatClock(duration)}" };
        if (timeline.FakeClimaxes.Count is var count and > 0)
            parts.Add($"{count} fake-out{(count > 1 ? "s" : "")} survived");
        if (!string.IsNullOrEmpty(timeline.ClimaxOutcome)) parts.Add(OutcomeLabel(timeline.ClimaxOutcome));
        return string.Join("  -  ", parts);
    }

    // --- saving ---

    public bool CanSave => _save is not null;

    // Saved once and done: the same session saved twice would be two identical entries on the
    // shelf, so the button stays disabled, saying so, rather than vanishing.
    public bool Saved { get; private set; }

    // Whether it landed; a failed save can be tried again.
    public bool Save()
    {
        if (_save is null || Saved) return Saved;
        Saved = _save();
        return Saved;
    }
}
