using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Gooner.Core.Beat;
using Gooner.Core.Sessions;
using Gooner.Core.Tests.Support;

namespace Gooner.Core.Tests.Sessions;

// Port of tests/test_session_format_doc.py. The session format is documented for people (and their
// assistants) outside this repo to write against - skills/gooner-session-builder. A spec that has
// drifted from the code is worse than no spec: it produces files the app refuses, and the reader
// cannot tell which of the two is wrong. Held against the engine's own rhythms and validator, so it
// keeps guarding once the Python side is gone.
public partial class SessionFormatDocTests
{
    private static readonly string SkillDir = Repo.PathTo(Path.Combine("skills", "gooner-session-builder"));

    // Read with its line endings evened out: a Windows checkout has CRLF, the patterns expect \n.
    private static readonly Lazy<string> SpecText = new(() =>
        File.ReadAllText(Path.Combine(SkillDir, "SESSION_FORMAT.md")).ReplaceLineEndings("\n"));

    private static string Spec => SpecText.Value;

    [GeneratedRegex(@"`([A-Z][A-Za-z0-9/ ]+)`")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"```json\n(\{.*?\n\})\n```", RegexOptions.Singleline)]
    private static partial Regex FirstJsonBlock();

    // The folder is meant to be dropped into somebody's own assistant whole - a SKILL.md referring
    // to a file that stayed behind in the repo would be useless there.
    [Fact]
    public void the_skill_carries_the_spec_it_points_at()
    {
        var skill = File.ReadAllText(Path.Combine(SkillDir, "SKILL.md"));

        Assert.Contains("SESSION_FORMAT.md", skill, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(SkillDir, "SESSION_FORMAT.md")));
    }

    // Naming a rhythm that does not exist is the mistake this whole document is trying to prevent,
    // so the list it gives has to be the real one.
    [Fact]
    public void the_spec_lists_every_built_in_rhythm()
    {
        foreach (var name in BeatHandler.BeatPatternsMap.Keys)
            Assert.True(Spec.Contains(name, StringComparison.Ordinal), $"{name} is playable but undocumented");
    }

    // A name in the document that the app cannot play is worse than an omission - it reads as
    // permission.
    //
    // Python's version looks only at names in backticks, and the document lists its rhythms in a
    // plain block under "Built-in rhythm names" - so there it has never found one, and passes
    // whatever the list says. Here the block itself is read too, column by column.
    [Fact]
    public void the_spec_invents_no_rhythms_of_its_own()
    {
        var builtIn = BeatHandler.BeatPatternsMap.Keys.ToHashSet();
        var quoted = Quoted().Matches(Spec).Select(m => m.Groups[1].Value)
            .Where(name => name.Contains(' ', StringComparison.Ordinal)
                && !name.StartsWith("Sessions", StringComparison.Ordinal) && !name.StartsWith("What", StringComparison.Ordinal))
            .ToHashSet();
        var listed = RhythmList().Match(Spec).Groups[1].Value.Split('\n')
            .SelectMany(line => Columns().Split(line.Trim())).Where(name => name.Length > 0).ToHashSet();

        Assert.Subset(builtIn, quoted);
        Assert.NotEmpty(listed);
        Assert.Subset(builtIn, listed);
    }

    [GeneratedRegex(@"### Built-in rhythm names.*?```\n(.*?)\n```", RegexOptions.Singleline)]
    private static partial Regex RhythmList();

    [GeneratedRegex(" {2,}")]
    private static partial Regex Columns();

    [Fact]
    public void the_spec_states_the_current_format_version() =>
        Assert.Contains($"`{SessionFiles.FormatVersion}`", Spec, StringComparison.Ordinal);

    [Fact]
    public void the_spec_names_every_segment_kind_and_outcome()
    {
        foreach (var kind in SessionFiles.SegmentKinds)
            Assert.Contains($"\"{kind}\"", Spec, StringComparison.Ordinal);
        foreach (var outcome in SessionFiles.ClimaxOutcomes)
            Assert.Contains($"\"{outcome}\"", Spec, StringComparison.Ordinal);
    }

    // The first fenced block is what a reader copies. If the app refuses it, the document has
    // taught them the wrong thing.
    [Fact]
    public void the_spec_example_is_a_session_the_app_would_accept()
    {
        var block = FirstJsonBlock().Match(Spec);
        Assert.True(block.Success, "the spec has no complete example object");

        Assert.Empty(SessionFiles.ValidateSession(JsonNode.Parse(block.Groups[1].Value)!.AsObject()));
    }
}
