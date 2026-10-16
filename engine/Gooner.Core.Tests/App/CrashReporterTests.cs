using System.Runtime.CompilerServices;
using Gooner.Core.App;
using Gooner.Core.Logging;

namespace Gooner.Core.Tests.App;

// Port of tests/test_crash_handler.py: what happens to an exception nobody caught. Godot catches
// one escaping a callback itself and reports it as an error, which the engine hands to this - so
// the app survives it, as the Python app did once it had a hook of its own. What is left to hold is
// the order (the log first, the panel second), the panel carrying the stack trace, and one
// interruption per fault.
public sealed class CrashReporterTests : IDisposable
{
    private readonly List<string> _logged = [];
    private readonly List<string> _shown = [];

    public CrashReporterTests() => AppLog.Sink = new Collector(_logged);

    public void Dispose() => AppLog.Configure(false, null);

    private CrashReporter Reporter(Action<string>? show = null) => new(show ?? _shown.Add);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Boom() => throw new InvalidOperationException("the thing exploded");

    private static Exception Raised(Action action)
    {
        try
        {
            action();
        }
#pragma warning disable CA1031 // Catching whatever the test throws, to report it.
        catch (Exception e)
#pragma warning restore CA1031
        {
            return e;
        }
        throw new InvalidOperationException("nothing was thrown");
    }

    [Fact]
    public void the_traceback_reaches_the_log()
    {
        Reporter().Report(Raised(Boom));

        var entry = Assert.Single(_logged);
        Assert.StartsWith("ERROR", entry);
        Assert.Contains("the thing exploded", entry);
        Assert.Contains(nameof(Boom), entry);
    }

    // With the diagnostic log off - the default - the panel is the only artifact there is, so it
    // carries the stack trace rather than just apologising.
    [Fact]
    public void the_user_is_shown_something_they_can_report()
    {
        Reporter().Report(Raised(Boom));

        var details = Assert.Single(_shown);
        Assert.Contains("InvalidOperationException", details);
        Assert.Contains("the thing exploded", details);
        Assert.Contains(nameof(Boom), details);
        Assert.NotEmpty(CrashReporter.Message);
    }

    // A display that cannot be built is not allowed to become a second crash.
    [Fact]
    public void a_broken_dialog_is_survived_and_still_leaves_the_log_entry()
    {
        var reporter = Reporter(_ => throw new InvalidOperationException("even the panel is broken"));

        reporter.Report(Raised(Boom));

        Assert.Contains(_logged, line => line.Contains("the thing exploded", StringComparison.Ordinal));
        Assert.Contains(_logged, line => line.Contains("Could not show the crash panel", StringComparison.Ordinal));
    }

    // While one panel is up a second fault is written down but not put on top of it - dropping it
    // without a record would defeat the point.
    [Fact]
    public void a_failure_while_the_panel_is_up_is_logged_not_shown()
    {
        var reporter = Reporter();
        reporter.Report(Raised(Boom));

        reporter.Report(Raised(() => throw new ArgumentException("a timer misfired while the panel was up")));

        Assert.Single(_shown);
        Assert.Contains(_logged, line => line.Contains("a timer misfired", StringComparison.Ordinal));
    }

    [Fact]
    public void once_the_panel_is_closed_a_new_fault_is_shown_again()
    {
        var reporter = Reporter();
        reporter.Report(Raised(Boom));
        reporter.Dismissed();

        reporter.Report(Raised(() => throw new ArgumentException("a completely different problem")));

        Assert.Equal(2, _shown.Count);
    }

    // Because the app survives, the same fault can happen again - something failing every frame
    // would otherwise put a panel up every frame. The record is still written each time.
    [Fact]
    public void the_same_failure_only_interrupts_you_once()
    {
        var reporter = Reporter();
        for (var i = 0; i < 3; i++)
        {
            reporter.Report(Raised(Boom));
            reporter.Dismissed();
        }

        Assert.Single(_shown);
        Assert.Equal(3, _logged.Count(line => line.Contains("the thing exploded", StringComparison.Ordinal)));
    }

    // The suppression is per fault, not a one-shot mute for the rest of the session.
    [Fact]
    public void a_different_failure_is_still_worth_showing()
    {
        var reporter = Reporter();
        reporter.Report(Raised(Boom));
        reporter.Dismissed();

        reporter.Report(Raised(() => throw new ArgumentException("a completely different problem")));

        Assert.Equal(2, _shown.Count);
        Assert.Contains("ArgumentException", _shown[1]);
    }

    // A crash before there is anything to show it on still has to be logged.
    [Fact]
    public void it_reports_without_a_panel_to_show()
    {
        var reporter = new CrashReporter(show: null);

        reporter.Report(Raised(Boom));

        Assert.Contains(_logged, line => line.Contains("the thing exploded", StringComparison.Ordinal));
    }

    // --- the same fault, told apart from another ---

    // What the engine hands over is the exception as text, as .NET writes it: the type and message,
    // then the frames. The fault is the type and where it was raised.
    [Fact]
    public void a_fault_is_its_type_and_where_it_was_raised()
    {
        const string text = "System.InvalidOperationException: the thing exploded\n" +
            "   at Gooner.Engine.Main.Boom() in C:\\src\\Main.cs:line 12\n" +
            "   at Gooner.Engine.Main._Process(Double delta) in C:\\src\\Main.cs:line 40";

        Assert.Equal(("System.InvalidOperationException", "Gooner.Engine.Main.Boom() in C:\\src\\Main.cs:line 12"), CrashReporter.Signature(text));
    }

    [Fact]
    public void a_fault_without_frames_is_its_type_alone() =>
        Assert.Equal(("System.Exception", null), CrashReporter.Signature("System.Exception: no frames here"));

    [Theory]
    [InlineData("System.NullReferenceException: Object reference not set to an instance of an object.\n   at X.Y()", true)]
    [InlineData("Gooner.Core.Things.SomeException: boom", true)]
    [InlineData("Texture requires the CAN_COPY_FROM bit to be set to be retrieved.", false)]
    [InlineData("Condition \"data.is_empty()\" is true. Returning: Ref<Image>()", false)]
    public void only_an_exception_counts_as_a_crash(string text, bool isException) =>
        Assert.Equal(isException, CrashReporter.LooksLikeException(text));

    private sealed class Collector(List<string> lines) : ILogSink
    {
        public void Write(LogLevel level, string category, string message, object?[] args) =>
            lines.Add($"{level.ToString().ToUpperInvariant()} {string.Format(System.Globalization.CultureInfo.InvariantCulture, message, args)}");
    }
}
