using Gooner.Core.Logging;

namespace Gooner.Core.Tests.Logging;

// Port of tests/test_applog.py. Not ported: the root-logger and stderr tests - there is no root
// logger here, and the console echo is whatever the host hands to Configure. DEBUG and CRITICAL
// do not exist on this side at all, so "INFO is the floor" holds by construction. The sink is one
// for the whole process, which is why the test assembly runs in sequence (AssemblyInfo.cs).
public sealed class AppLogTests : IDisposable
{
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-applog-");

    private string Dir => _tmp.FullName;

    private string Contents()
    {
        var path = AppLog.LogFilePath(Dir);
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }

    // A test that left file logging on would keep writing into the previous test's folder.
    public void Dispose()
    {
        AppLog.Configure(enabled: false, logDir: null);
        _tmp.Delete(recursive: true);
    }

    [Fact]
    public void a_line_names_the_app_and_the_module()
    {
        AppLog.Configure(enabled: true, logDir: Dir);

        AppLog.Info("BeatHandler", "hello");

        Assert.Contains($"{AppLog.LoggerName}.BeatHandler: hello", Contents());
    }

    [Fact]
    public void nothing_is_written_when_logging_is_disabled()
    {
        AppLog.Configure(enabled: false, logDir: Dir);

        AppLog.Error("Test", "something went wrong");

        Assert.Empty(AppLog.LogFilePaths(Dir));
    }

    [Fact]
    public void enabling_writes_to_a_file()
    {
        AppLog.Configure(enabled: true, logDir: Dir);

        AppLog.Info("Test", "hello from the test");

        Assert.Contains("hello from the test", Contents());
        Assert.Contains("INFO", Contents());
    }

    [Fact]
    public void every_level_there_is_gets_through_by_default()
    {
        AppLog.Configure(enabled: true, logDir: Dir);

        AppLog.Info("Test", "marker-info");
        AppLog.Warning("Test", "marker-warning");
        AppLog.Error("Test", "marker-error");

        Assert.Contains("marker-info", Contents());
        Assert.Contains("marker-warning", Contents());
        Assert.Contains("marker-error", Contents());
    }

    [Fact]
    public void the_level_name_is_recorded()
    {
        AppLog.Configure(enabled: true, logDir: Dir);

        AppLog.Warning("Test", "careful");

        Assert.Contains("WARNING", Contents());
    }

    [Fact]
    public void values_go_in_as_arguments()
    {
        AppLog.Configure(enabled: true, logDir: Dir);

        AppLog.Info("Test", "Playlist loaded: {0} files", 42);

        Assert.Contains("Playlist loaded: 42 files", Contents());
    }

    // A message whose placeholders do not match its arguments is still a message - logging must
    // never be the thing that throws.
    [Fact]
    public void a_message_that_does_not_format_is_written_as_it_is()
    {
        AppLog.Configure(enabled: true, logDir: Dir);

        AppLog.Info("Test", "broken {1} placeholder", "only one");

        Assert.Contains("broken {1} placeholder", Contents());
    }

    [Fact]
    public void exception_details_are_captured()
    {
        AppLog.Configure(enabled: true, logDir: Dir);

        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException e)
        {
            AppLog.Error("Test", "while doing a thing: {0}", e);
        }

