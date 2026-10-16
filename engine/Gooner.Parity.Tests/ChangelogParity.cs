using Gooner.Core.App;

namespace Gooner.Parity.Tests;

// src/changelog.py vs Gooner.Core.App.Changelog: the port carries a copy of every entry until the
// Python app is retired, so an entry added there and merged in fails here until it is copied over.
public class ChangelogParity
{
    [Fact]
    public void entries()
    {
        var expected = Golden.Load("changelog")["entries"]!.AsArray()
            .Select(e => new ChangelogEntry((string)e!["name"]!, (string)e["html"]!))
            .ToList();

        Assert.Equal(expected, Changelog.Entries);
    }
}
