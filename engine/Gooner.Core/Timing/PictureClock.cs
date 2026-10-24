namespace Gooner.Core.Timing;

// The clock the picture runs on, fed the audio clock once a frame. The audio clock is the truth -
// it is what the click is mixed against - but it moves in mix-sized steps and jitters by a few
// milliseconds, and a note drawn straight off it stutters on its way to the hit zone. This one
// advances by frame time, is pulled a tenth of the way towards the audio each frame, and never runs
// backwards. Found in the phase 0 spike, where it brought notes to within 2 ms of their click.
public sealed class PictureClock
{
    // Share of the gap to the audio clock closed per frame.
    public const double Pull = 0.1;

    // Further ahead than this and nudging would take seconds to catch up - after a stall on the
    // main thread, say - so the clock jumps instead.
    public const double SnapAfterSec = 0.05;

    private double? _now;

    // How often it had to jump forward. Diagnostic; zero in a run that never stalled.
    public int Snaps { get; private set; }

    public double Next(double audio, double frameSeconds)
    {
        if (_now is not { } now)
        {
            _now = audio;
            return audio;
        }
        var predicted = now + frameSeconds;
        var error = audio - predicted;
        if (Math.Abs(error) > SnapAfterSec)
        {
            // Far behind the audio: jump to it. Far ahead of it: wait for it.
            if (error > 0) Snaps++;
            _now = Math.Max(now, audio);
        }
        else
        {
            _now = Math.Max(now, predicted + error * Pull);
        }
        return _now.Value;
    }
}
