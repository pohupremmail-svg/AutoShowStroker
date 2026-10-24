namespace Gooner.Core.Climax;

// What the climax needs from the rhythm, and no more: BeatHandler is told "be fast across this
// moment", never "the orgasm is here" - it has no idea what a climax is.
public interface IFinalePlanner
{
    double SessionStartTime { get; }

    // When the difficulty ramp tops out, or null when there is none to wait for.
    double? RampCompleteAt { get; }

    void SetFinaleAt(double? moment);

    void HoldFinalSegment();

    // Stops the rhythm outright - what a denial does.
    void EndRhythm();
}

// What the climax needs from the callouts: a line that is said whatever the gates say.
public interface ICalloutVoice
{
    void ForceOutputSentence(string category);
}
