using System.Text.RegularExpressions;
using Gooner.Core.App;
using Gooner.Core.Logging;
using Gooner.Core.Tests.Support;

namespace Gooner.Core.Tests.Logging;

// What an exception says about a file is the whole path - "Could not find file 'D:\Collection\x.gif'"
// - and a path is exactly what the log must not hold: a media folder says where the collection lives,
// the user's own character folder likewise, and a user folder carries the account's name. So the log
// is only ever told what went wrong in AppLog.Describe's words: the exception's kind and its text with
// every path cut down to its file name - the one thing a warning about that file may name.
public sealed class LogPrivacyTests : IDisposable
{
    private readonly List<string> _logged = [];

    public LogPrivacyTests() => AppLog.Sink = new Collector(_logged);

    public void Dispose() => AppLog.Configure(false, null);

    [Fact]
    public void a_missing_file_is_named_without_its_folder()
    {
        var text = AppLog.Describe(new FileNotFoundException(null, @"D:\Private Stuff\Collection\clip one.gif"));

        Assert.Contains("clip one.gif", text);
        Assert.DoesNotContain("Private", text);
        Assert.DoesNotContain("Collection", text);
        Assert.StartsWith("FileNotFoundException", text);
    }

    [Theory]
    [InlineData(@"Access to the path 'C:\Users\Someone\Pictures\a.png' is denied.", "a.png", "Someone")]
    [InlineData(@"Could not find a part of the path 'E:/stash/deep/b.mp4'.", "b.mp4", "stash")]
    [InlineData(@"The process cannot access the file '\\nas\media\c.jpg' because it is being used by another process.", "c.jpg", "nas")]
    [InlineData(@"Could not find file C:\Users\Someone\x.vrm", "x.vrm", "Someone")]
    public void every_path_in_the_text_is_cut_to_its_file_name(string message, string kept, string gone)
    {
        var text = AppLog.Describe(new IOException(message));

        Assert.Contains(kept, text);
        Assert.DoesNotContain(gone, text);
    }

    [Fact]
    public void a_text_without_a_path_stays_as_it_is() =>
        Assert.Equal("InvalidDataException: canvas 9000x9000 is too large", AppLog.Describe(new InvalidDataException("canvas 9000x9000 is too large")));

    // An exception nobody caught is logged with its stack trace, and its text can name a file too.
    [Fact]
    public void a_crash_is_logged_without_the_folders_it_names()
    {
        var reporter = new CrashReporter(_ => { });

        reporter.Report(new IOException(@"Could not find file 'D:\Private\Collection\clip.gif'."));

        var entry = Assert.Single(_logged);
        Assert.Contains("clip.gif", entry);
        Assert.DoesNotContain("Private", entry);
    }

    // And what the crash panel shows is copied into the Discord - the same text, the same cut.
    [Fact]
    public void the_crash_panel_shows_the_same_cut_text()
    {
        var shown = new List<string>();
        var reporter = new CrashReporter(shown.Add);

        reporter.Report(new IOException(@"Could not find file 'D:\Private\Collection\clip.gif'."));

        Assert.DoesNotContain("Private", Assert.Single(shown));
    }

    // The rule held for good: no call into the log hands it an exception's own text.
    [Fact]
    public void no_log_call_hands_over_an_exceptions_own_text()
    {
        var call = new Regex(@"AppLog\.(Info|Warning|Error)\((?:[^;]|\n)*?;", RegexOptions.Multiline);
        var offenders = new List<string>();
        foreach (var dir in new[] { "engine/Gooner.Core", "engine/godot/scripts" })
            foreach (var file in Directory.EnumerateFiles(Repo.PathTo(dir), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
                foreach (Match m in call.Matches(File.ReadAllText(file)))
                    if (m.Value.Contains(".Message", StringComparison.Ordinal))
                        offenders.Add($"{Path.GetFileName(file)}: {m.Value.Split('\n')[0].Trim()}");
            }

        Assert.Empty(offenders);
    }

    private sealed class Collector(List<string> lines) : ILogSink
    {
        public void Write(LogLevel level, string category, string message, object?[] args) =>
            lines.Add($"{level.ToString().ToUpperInvariant()} {string.Format(System.Globalization.CultureInfo.InvariantCulture, message, args)}");
    }
}
