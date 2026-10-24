using Gooner.Core.Beat;
using Gooner.Core.Sessions;

namespace Gooner.Core.Devices;

// What BeatSync hands its targets to - IntifaceController in the app, a recorder in a test.
public interface IDeviceTarget
{
    // Up or down, and when the note lands on the monotonic clock.
    void OnTarget(bool up, double deadline);

    void CancelMotion();
}

// Port of src/plugins/intiface/beat_sync.py: turns the rhythm into the next linear target.
//
// The BeatHandler contributes exactly one thing to device support - NoteScheduled, "the rhythm has
// committed to its next note" - and everything else is read off its public surface: UpcomingBeats
// for when the next audible note lands, its beat and pause events. It is the same rule the climax
// follows: the rhythm is only ever told "be fast here" and never learns what is listening.
//
// One target per audible note. A note's identity is how many notes have played - BeatHit counts
// them - so a re-prediction after a settings change or a resume cannot make the device stroke twice
// for the same beat. That count is also the direction: odd notes pull, even notes push. Which way a
// device travels is this class's business, and the rhythm is never asked.
public sealed class BeatSync
{
    // Far enough ahead to hold the next audible note even at the slowest usable rhythm with several
    // silent steps in front of it. Only the first audible one is ever read.
    public const double HorizonSec = 30.0;

    private readonly BeatHandler _beat;
    private readonly IDeviceTarget _target;
    private int _noteId;
    private int? _sentNoteId;

    public BeatSync(BeatHandler beat, IDeviceTarget target)
    {
        ArgumentNullException.ThrowIfNull(beat);
        ArgumentNullException.ThrowIfNull(target);
        _beat = beat;
        _target = target;
    }

    public bool Armed { get; private set; }

    // A rhythm pause: nothing moves until the beat comes back, which it does on its own.
    public bool Paused { get; private set; }

    public void Attach()
    {
        _beat.NoteScheduled += OnNoteScheduled;
        _beat.BeatHit += () => _noteId++;
        _beat.SessionPlanned += OnSessionPlanned;
        _beat.BeatPaused += OnPause;
        _beat.BeatResumed += OnPauseEnded;
    }

    // Starts driving the device from the rhythm, beginning with the note in flight.
    public void Arm()
    {
        Armed = true;
        _sentNoteId = null;
        OnNoteScheduled();
    }

    public void Disarm()
    {
        Armed = false;
        _sentNoteId = null;
        _target.CancelMotion();
    }

    public void OnNoteScheduled()
    {
        if (!Armed || Paused || _sentNoteId == _noteId) return;
        if (NextAudibleDeadline() is not { } deadline) return;
        _sentNoteId = _noteId;
        _target.OnTarget(_noteId % 2 == 1, deadline);
    }

    // When the next audible note lands, or null. UpcomingBeats also predicts through a running
    // pause - the note track draws the beat flying back in - and a device must not move during one,
    // which is what Paused guards.
    private double? NextAudibleDeadline()
    {
        var now = Monotonic.Now;
        foreach (var beat in _beat.UpcomingBeats(HorizonSec))
            if (beat.Audible) return now + beat.Offset;
        return null;
    }

    private void OnSessionPlanned(double startTime, SessionScript? script)
    {
        Paused = false;
        _sentNoteId = null;
    }

    private void OnPause()
    {
        Paused = true;
        _target.CancelMotion();
    }

    private void OnPauseEnded()
    {
        Paused = false;
        OnNoteScheduled();
    }
}