        Assert.Contains("while doing a thing", Contents());
        Assert.Contains("InvalidOperationException: boom", Contents());
    }

    [Fact]
    public void disabling_again_stops_writing()
    {
        AppLog.Configure(enabled: true, logDir: Dir);
        AppLog.Info("Test", "first");

        AppLog.Configure(enabled: false, logDir: Dir);
        AppLog.Info("Test", "second");

        Assert.Contains("first", Contents());
        Assert.DoesNotContain("second", Contents());
    }

    [Fact]
    public void reconfiguring_does_not_stack_handlers()
    {
        for (var i = 0; i < 3; i++)
            AppLog.Configure(enabled: true, logDir: Dir);

        AppLog.Info("Test", "once please");

        Assert.Single(Contents().Split('\n'), line => line.Contains("once please", StringComparison.Ordinal));
    }

    // A log that cannot be opened must never stop the app from starting.
    [Fact]
    public void configure_survives_an_unwritable_directory()
    {
        var blocker = Path.Combine(Dir, "logs");
        File.WriteAllText(blocker, "I am a file, not a directory");

        AppLog.Configure(enabled: true, logDir: blocker);

        AppLog.Info("Test", "still alive");
    }

    [Fact]
    public void log_file_paths_lists_rotated_backups()
    {
        AppLog.Configure(enabled: true, logDir: Dir);
        AppLog.Info("Test", "current");
        File.WriteAllText(Path.Combine(Dir, AppLog.LogFileName + ".1"), "older");

        var names = AppLog.LogFilePaths(Dir).Select(Path.GetFileName).Order(StringComparer.Ordinal);

        Assert.Equal([AppLog.LogFileName, AppLog.LogFileName + ".1"], names);
    }

    // On Windows an open log is locked - deleting it while logging is on is the case that needs
    // care.
    [Fact]
    public void delete_log_files_removes_them_while_logging_is_active()
    {
        AppLog.Configure(enabled: true, logDir: Dir);
        AppLog.Info("Test", "to be deleted");
        File.WriteAllText(Path.Combine(Dir, AppLog.LogFileName + ".1"), "older");

        Assert.True(AppLog.DeleteLogFiles(Dir));
        Assert.Empty(AppLog.LogFilePaths(Dir));
    }

    [Fact]
    public void logging_still_works_after_a_delete()
    {
        AppLog.Configure(enabled: true, logDir: Dir);
        AppLog.Info("Test", "before");
        AppLog.DeleteLogFiles(Dir);

        AppLog.Info("Test", "after");

        Assert.Contains("after", Contents());
    }

    [Fact]
    public void delete_reports_false_when_there_was_nothing()
    {
        AppLog.Configure(enabled: false, logDir: Dir);

        Assert.False(AppLog.DeleteLogFiles(Dir));
    }

    // Merely switching it on must not leave an empty file behind - that would make "delete my
    // data" look like it failed.
    [Fact]
    public void enabling_alone_writes_no_file()
    {
        AppLog.Configure(enabled: true, logDir: Dir);

        Assert.Empty(AppLog.LogFilePaths(Dir));
    }

    [Fact]
    public void rotation_is_bounded()
    {
        AppLog.Configure(enabled: true, logDir: Dir);

        for (var i = 0; i < 4000; i++)
            AppLog.Info("Test", "padding line {0} with enough text to push the file over the limit", i);

        var paths = AppLog.LogFilePaths(Dir);
        Assert.True(paths.Count <= AppLog.BackupCount + 1);
        Assert.All(paths, p => Assert.True(new FileInfo(p).Length <= AppLog.MaxBytes * 2));
        Assert.Contains("padding line 3999", Contents());
    }

    // --- level selection ---

    [Fact]
    public void the_default_level_is_info()
    {
        AppLog.Configure(enabled: true, logDir: Dir);

        AppLog.Info("Test", "visible by default");

        Assert.Contains("visible by default", Contents());
    }

    [Theory]
    [InlineData("INFO", true, true, true)]
    [InlineData("WARNING", false, true, true)]
    [InlineData("ERROR", false, false, true)]
    public void the_configured_level_filters_everything_below_it(string level, bool infoKept, bool warningKept, bool errorKept)
    {
        AppLog.Configure(enabled: true, logDir: Dir, level: level);

        AppLog.Info("Test", "an-info-line");
        AppLog.Warning("Test", "a-warning-line");
        AppLog.Error("Test", "an-error-line");

        Assert.Equal(infoKept, Contents().Contains("an-info-line", StringComparison.Ordinal));
        Assert.Equal(warningKept, Contents().Contains("a-warning-line", StringComparison.Ordinal));
        Assert.Equal(errorKept, Contents().Contains("an-error-line", StringComparison.Ordinal));
    }

    // No DEBUG/TRACE in this app - a level below the floor is clamped, not honoured.
    [Fact]
    public void info_stays_the_floor_even_if_a_lower_level_is_asked_for()
    {
        AppLog.Configure(enabled: true, logDir: Dir, level: "DEBUG");

        AppLog.Info("Test", "should appear");

        Assert.Contains("should appear", Contents());
    }

    [Fact]
    public void an_unknown_level_name_falls_back_to_info()
    {
        AppLog.Configure(enabled: true, logDir: Dir, level: "LOUD");

        AppLog.Info("Test", "still recorded");

        Assert.Contains("still recorded", Contents());
    }

    [Theory]
    [InlineData("info", LogLevel.Info)]
    [InlineData("Warning", LogLevel.Warning)]
    [InlineData("ERROR", LogLevel.Error)]
    [InlineData("DEBUG", LogLevel.Info)]
    [InlineData(null, LogLevel.Info)]
    public void level_names_resolve_without_ever_failing(string? name, LogLevel expected) =>
        Assert.Equal(expected, AppLog.LevelValue(name));

    // The dialog builds its dropdown from this, so it cannot drift from what Configure accepts.
    [Fact]
    public void selectable_levels_are_exposed_for_the_ui() =>
        Assert.Equal(["INFO", "WARNING", "ERROR"], AppLog.Levels);

    // Delete and the level picker sit in the same dialog - wiping the file must not quietly widen
    // what gets recorded afterwards.
    [Fact]
    public void deleting_the_log_keeps_the_chosen_level()
    {
        AppLog.Configure(enabled: true, logDir: Dir, level: "ERROR");
        AppLog.Error("Test", "before the delete");

        AppLog.DeleteLogFiles(Dir);

        AppLog.Info("Test", "an info line");
        AppLog.Error("Test", "an error line");
        Assert.DoesNotContain("an info line", Contents());
        Assert.Contains("an error line", Contents());
    }

    // What `python main.py` printed to the terminal: the host decides where that goes, and it is
    // filtered by the same threshold.
    [Fact]
    public void the_host_echo_hears_what_passes_the_threshold()
    {
        var echo = new List<string>();
        AppLog.Configure(enabled: false, logDir: null, level: "WARNING", echo: new ListSink(echo));

        AppLog.Info("Test", "quiet");
        AppLog.Warning("Test", "loud {0}", 1);

        Assert.Equal(["Warning Test loud 1"], echo);
    }

    private sealed class ListSink(List<string> lines) : ILogSink
    {
        public void Write(LogLevel level, string category, string message, object?[] args) =>
            lines.Add($"{level} {category} {string.Format(System.Globalization.CultureInfo.InvariantCulture, message, args)}");
    }
}
