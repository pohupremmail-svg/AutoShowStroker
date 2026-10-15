namespace Gooner.Core.Logging;

public enum LogLevel
{
    Info,
    Warning,
    Error,
}

public interface ILogSink
{
    void Write(LogLevel level, string category, string message, object?[] args);
}

// Where Gooner.Core reports. Silent until the host plugs in a sink - the same rules as
// src/applog.py apply to every call: INFO is the floor, values go in as arguments rather than
// being formatted into the text, and a media folder path is never logged (a bare file name only,
// in a warning about that one file). The file handler, the opt-in and the level setting arrive
// with the applog port; until then this is only the seam the ported modules log through.
public static class AppLog
{
    private sealed class NullSink : ILogSink
    {
        public void Write(LogLevel level, string category, string message, object?[] args) { }
    }

    public static ILogSink Sink { get; set; } = new NullSink();

    public static void Info(string category, string message, params object?[] args) => Sink.Write(LogLevel.Info, category, message, args);

    public static void Warning(string category, string message, params object?[] args) => Sink.Write(LogLevel.Warning, category, message, args);

    public static void Error(string category, string message, params object?[] args) => Sink.Write(LogLevel.Error, category, message, args);
}
