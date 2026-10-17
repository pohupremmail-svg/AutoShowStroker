using Gooner.Core.Settings;

namespace Gooner.Core.App;

// Whether the window is full screen: it is, the first time, and after that it is whatever the user
// left it as. Remembered the moment they switch rather than at quit, so a crash cannot lose it.
public sealed class WindowState : ISettingsOwner
{
    public static string SettingsGroup => "Window";

    public static IReadOnlyDictionary<string, object> Defaults { get; } = new Dictionary<string, object>
    {
        ["fullscreen"] = true,
    };

    private readonly ISettingsStore? _settings;

    public WindowState(ISettingsStore? settings)
    {
        _settings = settings;
        var fallback = (bool)Defaults["fullscreen"];
        Fullscreen = settings?.GetBool($"{SettingsGroup}/fullscreen", fallback) ?? fallback;
    }

    public bool Fullscreen { get; private set; }

    public void Remember(bool fullscreen)
    {
        if (fullscreen == Fullscreen) return;
        Fullscreen = fullscreen;
        _settings?.SetValue($"{SettingsGroup}/fullscreen", fullscreen);
    }
}
