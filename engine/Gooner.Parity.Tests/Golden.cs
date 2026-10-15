using System.Globalization;
using System.Text.Json.Nodes;

namespace Gooner.Parity.Tests;

// The golden files the Python implementation wrote, and the allowlist of deliberate deviations.
internal static class Golden
{
    private static readonly Lazy<string> Dir = new(FindDir);
    private static readonly Lazy<JsonObject> Allow = new(() => JsonNode.Parse(File.ReadAllText(Path.Combine(RepoParityDir(), "allowlist.json")))!.AsObject());

    public static JsonObject Load(string module)
    {
        var path = Path.Combine(Dir.Value, module + ".json");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"No golden file at {path}. Run `.venv/Scripts/python.exe -m parity.generate` from the repo root first.");
        return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }

    // Cases of one section as xUnit theory data: (name, case).
    public static IEnumerable<object[]> Cases(string module, string section) =>
        Load(module)[section]!.AsArray().Select(c => new object[] { (string)c!["name"]!, c.ToJsonString() });

    // Why a case is allowed to differ, or null if it is not.
    public static string? Allowed(string section, string name) =>
        Allow.Value[section] is JsonObject entries && entries[name] is { } reason ? (string?)reason : null;

    // Same JSON meaning: numbers by value (Python writes 60.0 where .NET writes 60), objects
    // regardless of key order.
    public static bool Equivalent(JsonNode? a, JsonNode? b)
    {
        switch (a, b)
        {
            case (null, null):
                return true;
            case (JsonObject x, JsonObject y):
                return x.Count == y.Count && x.All(kv => y.ContainsKey(kv.Key) && Equivalent(kv.Value, y[kv.Key]));
            case (JsonArray x, JsonArray y):
                return x.Count == y.Count && x.Zip(y).All(p => Equivalent(p.First, p.Second));
            case (JsonValue x, JsonValue y):
                var kx = x.GetValueKind();
                var ky = y.GetValueKind();
                if (kx != ky) return false;
                if (kx == System.Text.Json.JsonValueKind.Number)
                    return Math.Abs(Num(x) - Num(y)) <= 1e-9 * Math.Max(1.0, Math.Abs(Num(x)));
                return x.ToJsonString() == y.ToJsonString();
            default:
                return false;
        }
    }

    private static double Num(JsonValue v) => double.Parse(v.ToJsonString(), CultureInfo.InvariantCulture);

    private static string FindDir()
    {
        var overridden = Environment.GetEnvironmentVariable("GOONER_PARITY_DIR");
        return string.IsNullOrEmpty(overridden) ? Path.Combine(RepoParityDir(), "out") : overridden;
    }

    // A path under the repository root - for reading the same res/ files the Python side read.
    public static string RepoPath(string relative) => Path.Combine(Path.GetDirectoryName(RepoParityDir())!, relative);

    private static string RepoParityDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "parity")) && Directory.Exists(Path.Combine(dir.FullName, "engine")))
                return Path.Combine(dir.FullName, "parity");
        throw new DirectoryNotFoundException("parity/ not found above " + AppContext.BaseDirectory);
    }
}
