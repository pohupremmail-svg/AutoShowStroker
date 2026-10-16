using System.Globalization;
using Gooner.Core.Data;
using Gooner.Core.Sessions;
using Gooner.Core.Simulation;

// dotnet run --project engine/Gooner.Harness -- [options]
//
//   --seed N             the dice (default: a random one, printed so the run can be repeated)
//   --minutes M          stop after M minutes if no climax ended it first (default 60)
//   --climax A:B         place the climax A to B seconds in, not waiting for the ramp
//   --no-climax          a session without one
//   --edge-at S          press "I reached my edge" S seconds in (repeatable)
//   --replay FILE        replay a saved session
//   --save FILE          save the session just played, as the app's Save would
//   --show-callouts      print what is said; without it only that something was said
//
// Uses the default settings and touches no data directory: nothing is read from or written to
// the app's own folder, so a run here can never change what the app remembers.

var options = Args.Parse(args);
if (options is null)
{
    Console.Error.WriteLine("Unknown option. See the comment at the top of engine/Gooner.Harness/Program.cs.");
    return 2;
}

var seed = options.Seed ?? Random.Shared.Next();
var session = new HeadlessSession(new HeadlessOptions
{
    Seed = seed,
    CalloutDir = Args.FindCallouts(),
    ClimaxActive = options.NoClimax ? false : null,
    MinClimaxAfter = options.Climax?.Min,
    MaxClimaxAfter = options.Climax?.Max,
    ClimaxOnlyAfterRamp = options.Climax is null ? null : false,
});

SessionScript? script = null;
if (options.Replay is { } replayPath)
{
    try
    {
        var saved = SessionFiles.ReadSessionFile(replayPath);
        Console.WriteLine($"Replaying: {SessionFiles.Describe(saved)}");
        script = new SessionScript(saved);
    }
    catch (UnsupportedSessionFileException e)
    {
        Console.Error.WriteLine(e.Message);
        return 1;
    }
}

Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Seed {seed}"));
session.Start(script);
var printed = 0;
void Print()
{
    for (; printed < session.Events.Count; printed++)
    {
        var e = session.Events[printed];
        var detail = e.What == "callout" && !options.ShowCallouts ? "(something is said)" : e.Detail;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{Clock(e.At),8}  {e.What,-8} {detail}"));
    }
}

foreach (var at in options.EdgesAt.Order())
{
    session.AdvanceTo(at);
    Print();
    if (session.Ended) break;
    if (session.EdgeRelief() == 0)
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{Clock(at),8}  edge     refused (mid-pause or after the climax)"));
}
session.RunFor(options.Minutes * 60);
Print();

var info = session.Score.DeliverInfos();
Console.WriteLine();
Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
    $"{Clock((double?)info["total_dur_sec"] ?? 0)} played, {info["total_num_beat"]} beats, {info["total_num_pauses"]} pauses, {info["fakeout_count"]} fake-outs"));
Console.WriteLine($"Favourite rhythm: {info["most_used_pattern"]}, climax: {info["climax_outcome"]?.ToString() ?? "none"}");
Console.WriteLine($"Achievements: {string.Join(", ", session.Unlocked!.Select(a => a.Id).DefaultIfEmpty("none"))}");

if (options.Save is { } savePath)
{
    var saved = session.Save();
    Console.WriteLine(SessionFiles.WriteSessionFile(savePath, saved) ? $"Saved: {SessionFiles.Describe(saved)}" : "Could not save the session.");
}
return 0;

static string Clock(double seconds) =>
    TimeSpan.FromSeconds(seconds).ToString(seconds >= 3600 ? @"h\:mm\:ss\.f" : @"mm\:ss\.f", CultureInfo.InvariantCulture);

internal sealed record Args(int? Seed, double Minutes, (double Min, double Max)? Climax, bool NoClimax,
    List<double> EdgesAt, string? Replay, string? Save, bool ShowCallouts)
{
    public static Args? Parse(string[] args)
    {
        var parsed = new Args(null, 60, null, false, [], null, null, false);
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException(args[i]);
            double Number(string text) => double.Parse(text, CultureInfo.InvariantCulture);
            try
            {
                switch (args[i])
                {
                    case "--seed": parsed = parsed with { Seed = int.Parse(Next(), CultureInfo.InvariantCulture) }; break;
                    case "--minutes": parsed = parsed with { Minutes = Number(Next()) }; break;
                    case "--climax":
                        var range = Next().Split(':');
                        parsed = parsed with { Climax = (Number(range[0]), Number(range[^1])) };
                        break;
                    case "--no-climax": parsed = parsed with { NoClimax = true }; break;
                    case "--edge-at": parsed.EdgesAt.Add(Number(Next())); break;
                    case "--replay": parsed = parsed with { Replay = Next() }; break;
                    case "--save": parsed = parsed with { Save = Next() }; break;
                    case "--show-callouts": parsed = parsed with { ShowCallouts = true }; break;
                    default: return null;
                }
            }
            catch (Exception e) when (e is FormatException or ArgumentException or IndexOutOfRangeException)
            {
                return null;
            }
        }
        return parsed;
    }

    // res/callouts, found by walking up from wherever the harness runs - the same files the app reads.
    public static string? FindCallouts() =>
        ResourceRoot.Find([Environment.CurrentDirectory, AppContext.BaseDirectory]) is { } res
            ? Path.Combine(res, "callouts")
            : null;
}
