using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

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

// Where Gooner.Core reports, and the opt-in diagnostic log. Port of src/applog.py.
//
// The file log is **off by default**, and that is a product decision: a log in an app like this is
// a record of when someone used it, so it only exists once they have asked for one. The switch and
// the level live in Privacy & Data, next to where the data is disclosed and deleted.
//
// What may go in it: INFO is the floor - there is no DEBUG or TRACE, the log explains a problem to
// a human and does not trace execution. Values go in as arguments rather than being formatted into
// the text. Never a media folder path; a bare file name only in a warning about that one file.
// Paths the app owns (the data directory, res/) are fine.
public static partial class AppLog
{
    public const string LoggerName = "gooner";
    public const string LogFileName = "gooner.log";
    public const string DefaultLevel = "INFO";

    // Small on purpose: a support artifact someone might paste into a message, not an archive.
    // Two backups survive a restart mid-problem.
    public const long MaxBytes = 512 * 1024;
    public const int BackupCount = 2;

    // What the user can pick, in order - the Privacy & Data dropdown is built from this.
    public static readonly IReadOnlyList<string> Levels = ["INFO", "WARNING", "ERROR"];

    private sealed class NullSink : ILogSink
    {
        public void Write(LogLevel level, string category, string message, object?[] args) { }
    }

    private static readonly object Gate = new();
    private static FileLogSink? _file;

    public static ILogSink Sink { get; set; } = new NullSink();

    public static bool IsFileLoggingActive
    {
        get
        {
            lock (Gate) return _file is not null;
        }
    }

    public static void Info(string category, string message, params object?[] args) => Sink.Write(LogLevel.Info, category, message, args);

    public static void Warning(string category, string message, params object?[] args) => Sink.Write(LogLevel.Warning, category, message, args);

    public static void Error(string category, string message, params object?[] args) => Sink.Write(LogLevel.Error, category, message, args);

    // The level for one of Levels, INFO otherwise. An allowlist, so anything below the floor
    // resolves to INFO, and it never throws: the value comes from the settings, which an older
    // build or a hand edit can put anything into.
    public static LogLevel LevelValue(string? name) => name?.ToUpperInvariant() switch
    {
        "WARNING" => LogLevel.Warning,
        "ERROR" => LogLevel.Error,
        _ => LogLevel.Info,
    };

    public static string LogFilePath(string logDir) => Path.Combine(logDir, LogFileName);

    // Every log file on disk, rotated backups included.
    public static IReadOnlyList<string> LogFilePaths(string logDir)
    {
        if (!Directory.Exists(logDir)) return [];
        return [.. Directory.GetFiles(logDir, LogFileName + "*").Order(StringComparer.Ordinal)];
    }

    // (Re)builds where messages go. Safe to call repeatedly and at any time, and never throws: a
    // log that cannot be written is a reason to run without one, not a reason to fail startup.
    // `echo` is the host's console - what `python main.py` printed to the terminal.
    public static void Configure(bool enabled, string? logDir, string level = DefaultLevel, ILogSink? echo = null)
    {
        lock (Gate)
        {
            _file = enabled && logDir is not null ? new FileLogSink(logDir) : null;
            Sink = new ThresholdSink(LevelValue(level), echo, _file);
        }
    }

    // Removes the log and its backups; whether anything was there. Logging carries on afterwards,
    // at the level it had - the file is only ever open for the moment a line is written, so there
    // is nothing to close first.
    public static bool DeleteLogFiles(string logDir)
    {
        lock (Gate)
        {
            var removed = false;
            foreach (var path in LogFilePaths(logDir))
            {
                try
                {
                    File.Delete(path);
                    removed = true;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Warning("applog", "Could not delete {0}: {1}", Path.GetFileName(path), e.Message);
                }
            }
            return removed;
        }
    }

    // What went wrong, in words the log may hold: the exception's kind and its text with every path
    // cut down to its file name. An exception names the whole path of a file it could not read -
    // "Could not find file 'D:\Collection\x.gif'" - and a media folder, the user's own character
    // folder or a user folder with the account's name in it must never reach the log; a bare file
    // name may, in a warning about that one file. Never hand the log an exception's own Message.
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return $"{exception.GetType().Name}: {Scrub(exception.Message)}";
    }

    // `text` with every absolute path in it - a drive's or a share's - cut down to its file name. A
    // path ends at a quote or the end of the line, which is how .NET writes one into a message.
    public static string Scrub(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return AbsolutePath().Replace(text, m => Path.GetFileName(m.Value.TrimEnd('\\', '/')));
    }

    [GeneratedRegex(@"(?:[A-Za-z]:[\\/]|\\\\|//)[^'""\r\n]*")]
    private static partial Regex AbsolutePath();

    // A message with its arguments filled in - or as it is, when they do not fit: logging must
    // never be the thing that throws.
    internal static string Format(string message, object?[] args)
    {
        if (args.Length == 0) return message;
        try
        {
            return string.Format(CultureInfo.InvariantCulture, message, args);
        }
        catch (FormatException)
        {
            return message + " " + string.Join(", ", args);
        }
    }

    private sealed class ThresholdSink(LogLevel threshold, ILogSink? echo, FileLogSink? file) : ILogSink
    {
        public void Write(LogLevel level, string category, string message, object?[] args)
        {
            if (level < threshold) return;
            echo?.Write(level, category, message, args);
            file?.Write(level, category, message, args);
        }
    }

    // The log file, opened only for the moment a line is written - so merely switching logging on
    // creates nothing, and deleting it never runs into a file the log is holding open. Rotates the
    // way Python's RotatingFileHandler does: before a line that would take the file past MaxBytes,
    // gooner.log becomes gooner.log.1, .1 becomes .2, and the oldest goes.
    private sealed class FileLogSink(string logDir) : ILogSink
    {
        private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

        public void Write(LogLevel level, string category, string message, object?[] args)
        {
            var line = string.Create(CultureInfo.InvariantCulture,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {LevelName(level),-8} {LoggerName}.{category}: {Format(message, args)}\n");
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(logDir);
                    var path = LogFilePath(logDir);
                    var length = File.Exists(path) ? new FileInfo(path).Length : 0;
                    if (length > 0 && length + Utf8.GetByteCount(line) >= MaxBytes)
                        Rotate(path);
                    File.AppendAllText(path, line, Utf8);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Nowhere left to say it - the log is the thing that failed.
                }
            }
        }

        private static void Rotate(string path)
        {
            for (var i = BackupCount - 1; i >= 1; i--)
            {
                var from = $"{path}.{i}";
                if (File.Exists(from))
                    File.Move(from, $"{path}.{i + 1}", overwrite: true);
            }
            File.Move(path, path + ".1", overwrite: true);
        }

        private static string LevelName(LogLevel level) => level switch
        {
            LogLevel.Warning => "WARNING",
            LogLevel.Error => "ERROR",
            _ => "INFO",
        };
    }
}
