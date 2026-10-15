using System.Text.Json.Nodes;

namespace Gooner.Parity.Tests;

// src/ClimaxHandler.py vs Gooner.Core.Climax.ClimaxHandler, together with the beat it steers:
// whole sessions on one clock and one stream of dice, every event compared with its moment - the
// climax drawn, held back, ruined or denied, fakes rolled and revealed, edges pushing it back,
// settings changed on the way, replays with and without a recorded climax.
public class ClimaxHandlerParity
{
    private const string Module = "climax_handler";

    public static IEnumerable<object[]> Sessions() => Golden.Cases(Module, "sessions");

    [Theory]
    [MemberData(nameof(Sessions))]
    public void session(string name, string json)
    {
        var c = JsonNode.Parse(json)!;
        var actual = BeatHandlerParity.Play(
            c["config"]!.AsObject(), c["steps"]!.AsArray(), c["draws"]!.AsArray(), c["climax_config"]!.AsObject());
        BeatHandlerParity.Compare(name, c["expected"]!.AsArray(), actual);
    }
}
