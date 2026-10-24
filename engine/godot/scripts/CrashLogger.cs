using System;
using Gooner.Core.App;
using Godot;

namespace Gooner.Engine;

// Where an exception nobody caught ends up. Godot catches one escaping a callback - a frame, a
// signal, a deferred call - and reports it as an engine error; this logger is handed every engine
// error, picks out the ones that are .NET exceptions and passes them to the CrashReporter. Engine
// errors of its own (a texture it cannot read back, say) are not crashes of the app and stay where
// Godot puts them.
//
// Called from whatever thread the error happened on, so the report is posted to the main thread.
// An exception on a worker thread of our own ends the process in .NET; AppDomain's last word is
// logged before it does.
public partial class CrashLogger : Logger
{
    private readonly CrashReporter _reporter;

    public CrashLogger(CrashReporter reporter)
    {
        _reporter = reporter;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => _reporter.Report(e.ExceptionObject.ToString() ?? "unknown");
    }

    public override void _LogError(string function, string file, int line, string code, string rationale,
        bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        var text = CrashReporter.LooksLikeException(code) ? code
            : CrashReporter.LooksLikeException(rationale) ? rationale
            : null;
        if (text is null) return;
        Callable.From(() => _reporter.Report(text)).CallDeferred();
    }
}
