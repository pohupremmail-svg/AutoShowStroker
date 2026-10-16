using Gooner.Core.Beat;

namespace Gooner.Core.Panels;

// Port of src/PatternStepWidget.py without its widget: one step of a beat pattern - a duration bar
// plus a Beat/Pause button.
//
// Encodes the same signed int BeatHandler patterns use: the sign is audible or muted, the magnitude
// (1-4) the relative duration of the step. BeatHandler divides the base step time by the
// magnitude, so it is an *inverse*-duration multiplier: weight 1 is the longest step, weight 4 the
// shortest - not a loudness. Length and mute state are separate controls, so a pause can have its
// own length without adjusting it un-pausing it.
public sealed class PatternStep
{
    public const int MinWeight = 1;
    public const int MaxWeight = 4;

    private int _weight;

    public PatternStep(int value) => Value = value;

    public int Weight
    {
        get => _weight;
        set => _weight = Math.Clamp(value, MinWeight, MaxWeight);
    }

    public bool Muted { get; set; }

    public int Value
    {
        get => Muted ? -Weight : Weight;
        set
        {
            // audible = value > 0 on the Python side, so a zero is a pause.
            Muted = value <= 0;
            Weight = value == 0 ? MinWeight : Math.Abs(value);
        }
    }

    public string ButtonText => Muted ? "Pause" : "Beat";

    public string Tooltip => $"Duration weight {Weight} (1 = longest step, {MaxWeight} = shortest)";

    // How much of the bar is filled: weight 1, the longest step, draws tallest.
    public double BarRatio => (MaxWeight - Weight) / (double)(MaxWeight - MinWeight);

    // What a press or drag at `y` on a bar `height` tall sets: the top is the longest step, the
    // bottom the shortest. Rounded half to even, as round() on the Python side.
    public static int WeightAt(double y, double height)
    {
        var ratio = Math.Clamp(y / height, 0.0, 1.0);
        var level = MinWeight + (int)Math.Round(ratio * (MaxWeight - MinWeight), MidpointRounding.ToEven);
        return Math.Clamp(level, MinWeight, MaxWeight);
    }
}

public readonly record struct PreviewTick(int Step, bool Click, int NextInMs);

// Port of src/PatternEditorDialog.py without its widgets: the step-sequencer editor for the user's
// own beat patterns. Built-in patterns are read-only and never listed - only the custom ones.
//
// The preview plays the pattern round and round: each tick lights a step, says whether it clicks,
// and how long until the next - the base step time divided by the weight, as BeatHandler divides
// it. The engine runs the timer and plays the click; anything that loads another pattern stops it,
// or a preview left running would go on clicking for the rest of the app's life.
public sealed class PatternEditor
{
    public const int MaxSteps = 16;

    // The preview's step time for a weight-1 (longest) step; shorter steps get base / weight.
    public const int PreviewBaseStepMs = 400;

    public static IReadOnlyList<int> DefaultSteps { get; } = [1, 1, 1, 1];

    private readonly BeatHandler _beat;
    private readonly List<PatternStep> _steps = [];
    private int _previewPosition;

    public PatternEditor(BeatHandler beat)
    {
        ArgumentNullException.ThrowIfNull(beat);
        _beat = beat;
        StartNew();
    }

    public IReadOnlyList<string> PatternNames => [.. _beat.CustomBeatPatterns.Keys];

    // The pattern being edited; null while creating a new one.
    public string? Selected { get; private set; }

    public string Name { get; set; } = "";

    public IReadOnlyList<PatternStep> Steps => _steps;

    public IReadOnlyList<int> CurrentSteps => [.. _steps.Select(s => s.Value)];

    public string Error { get; private set; } = "";

    public bool Previewing { get; private set; }

    // --- the list ---

    public void StartNew() => Load(null, DefaultSteps);

    public void Select(string name)
    {
        if (_beat.CustomBeatPatterns.TryGetValue(name, out var steps)) Load(name, steps);
    }

    private void Load(string? name, IReadOnlyList<int> steps)
    {
        StopPreview();
        Selected = name;
        Name = name ?? "";
        SetSteps(steps);
        Error = "";
    }

    // --- steps ---

    public void SetSteps(IReadOnlyList<int> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        _steps.Clear();
        _steps.AddRange(steps.Select(value => new PatternStep(value)));
    }

    public void AddStep()
    {
        if (_steps.Count < MaxSteps) _steps.Add(new PatternStep(1));
    }

    public void RemoveStep()
    {
        if (_steps.Count > 1) _steps.RemoveAt(_steps.Count - 1);
    }

    // --- saving and deleting ---

    // Saving under a new name renames: the old entry goes once the new one is in. False, with the
    // reason in Error, when BeatHandler refuses the pattern.
    public bool Save()
    {
        var name = Name.Trim();
        var renamedFrom = Selected is { } editing && editing != name ? editing : null;
        try
        {
            _beat.AddOrUpdateCustomPattern(name, CurrentSteps);
        }
        catch (ArgumentException e)
        {
            Error = e.Message;
            return false;
        }
        if (renamedFrom is not null) _beat.DeleteCustomPattern(renamedFrom);
        Error = "";
        Selected = name;
        Name = name;
        return true;
    }

    public void DeleteSelected()
    {
        if (Selected is not { } name) return;
        _beat.DeleteCustomPattern(name);
        StartNew();
    }

    // --- preview ---

    public void StartPreview()
    {
        if (_steps.Count == 0) return;
        _previewPosition = 0;
        Previewing = true;
    }

    public void StopPreview() => Previewing = false;

    public PreviewTick PreviewTick()
    {
        if (_previewPosition >= _steps.Count) _previewPosition = 0;
        var step = _steps[_previewPosition];
        var tick = new PreviewTick(_previewPosition, step.Value > 0, PreviewBaseStepMs / step.Weight);
        _previewPosition = (_previewPosition + 1) % _steps.Count;
        return tick;
    }
}
