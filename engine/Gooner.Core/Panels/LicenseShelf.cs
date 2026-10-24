namespace Gooner.Core.Panels;

// The folder of license texts behind Help > Third-Party Licenses - src/ThirdPartyLicensesDialog.py's
// list, for the engine's own folder (engine/licenses, beside the executable once exported). Several
// of the licenses only allow passing the software on when their text travels with it, so the texts
// are bundled and shown. THIRD_PARTY_NOTICES.txt comes first, as the index; every other text can be
// picked after it.
public static class LicenseShelf
{
    public const string Notices = "THIRD_PARTY_NOTICES.txt";

    private const string TextLine = "License text:";

    // The notices first, then every other .txt by name, ignoring case.
    public static IReadOnlyList<string> Files(string dir)
    {
        if (!Directory.Exists(dir)) return [];
        var others = Directory.GetFiles(dir, "*.txt")
            .Select(path => Path.GetFileName(path))
            .Where(name => name != Notices)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
        return [.. (File.Exists(Path.Combine(dir, Notices)) ? new[] { Notices } : []).Concat(others)];
    }

    // Every file a "License text:" line of the notices names.
    public static IReadOnlySet<string> NamedTexts(string notices)
    {
        ArgumentNullException.ThrowIfNull(notices);
        return notices.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith(TextLine, StringComparison.Ordinal))
            .SelectMany(line => line[TextLine.Length..].Split(','))
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .ToHashSet();
    }
}
