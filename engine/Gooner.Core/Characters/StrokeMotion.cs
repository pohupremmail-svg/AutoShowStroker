namespace Gooner.Core.Characters;

// How far down her hand is on the toy at a moment of the beat: 1 at the bottom of a whole stroke, 0 at
// the top, near the tip. One stroke per note, the beat its frequency (the user's spec): down on each
// note, up between two.
//
// A fast beat gets a shorter stroke, kept at the top: a whole one at five notes a second covers a
// quarter of the toy from one frame to the next, and the hand seems to jump. The stroke shrinks with
// the gap between notes, so that it never moves much more than a tenth of a whole stroke in a frame.
// How deep a note goes is settled by the shorter of the gaps either side of it - known in advance,
// since the beat is planned - so the last slow note before a fast run already stops short, and the hand
// is up there when the run begins instead of climbing to it. Going up from a note it leaves at that
// note's depth, coming down it arrives at the next one's, so there is no jump anywhere.
public static class StrokeMotion
{
    // A gap between two notes at least this long gets the whole stroke.
    public const double FullStrokeGap = 0.5;

    // The shortest stroke, as a share of the whole, however fast the beat.
    public const double ShortestStroke = 0.2;

    // A gap longer than this is a pause, not a stroke: the hand eases to a stop instead.
    public const double PauseGap = 2.5;

    // How far down the hand is at `now`, between the notes `last` and `next`; `before` is the note
    // before `last` and `after` the one after `next`, either unknown. Null when there is no stroke to
    // make: no note on one side, or a pause between them.
    public static double? Down(double now, double? before, double? last, double? next, double? after)
    {
        if (last is not { } from || next is not { } to || to <= from || to - from > PauseGap) return null;
        var gap = to - from;
        var phase = Math.Clamp((now - from) / gap, 0, 1);
        var depth = phase < 0.5 ? Depth(from - before, gap) : Depth(gap, after - to);
        return depth * 0.5 * (1 + Math.Cos(2 * Math.PI * phase));
    }

    // How deep a note goes between the gaps either side of it: as deep as the shorter allows. A gap
    // that is unknown, or a pause, has no say.
    private static double Depth(double? gapBefore, double? gapAfter)
    {
        var depth = 1.0;
        foreach (var gap in new[] { gapBefore, gapAfter })
            if (gap is > 0 and <= PauseGap) depth = Math.Min(depth, Math.Clamp(gap.Value / FullStrokeGap, ShortestStroke, 1));
        return depth;
    }
}
