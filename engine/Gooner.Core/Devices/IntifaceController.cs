using Gooner.Core.Logging;
using Gooner.Core.Settings;

namespace Gooner.Core.Devices;

// Port of IntifaceController in src/plugins/intiface/controller.py: the window's side of the device
// connection - what it is set to, what it last heard, and the safety latch. The network is only
// reached through the worker, and the rhythm not at all: BeatSync calls OnTarget.
//
// Device output is never restored from the settings. It starts off at every launch and has to be
// turned on deliberately, so a box ticked once months ago can never quietly open a connection on
// some later start. The address and the stroke range are remembered.
public sealed class IntifaceController : ISettingsOwner
{
    public static string SettingsGroup => "IntifaceController";

    public const int RequestTimeoutMs = 3000;

    private const string Log = "intiface";

    // No address ships with the app. Intiface Central shows the one its own server listens on,
    // which is the only one that can be right for a given machine - and guessing on the user's
    // behalf would bake a network address into a program whose promise is that it contacts none.
    public static IReadOnlyDictionary<string, object> Defaults { get; } = new Dictionary<string, object>
    {
        ["server_url"] = "",
        ["min_position"] = 0.25,
        ["max_position"] = 0.75,
    };

    private readonly ISettingsStore? _settings;
    private readonly Action<Action> _post;
    private readonly int _requestTimeoutMs;
    private readonly MotionGate _gate = new();
    private IntifaceWorker? _worker;
    private bool _shuttingDown;

    // `post` is how what the worker says reaches the window's thread; inline when not given.
    public IntifaceController(ISettingsStore? settings, Action<Action>? post = null, int requestTimeoutMs = RequestTimeoutMs)
    {
        _settings = settings;
        _post = post ?? (action => action());
        _requestTimeoutMs = requestTimeoutMs;
        try
        {
            var url = settings?.GetString(Key("server_url"), ServerUrl) ?? ServerUrl;
            var low = settings?.GetDouble(Key("min_position"), MinPosition) ?? MinPosition;
            var high = settings?.GetDouble(Key("max_position"), MaxPosition) ?? MaxPosition;
            ValidatePositions(low, high);
            if (url.Length > 0) ValidateUrl(url);
            (ServerUrl, MinPosition, MaxPosition) = (url, low, high);
        }
        catch (ArgumentException)
        {
            Status = "Invalid saved device settings; Intiface disabled";
            AppLog.Warning(Log, "Invalid saved Intiface settings; starting with device output disabled");
        }
    }

    private static string Key(string name) => $"{SettingsGroup}/{name}";

    public bool Enabled { get; private set; }

    public string ServerUrl { get; private set; } = (string)Defaults["server_url"];

    public double MinPosition { get; private set; } = (double)Defaults["min_position"];

    public double MaxPosition { get; private set; } = (double)Defaults["max_position"];

    public string Status { get; private set; } = "Intiface disabled";

    public string DeviceName { get; private set; } = "";

    public bool HasWorker => _worker is not null;

    public event Action? StateChanged;

    public event Action? ShutdownFinished;

    // Finding the first device is not a resume - it is the connection finally arriving - while
    // losing one latches device sync off until the user says otherwise.
    public event Action? DeviceFound;

    public event Action? DeviceLost;

