using System.Text.RegularExpressions;
using Gooner.Core.Localization;
using Gooner.Core.Logging;

namespace Gooner.Core.App;

// Port of src/crash_handler.py: what the app does with an exception nobody caught.
//
// Godot catches an exception escaping a callback itself and reports it as an engine error; the
// engine hands that error here (and one from a worker thread, which would end the process). So the
// app survives an unhandled error, as the Python app did once it had a hook of its own - for a
// slideshow with a rhythm behind it, a session that keeps running while saying something went wrong
// beats one that vanishes mid-use.
//
// The log entry comes first, the panel second: a display that cannot be built must not cost the
// record. The panel carries the stack trace, not an apology - the diagnostic log is off by default,
// so for most users the screen is the only place it will ever exist. Nothing is sent anywhere.
//
// Repeats are suppressed per fault (its type and where it was raised): something failing every
// frame would otherwise put a panel up every frame. Every occurrence is still logged.
public sealed partial class CrashReporter
{
    public static string Title => L10n.T("GoonerApp hit a problem");

    public static string Message => L10n.T(
        "Something went wrong that the app did not expect.\n\n" +
        "It may keep working, or it may close. If you want to report this, the details below have the " +
        "technical part - copy it into the Discord.");

    private const string Log = "crash_handler";

    private readonly Action<string>? _show;
    private readonly HashSet<(string Type, string? Where)> _alreadyShown = [];
    private bool _showing;

    // `show` puts the panel up with the details; null where there is nothing to show it on yet.
    public CrashReporter(Action<string>? show) => _show = show;

    public void Report(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Report(exception.ToString());
    }

    public void Report(string details)
    {
        ArgumentNullException.ThrowIfNull(details);
        // An exception's text names whole paths - a media folder, a user folder - and what the panel
        // shows is meant to be copied into the Discord: both get the file names only (AppLog.Scrub).
        details = AppLog.Scrub(details);
        // Unconditional, and before anything else - including a failure that happens while an
        // earlier one is being reported. Whether it reaches a file is the user's choice.
        AppLog.Error(Log, "Unhandled exception: {0}", details);
        if (_showing || _show is null) return;
        if (!_alreadyShown.Add(Signature(details))) return;
        _showing = true;
        try
        {
            _show(details);
        }
#pragma warning disable CA1031 // Already handling a crash; making a second one here would help nobody.
        catch (Exception e)
#pragma warning restore CA1031
        {
            _showing = false;
            AppLog.Warning(Log, "Could not show the crash panel: {0}", AppLog.Describe(e));
        }
    }

    // The panel has been closed; the next new fault may be shown.
    public void Dismissed() => _showing = false;

    [GeneratedRegex(@"^\s*([\w.`+]+(?:Exception|Error))(?::|\r?$)", RegexOptions.Multiline)]
    private static partial Regex ExceptionHeader();

    [GeneratedRegex(@"^\s*at (.+)$", RegexOptions.Multiline)]
    private static partial Regex Frame();

    // What counts as "the same failure again": the exception's type and the frame it was raised in.
    public static (string Type, string? Where) Signature(string details)
    {
        ArgumentNullException.ThrowIfNull(details);
        var header = ExceptionHeader().Match(details);
        var type = header.Success ? header.Groups[1].Value : details.Split('\n')[0].Trim();
        var frame = Frame().Match(details);
        return (type, frame.Success ? frame.Groups[1].Value.TrimEnd('\r') : null);
    }

    // Whether an engine error is a .NET exception - the text .NET writes for one starts with its type
    // - rather than one of the engine's own complaints, which are not crashes of the app.
    public static bool LooksLikeException(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var first = text.TrimStart().Split('\n')[0];
        return ExceptionHeader().IsMatch(first);
    }
}
