using Gooner.Core.Timing;

namespace Gooner.Core.App;

// The outcome in large letters over the note track - "CUM", "RUINED", "DENIED" - blinking between
// two shades of its colour. Port of GoonerApp's climax status label; the engine draws Text on Color,
// and nothing while Text is null.
public sealed class ClimaxBanner
{
    public const int BlinkIntervalMs = 100;

    // In the colour scheme in use when it is asked.
    public static IReadOnlyDictionary<string, (string On, string Off)> StatusColors =>
        new Dictionary<string, (string On, string Off)>
        {
            ["cum"] = (Palette.Accent, Palette.AccentHover),
            ["ruined"] = (Palette.Ruined, Palette.RuinedDim),
            ["denied"] = (Palette.Denied, Palette.DeniedDim),
        };

    private (string On, string Off) _colors;
    private bool _blinkOn;

    public ClimaxBanner(ClockScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        BlinkTimer = scheduler.CreateTimer(ToggleBlink);
    }

    public string? Text { get; private set; }

    public string? Color { get; private set; }

    public ClockTimer BlinkTimer { get; }

    // A ClimaxHandler status: one of the three outcomes, or anything else ("neutral") to clear it.
    public void Update(string status)
    {
        if (!StatusColors.TryGetValue(status, out var colors))
        {
            BlinkTimer.Stop();
            Text = null;
            Color = null;
            return;
        }
        Text = status.ToUpperInvariant();
        _colors = colors;
        _blinkOn = true;
        Color = colors.On;
        BlinkTimer.Start(BlinkIntervalMs);
    }

    // Only the colour blinks; the text never clears.
    public void ToggleBlink()
    {
        _blinkOn = !_blinkOn;
        Color = _blinkOn ? _colors.On : _colors.Off;
    }

    // Stops the blink but keeps the last outcome up in its bright colour - on Stop, where the result
    // should stay readable rather than disappear or flash forever.
    public void Freeze()
    {
        BlinkTimer.Stop();
        if (Text is not null) Color = _colors.On;
    }
}
