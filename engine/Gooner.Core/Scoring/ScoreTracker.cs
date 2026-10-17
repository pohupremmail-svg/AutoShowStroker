using System.Globalization;
using System.Text.Json.Nodes;
using Gooner.Core.Data;
using Gooner.Core.Formatting;
using Gooner.Core.Localization;
using Gooner.Core.Logging;
using Gooner.Core.Sessions;

namespace Gooner.Core.Scoring;

// Session stats, the session history behind them, and personal-record detection. Port of
// src/ScoreTracker.py.
//
// The session as delivered (DeliverInfos) and every history entry are JsonObjects, as they are
// dicts in Python: achievements read both the same way, and an entry written by an older build
// simply lacks the newer keys. Time comes from the TimeProvider, so a test moves a clock where
// Python monkeypatches time.time.
public sealed class ScoreTracker : App.IRecordChaseSource
{
    private const string Log = "ScoreTracker";
    private const string HistoryName = "session_history";

    // Metrics tracked as personal records across sessions - the single source of truth for the
    // recap's "New Record!" cards and the all-time-bests summary.
    public static readonly IReadOnlyList<string> PrMetrics =
        ["total_dur_sec", "total_num_beat", "average_beat_speed_active", "fakeout_count"];

    // Written into every history entry alongside the record metrics. Not records themselves -
    // nobody chases a personal best in being denied - but achievements read them back across
    // sessions, which only works if they were stored at the time.
    public static readonly IReadOnlyList<string> HistoryExtraFields =
        ["climax_outcome", "reported_outcome", "fakeouts_fallen_for", "edge_count", "was_replay"];

    public static readonly IReadOnlyDictionary<string, string> PrMetricLabels = new Dictionary<string, string>
    {
        ["total_dur_sec"] = L10n.N("Total Duration"),
        ["total_num_beat"] = L10n.N("Total Beats"),
        ["average_beat_speed_active"] = L10n.N("Active Beat Speed"),
        ["fakeout_count"] = L10n.N("Fakeouts Survived"),
    };

    public static readonly IReadOnlyDictionary<string, string> PrMetricUnits = new Dictionary<string, string>
    {
        ["total_dur_sec"] = L10n.N("s"),
        ["total_num_beat"] = L10n.N("beats"),
        ["average_beat_speed_active"] = L10n.N("beats/sec"),
        ["fakeout_count"] = L10n.N("fakeouts"),
    };

    // Bounded because the long-term statistics render every entry into a table, not because the
    // storage minds.
    public const int MaxHistoryEntries = 5000;

    // The live record chase only reveals itself once a metric is at least this close
    // (current/best) to its personal best - an anticipation/payoff moment, not a permanent HUD.
    public const double ChaseRevealThreshold = 0.8;

    // average_beat_speed_active is left out of the live chase: early in a session its small
    // active-time denominator makes instantaneous speed spike well above the eventual average.
    public static readonly IReadOnlyList<string> LiveChaseMetrics =
        [.. PrMetrics.Where(m => m != "average_beat_speed_active")];

    private readonly UserDataStore? _dataStore;
    private readonly TimeProvider _time;
    private readonly OrderedDictionary<string, int> _patterns = [];

    public ScoreTracker(UserDataStore? dataStore = null, TimeProvider? time = null)
    {
        _dataStore = dataStore;
        _time = time ?? TimeProvider.System;
        History = LoadHistory();
    }

    public int NumberOfPauses { get; internal set; }
    public double TotalDurationOfPauses { get; internal set; }
    public int BeatCount { get; internal set; }
    public int NumberOfBeatChanges { get; private set; }
    public double? CurPauseStartTime { get; private set; }
    public double? SessionStartTime { get; internal set; }
    public double? TotalRunTime { get; private set; }
    public double? AveragePauseDuration { get; private set; }
    public double? AverageBeatSpeed { get; private set; }
    public double? AverageBeatSpeedActive { get; private set; }
    public IReadOnlyDictionary<string, int> Patterns => _patterns;
    public int Skips { get; internal set; }
    public int Repeats { get; private set; }
    public string? ClimaxOutcome { get; private set; }
    public string? ReportedOutcome { get; private set; }
    public int FakeoutCount { get; internal set; }
    public int FakeoutsFallenFor { get; private set; }
    public int EdgeCount { get; private set; }
    public bool WasReplay { get; private set; }

    // The records the session just ended broke, each with the best it beat.
    public IReadOnlyDictionary<string, double> LastSessionNewRecords { get; private set; } = new Dictionary<string, double>();

    internal List<JsonObject> History { get; set; }

