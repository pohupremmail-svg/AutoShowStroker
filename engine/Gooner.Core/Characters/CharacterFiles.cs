namespace Gooner.Core.Characters;

// The figures the app brings along: res/characters/*.vrm. Until there is a picker, the first by
// name is the one on the start screen.
public static class CharacterFiles
{
    public static string? First(string? resources)
    {
        var dir = resources is null ? null : Path.Combine(resources, "characters");
        if (dir is null || !Directory.Exists(dir)) return null;
        // An extension test rather than a "*.vrm" pattern: on Windows that also matches *.vrma, the
        // format's animation files.
        return Directory.EnumerateFiles(dir)
            .Where(file => string.Equals(Path.GetExtension(file), ".vrm", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