    public static void ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("ws" or "wss")
            || string.IsNullOrEmpty(uri.Host)
            || uri.Fragment.Length > 0
            || uri.UserInfo.Length > 0)
            throw new ArgumentException("Enter the WebSocket address Intiface Central shows for its server (it starts with ws:// or wss://).");
    }

    public static void ValidatePositions(double low, double high)
    {
        if (!(double.IsFinite(low) && double.IsFinite(high) && 0 <= low && low < high && high <= 1))
            throw new ArgumentException("Stroke positions must satisfy 0% ≤ minimum < maximum ≤ 100%.");
    }

    // Applies and stores the connection settings. The address only has to be a real one when the
    // feature is being switched on: a half-typed address in a tab nobody is using must not block
    // the Save button of every other setting.
    public void Configure(bool enabled, string url, double low, double high)
    {
        ArgumentNullException.ThrowIfNull(url);
        url = url.Trim();
        ValidatePositions(low, high);
        if (enabled) ValidateUrl(url);
        if (_shuttingDown) return;
        CancelMotion();
        (Enabled, ServerUrl, MinPosition, MaxPosition) = (enabled, url, low, high);
        _settings?.SetValue(Key("server_url"), ServerUrl);
        _settings?.SetValue(Key("min_position"), MinPosition);
        _settings?.SetValue(Key("max_position"), MaxPosition);
        if (enabled) EnsureWorker();
        if (_worker is { } worker) worker.Post(() => worker.Configure(enabled, url));
        else Status = "Intiface disabled";
        StateChanged?.Invoke();
    }

    // Privacy & Data's "delete all settings": a live connection left up until the next restart is
    // not what deleting them is supposed to mean.
    public void DisconnectAndForget() =>
        Configure(false, "", (double)Defaults["min_position"], (double)Defaults["max_position"]);

    public void CancelMotion()
    {
        _gate.Invalidate();
        if (_worker is { } worker) worker.Post(async () => await worker.Stop().ConfigureAwait(false));
    }

    // The next audible note, handed over by BeatSync: up or down, and when it lands on the
    // monotonic clock. Which position that is is this half's business; which note it is is none of it.
    public void OnTarget(bool up, double deadline) => QueueMove(up ? MaxPosition : MinPosition, deadline);

    private void QueueMove(double position, double deadline)
    {
        if (!Enabled || DeviceName.Length == 0 || _shuttingDown || _worker is not { } worker) return;
        var epoch = _gate.Current;
        worker.Post(() => worker.Move(position, deadline, epoch));
    }

    public void MoveTo(double position, double durationMs)
    {
        if (!double.IsFinite(position) || position < MinPosition || position > MaxPosition)
            throw new ArgumentException("Position is outside the configured stroke range");
        if (!double.IsFinite(durationMs) || durationMs < 1 || durationMs > uint.MaxValue)
            throw new ArgumentException("Movement duration must be a positive number of milliseconds");
        QueueMove(position, Monotonic.Now + durationMs / 1000);
    }

    public void TestUp()
    {
        CancelMotion();
        MoveTo(MaxPosition, 1000);
    }

    public void TestDown()
    {
        CancelMotion();
        MoveTo(MinPosition, 1000);
    }

    public void Scan()
    {
        if (_worker is { } worker && Enabled) worker.Post(worker.Scan);
    }

    // The stop goes out first; ShutdownFinished says when the connection is gone.
    public void Shutdown()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        CancelMotion();
        if (_worker is { } worker) worker.Post(worker.Shutdown);
    }

    private void EnsureWorker()
    {
        if (_worker is not null) return;
        _worker = new IntifaceWorker(_gate, _requestTimeoutMs,
            status => _post(() => OnStatus(status)),
            device => _post(() => OnDevice(device)),
            () => _post(OnWorkerFinished));
    }

    private void OnStatus(string status)
    {
        Status = status;
        StateChanged?.Invoke();
    }

    internal void OnDevice(string name)
    {
        var hadDevice = DeviceName.Length > 0;
        DeviceName = name;
        _gate.Invalidate();
        if (name.Length > 0 && !hadDevice) DeviceFound?.Invoke();
        else if (hadDevice && name.Length == 0) DeviceLost?.Invoke();
        StateChanged?.Invoke();
    }

    private void OnWorkerFinished()
    {
        _worker = null;
        ShutdownFinished?.Invoke();
    }

    // For the arming policy's tests: what the controller believes about itself, without a socket.
    internal void Pretend(bool enabled, string deviceName)
    {
        Enabled = enabled;
        DeviceName = deviceName;
    }
}
