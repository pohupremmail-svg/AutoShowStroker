using Gooner.Core.Panels;

namespace Gooner.Parity.Tests;

// HELP_TOPICS in src/HelpDialog.py vs Gooner.Core.Panels.Guide.PythonTopics: the port carries a
// copy of the Guide, word for word, and puts the engine's differences on top of it as edits.
public class GuideParity
{
    [Fact]
    public void topics()
    {
        var expected = Golden.Load("guide")["topics"]!.AsArray()
            .Select(t => new GuideTopic((string)t!["name"]!, (string)t["html"]!))
            .ToList();

        Assert.Equal(expected, Guide.PythonTopics);
    }
}
