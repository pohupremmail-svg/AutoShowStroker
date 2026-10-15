using System.Text.Json.Nodes;
using Gooner.Core.Logging;

namespace Gooner.Parity.Tests;

// src/applog.py vs Gooner.Core.Logging.AppLog: the same messages at each configured level, the
// file each side writes compared line by line past the timestamp - the level column, the logger
// name, what the threshold lets through.
public class AppLogParity
{
    private const string Module = "applog";

    public static IEnumerable<object[]> Files() => Golden.Cases(Module, "files");

    [Theory]
    [MemberData(nameof(Files))]
    public void file(string name, string json)
    {
        var c = JsonNode.Parse(json)!;
        var dir = Directory.CreateTempSubdirectory("gooner-parity-applog-");
        try
        {
            AppLog.Configure(enabled: true, logDir: dir.FullName, level: name);
            foreach (var line in c["lines"]!.AsArray())
            {
                var l = line!.AsArray();
                var args = l[4]!.AsArray().Select(a => a is JsonValue v && v.TryGetValue<int>(out var i) ? (object?)i : (string?)a).ToArray();
                switch ((string)l[0]!)
                {
                    case "info": AppLog.Info((string)l[1]!, (string)l[3]!, args); break;
                    case "warning": AppLog.Warning((string)l[1]!, (string)l[3]!, args); break;
                    case "error": AppLog.Error((string)l[1]!, (string)l[3]!, args); break;
                }
            }
            AppLog.Configure(enabled: false, logDir: null);
            var path = AppLog.LogFilePath(dir.FullName);
            var actual = File.Exists(path) ? File.ReadAllLines(path).Select(line => line[20..]).ToList() : [];
            Assert.Equal(c["expected"]!.AsArray().Select(e => (string)e!), actual);
        }
        finally
        {
            AppLog.Configure(enabled: false, logDir: null);
            dir.Delete(recursive: true);
        }
    }
}
