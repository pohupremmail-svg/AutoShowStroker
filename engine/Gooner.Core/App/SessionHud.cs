using Gooner.Core.Formatting;
using Gooner.Core.Scoring;
using Gooner.Core.Settings;
using Gooner.Core.Timing;

namespace Gooner.Core.App;

// What the record chase needs to know - ScoreTracker, or a double in a test.
public interface IRecordChaseSource
{
    Dictionary<string, double> GetAllTimeBests();

    (string Metric, double Current, double Best)? RecordChaseStatus(IReadOnlyDictionary<string, double> bestValues);

    IReadOnlyDictionary<string, double> LiveMetrics();
}

// The captions that float over the media while a session runs: the callout being said, the
// personal-record chase, the session clock. Port of src/SessionHudWidget.py as text - the engine
// draws each one, and nothing for a caption that is null.
//
// It owns whether a session is running, which is what removed an ordering trap on the Python side:
// the flag and the thing it guards are set by the same call.
public sealed class SessionHud : ISettingsOwner
{
    // Always stored under GoonerApp/, and they stay there, so no installation has to migrate.
    public static string SettingsGroup => "GoonerApp";

    public const int TickMs = 1000;

    public static IReadOnlyDictionary<string, object> Defaults { get; } = new Dictionary<string, object>
    {
        ["show_record_chase"] = true,
        ["show_session_timer"] = true,
    };

    private readonly IRecordChaseSource _score;
    private bool _running;
    // The bests as they stood when this session began: the chase is against the record you walked
    // in with - read live, you would be chasing yourself the moment you beat it.
    private IReadOnlyDictionary<string, double> _sessionStartBests = new Dictionary<string, double>();

    public SessionHud(IRecordChaseSource score, ISettingsStore? settings, ClockScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(scheduler);
        _score = score;
        ShowRecordChase = Read(settings, "show_record_chase");
        ShowSessionTimer = Read(settings, "show_session_timer");
        ClockTimer = scheduler.CreateTimer(RefreshClock);
    }

    private static bool Read(ISettingsStore? settings, string name)
    {
        var fallback = (bool)Defaults[name];
        return settings?.GetBool($"{SettingsGroup}/{name}", fallback) ?? fallback;
    }

    public bool ShowRecordChase { get; set; }
    public bool ShowSessionTimer { get; set; }

    public string? CalloutText { get; private set; }
    public string? RecordChaseText { get; private set; }
    public string? SessionTimerText { get; private set; }

    public ClockTimer ClockTimer { get; }

    // --- session lifecycle ---

    public void SessionStarted()
    {
        _running = true;
        _sessionStartBests = _score.GetAllTimeBests();
        ClockTimer.Start(TickMs);
        Refresh();
    }

    public void SessionEnded()
    {
        _running = false;
        ClockTimer.Stop();
        RecordChaseText = null;
        SessionTimerText = null;
    }

    // --- the captions ---

    // Re-evaluates both. Safe outside a session, which is what the settings dialog does after every
    // save: without the guard, saving put a frozen clock back up counting from the previous start.
    public void Refresh()
    {
        RefreshRecordChase();
        RefreshClock();
    }

    public void RefreshRecordChase()
    {
        if (!_running || !ShowRecordChase || _score.RecordChaseStatus(_sessionStartBests) is not { } status)
        {
            RecordChaseText = null;
            return;
        }
        var (metric, current, best) = status;
        var label = ScoreTracker.PrMetricLabels[metric];
        var currentText = ScoreTracker.FormatMetricValue(metric, current);
        RecordChaseText = current >= best
            ? $"\U0001f3c6 New {label} Record! {currentText}"
            : $"\U0001f3c6 Closing in on your {label} record: {currentText} / {ScoreTracker.FormatMetricValue(metric, best)}";
    }

    public void RefreshClock()
    {
        if (!_running || !ShowSessionTimer)
        {
            SessionTimerText = null;
            return;
        }
        var elapsed = _score.LiveMetrics().GetValueOrDefault("total_dur_sec", 0);
        SessionTimerText = $"⏱ {Clock.FormatClock(elapsed)}";
    }

    // --- what the app is saying ---

    public void ShowTease(string tease) => CalloutText = tease;

    public void HideTease() => CalloutText = null;
}
