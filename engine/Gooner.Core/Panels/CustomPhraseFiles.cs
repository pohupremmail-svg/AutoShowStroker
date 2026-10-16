using Gooner.Core.Callouts;

namespace Gooner.Core.Panels;

public sealed record ToneChoice(string Label, string? Tone);

// Port of src/CustomPhraseFilesDialog.py without its widgets: the user's own phrase files (the
// same {trigger_key: [phrases]} schema as res/callouts/<lang>/<tone>.json), merged onto the
// built-in phrases for a language and a tone - or every tone, which is how phrase files behaved
// before tones existed. Only calls the CalloutHandler; it stores its list itself on every change.
public sealed class CustomPhraseFiles
{
    public const string AnyTone = "Any tone";

    private readonly CalloutHandler _handler;

    public CustomPhraseFiles(CalloutHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    public IReadOnlyList<string> Languages => _handler.AvailableLanguages;

    // "Any tone" first and preselected: a file added without a thought for tones keeps being heard
    // whatever mix is ticked later.
    public IReadOnlyList<ToneChoice> ToneChoices =>
        [new(AnyTone, null), .. _handler.AvailableTones.Select(tone => new ToneChoice(CalloutHandler.ToneLabel(tone), tone))];

    public IReadOnlyList<string> Rows =>
        [.. _handler.CustomPhraseFiles.Select(file => $"{file.Lang} / {(file.Tone is { } tone ? CalloutHandler.ToneLabel(tone) : AnyTone)}: {file.Path}")];

    // Why the last file could not be added; empty after one that could.
    public string Error { get; private set; } = "";

    public bool Add(string path, string lang, string? tone)
    {
        Error = "";
        try
        {
            _handler.LoadCustomFile(path, lang, tone);
            return true;
        }
        catch (PhraseFileException e)
        {
            Error = e.Message;
            return false;
        }
    }

    // The file in that row of the list; nothing when no row is selected.
    public void Remove(int row)
    {
        Error = "";
        if (row < 0 || row >= _handler.CustomPhraseFiles.Count) return;
        _handler.UnloadCustomFile(_handler.CustomPhraseFiles[row].Path);
    }
}