    private double Now => (_time.GetUtcNow() - DateTimeOffset.UnixEpoch).TotalSeconds;

    public static string FormatMetricValue(string metric, double value) => metric switch
    {
        "total_dur_sec" => Clock.FormatDuration(value),
        "average_beat_speed_active" => L10n.F("{0} beats/sec", value.ToString("F2", CultureInfo.InvariantCulture)),
        // Python prints its int as is; counts are whole numbers here, which print the same.
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    public void ClimaxDecided(string? outcome) => ClimaxOutcome = outcome;

    // What the user says actually happened: came / ruined / stopped. Kept strictly apart from
    // ClimaxOutcome, which is what the app *demanded*. A denial the user obeyed and a denial they
    // ignored are the same announcement and opposite sessions, and that difference only exists if
    // both are written down. Null stays null: an unanswered session is not a guess.
    public void OutcomeReported(string? reported) => ReportedOutcome = reported;

    public void FellForFakeClimax() => FakeoutsFallenFor++;

    public void EdgeReached() => EdgeCount++;

    // This session is a saved one being played again. Set after SessionStarted(), which clears
    // it - a replay is otherwise indistinguishable in every number.
    public void ReplayStarted() => WasReplay = true;

    public void BeatPaused()
    {
        AppLog.Info(Log, "Beat paused");
        NumberOfPauses++;
        // Python tests the start time for truthiness, so a pause that began at exactly 0.0 counts
        // as no pause at all - kept, because a clock reading 0.0 never happens outside a test.
        if (CurPauseStartTime is null or 0.0)
            CurPauseStartTime = Now;
        else
            AppLog.Warning(Log, "Pause start time was not reset - pause bookkeeping may be off");
    }

    public void BeatResumed()
    {
        var start = CurPauseStartTime ?? throw new InvalidOperationException("The beat resumed without having paused.");
        var pauseDuration = Now - start;
        AppLog.Info(Log, "Beat resumed after {0}s", Math.Round(pauseDuration, MidpointRounding.ToEven));
        TotalDurationOfPauses += pauseDuration;
        CurPauseStartTime = null;
    }

    public void MediaSkipped() => Skips++;

    public void MediaRepeated() => Repeats++;

    public void SessionStarted()
    {
        AppLog.Info(Log, "Score tracking started");
        SessionStartTime = Now;
        NumberOfPauses = 0;
        TotalDurationOfPauses = 0;
        CurPauseStartTime = null;
        TotalRunTime = null;
        AveragePauseDuration = null;
        Skips = 0;
        Repeats = 0;
        BeatCount = 0;
        NumberOfBeatChanges = 0;
        _patterns.Clear();
        ClimaxOutcome = null;
        ReportedOutcome = null;
        FakeoutCount = 0;
        FakeoutsFallenFor = 0;
        EdgeCount = 0;
        WasReplay = false;
    }

    public void SessionEnded()
    {
        AppLog.Info(Log, "Score tracking ended");
        // Null if this fires without a matching SessionStarted - closing the window ends whatever
        // it finds running. Treated as a zero-length session rather than throwing out of the close.
        SessionStartTime ??= Now;
        var runTime = Now - SessionStartTime.Value;
        TotalRunTime = runTime;
        AveragePauseDuration = NumberOfPauses > 0 ? TotalDurationOfPauses / NumberOfPauses : null;
        AverageBeatSpeed = runTime > 0 ? BeatCount / runTime : 0;

        var activeTime = runTime - TotalDurationOfPauses;
        AverageBeatSpeedActive = activeTime > 0 ? BeatCount / activeTime : 0;

        RecordHistoryAndDetectPrs();
    }

    public JsonObject DeliverInfos() => new()
    {
        ["total_dur_sec"] = TotalRunTime,
        ["pause_dur_sec"] = TotalDurationOfPauses,
        ["average_pause_dur_sec"] = AveragePauseDuration,
        ["total_num_pauses"] = NumberOfPauses,
        ["total_num_beat"] = BeatCount,
        ["total_num_beat_change"] = NumberOfBeatChanges,
        ["average_beat_speed"] = AverageBeatSpeed,
        ["average_beat_speed_active"] = AverageBeatSpeedActive,
        ["most_used_pattern"] = FindFavPattern(),
        ["skips"] = Skips,
        ["repeats"] = Repeats,
        ["climax_outcome"] = ClimaxOutcome,
        ["reported_outcome"] = ReportedOutcome,
        ["fakeout_count"] = FakeoutCount,
        ["fakeouts_fallen_for"] = FakeoutsFallenFor,
        ["edge_count"] = EdgeCount,
        ["was_replay"] = WasReplay,
    };

    public void BeatChanged(double freq, string pattern)
    {
        _ = freq;
        NumberOfBeatChanges++;
        _patterns[pattern] = _patterns.GetValueOrDefault(pattern) + 1;
    }

    public void Beat() => BeatCount++;

    public void FakeClimaxTriggered() => FakeoutCount++;

    // The most used pattern; a tie goes to the one played first.
    internal string? FindFavPattern()
    {
        var maxCount = 0;
        string? favourite = null;
        foreach (var (pattern, count) in _patterns)
        {
            if (count > maxCount)
            {
                maxCount = count;
                favourite = pattern;
            }
        }
        return favourite;
    }

    // Empty until a session has started.
    public IReadOnlyDictionary<string, double> LiveMetrics()
    {
        if (SessionStartTime is not { } start) return new Dictionary<string, double>();
        var elapsed = Now - start;
        var activeTime = elapsed - TotalDurationOfPauses;
        return new Dictionary<string, double>
        {
            ["total_dur_sec"] = elapsed,
            ["total_num_beat"] = BeatCount,
            ["average_beat_speed_active"] = activeTime > 0 ? BeatCount / activeTime : 0,
            ["fakeout_count"] = FakeoutCount,
        };
    }

    // The metric closest to (or furthest past) its personal best, once it is close enough to be
    // worth showing; null otherwise. A best that is missing or not above zero takes no part.
    public (string Metric, double Current, double Best)? RecordChaseStatus(IReadOnlyDictionary<string, double> bestValues)
    {
        ArgumentNullException.ThrowIfNull(bestValues);
        var live = LiveMetrics();
        if (live.Count == 0) return null;
        (string Metric, double Current, double Best, double Progress)? bestMatch = null;
        foreach (var metric in LiveChaseMetrics)
        {
            if (!bestValues.TryGetValue(metric, out var best) || best <= 0) continue;
            var current = live[metric];
            var progress = current / best;
            if (bestMatch is null || progress > bestMatch.Value.Progress)
                bestMatch = (metric, current, best, progress);
        }
        if (bestMatch is not { } match || match.Progress < ChaseRevealThreshold) return null;
        return (match.Metric, match.Current, match.Best);
    }

    // Forgets every recorded session. In memory too, or the next session end would write the old
    // list straight back out.
    public void ClearHistory()
    {
        History = [];
        _dataStore?.Delete(HistoryName);
    }

    public IReadOnlyList<JsonObject> GetHistory() => [.. History];

    public Dictionary<string, double> GetAllTimeBests() => ComputeBests(History);

    // The best value of every record metric across the history. Only numbers take part: Python
    // would compare whatever a hand-edited file holds and raise on a string, this skips it.
    internal static Dictionary<string, double> ComputeBests(IEnumerable<JsonObject> history)
    {
        var bests = new Dictionary<string, double>();
        foreach (var entry in history)
        {
            foreach (var metric in PrMetrics)
            {
                if (!PyNumber(entry[metric], out var value)) continue;
                if (!bests.TryGetValue(metric, out var best) || value > best)
                    bests[metric] = value;
            }
        }
        return bests;
    }

    private static bool PyNumber(JsonNode? node, out double value)
    {
        value = 0;
        if (!PyJson.IsNumber(node)) return false;
        value = PyJson.Number(node);
        return true;
    }

    private void RecordHistoryAndDetectPrs()
    {
        var info = DeliverInfos();
        var previousBests = ComputeBests(History);

        LastSessionNewRecords = PrMetrics
            .Where(metric => previousBests.ContainsKey(metric) && PyNumber(info[metric], out var current) && current > previousBests[metric])
            .ToDictionary(metric => metric, metric => previousBests[metric]);

        var entry = new JsonObject
        {
            ["ended_at"] = _time.GetLocalNow().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        };
        foreach (var field in PrMetrics.Concat(HistoryExtraFields))
            entry[field] = info[field]?.DeepClone();
        History.Add(entry);
        if (History.Count > MaxHistoryEntries)
            History.RemoveRange(0, History.Count - MaxHistoryEntries);
        SaveHistory();
    }

    private List<JsonObject> LoadHistory()
    {
        if (_dataStore?.Load(HistoryName) is not JsonArray stored) return [];
        return [.. stored.OfType<JsonObject>().Select(e => e.DeepClone().AsObject())];
    }

    private void SaveHistory() =>
        _dataStore?.Save(HistoryName, new JsonArray([.. History.Select(e => (JsonNode)e.DeepClone())]));
}
