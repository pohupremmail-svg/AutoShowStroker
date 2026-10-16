using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Gooner.Core.Logging;

namespace Gooner.Core.Devices;

// Seconds on a clock that never jumps - what a "be there by then" deadline is measured on, on both
// sides of the worker.
public static class Monotonic
{
    public static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
}

// Voids queued motion the moment it is called, before the worker has heard of it: a target handed
// over a moment before a stop must not still arrive a moment after it. An atomic counter, read on
// one thread and bumped on another - never a lock, so nothing can wait on it while a device moves.
internal sealed class MotionGate
{
    private long _epoch;

    public void Invalidate() => Interlocked.Increment(ref _epoch);

    public long Current => Interlocked.Read(ref _epoch);

    public bool IsCurrent(long epoch) => Current == epoch;
}

// The Buttplug v3 connection: port of _IntifaceWorker in src/plugins/intiface/controller.py.
//
// An actor rather than a QThread: every step - a command from the window, a message from the
// server, a timer firing - is queued and run one at a time on its own loop, so its state is only
// ever touched by one thing at once, exactly as on the Qt thread. What it tells the window goes
// through the callbacks it was given, which post to wherever the window wants them.
//
// This half knows sockets and positions and nothing about rhythm; BeatSync is the other way round.
internal sealed class IntifaceWorker
{
    public const int ReconnectMs = 2000;

    // A server is free to ask for a heartbeat far faster than it could need one, and every ping
    // books a pending entry; without a floor a MaxPingTime of 1 is a thousand messages a second.
    public const int MinPingMs = 100;
    public const int MaxPingMs = 1000;

    private const int MaxMessageBytes = 1024 * 1024;
    private const string Log = "intiface";

    private static readonly IReadOnlyDictionary<string, string> ExpectedAnswers = new Dictionary<string, string>
    {
        ["RequestServerInfo"] = "ServerInfo",
        ["RequestDeviceList"] = "DeviceList",
    };

    private readonly Channel<Func<Task>> _inbox = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly MotionGate _gate;
    private readonly int _requestTimeoutMs;
    private readonly Action<string> _statusChanged;
    private readonly Action<string> _deviceChanged;
    private readonly Action _finished;
    private readonly Dictionary<long, JsonObject> _devices = [];
    private readonly Dictionary<long, (string Kind, double SentAt)> _pending = [];
    private readonly ActorTimer _retry, _connectTimeout, _moveTimer, _shutdownTimer, _scanTimer, _ping, _watchdog;

    private ClientWebSocket? _socket;
    private int _socketGeneration;
    private Uri? _url;
    private bool _enabled;
    private bool _closing;
    private bool _ready;
    private bool _failing;
    private string _failureStatus = "";
    private (long Index, int Feature, JsonObject Device)? _device;
    private long _nextId;
    private (double Position, double Deadline, long Epoch)? _pendingMove;
    private double _lastMoveAt;
    private long? _shutdownStopId;

    public IntifaceWorker(MotionGate gate, int requestTimeoutMs, Action<string> statusChanged, Action<string> deviceChanged, Action finished)
    {
        _gate = gate;
        _requestTimeoutMs = requestTimeoutMs;
        _statusChanged = statusChanged;
        _deviceChanged = deviceChanged;
        _finished = finished;
        _retry = new ActorTimer(this, Open);
        _connectTimeout = new ActorTimer(this, () => Fail("Intiface connection timed out; retrying"));
        _moveTimer = new ActorTimer(this, FlushMove);
        _shutdownTimer = new ActorTimer(this, FinishShutdown);
        _scanTimer = new ActorTimer(this, async () => await Send("StopScanning").ConfigureAwait(false));
        _ping = new ActorTimer(this, async () => await Send("Ping").ConfigureAwait(false), single: false);
        _watchdog = new ActorTimer(this, CheckTimeouts, single: false);
        _ = Task.Run(RunAsync);
    }

    // --- the loop ---

    public void Post(Func<Task> step) => _inbox.Writer.TryWrite(step);

    public void Post(Action step) => Post(() =>
    {
        step();
        return Task.CompletedTask;
    });

    private async Task RunAsync()
    {
        await foreach (var step in _inbox.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                await step().ConfigureAwait(false);
            }
#pragma warning disable CA1031 // The safety boundary: a step that throws must not end the loop that sends the stop.
            catch (Exception e)
#pragma warning restore CA1031
            {
                AppLog.Error(Log, "Unexpected failure in the device connection: {0}", e.GetType().Name);
                await Fail("Invalid Intiface response; device sync stopped").ConfigureAwait(false);
            }
        }
    }

    // --- commands from the window ---

    public async Task Configure(bool enabled, string url)
    {
        if (_closing) return;
        _enabled = enabled;
        _url = enabled ? new Uri(url) : null;
        _failureStatus = "";
        _retry.Stop();
        await Stop().ConfigureAwait(false);
        // The stop goes out before the connection is replaced.
        await CloseSocket().ConfigureAwait(false);
        ClearConnection();
        if (enabled)
        {
            _retry.Start(100);
            _statusChanged("Connecting to Intiface…");
        }
        else
        {
            AppLog.Info(Log, "Device output disabled; closing the Intiface connection");
            _statusChanged("Intiface disabled");
        }
    }

    public async Task Scan()
    {
        if (!_ready || _closing) return;
        await Send("StartScanning").ConfigureAwait(false);
        _scanTimer.Start(10000);
    }

    public async Task Move(double position, double deadline, long epoch)
    {
        if (_closing || !_ready || _device is null) return;
        _pendingMove = (position, deadline, epoch);
        await FlushMove().ConfigureAwait(false);
    }

    // StopAllDevices rather than StopDeviceCmd: it carries no device index, so a stop still goes out
    // when the device was removed while its hardware works through a Duration it already has.
    public async Task<long?> Stop()
    {
        _pendingMove = null;
        _moveTimer.Stop();
        return _ready ? await Send("StopAllDevices").ConfigureAwait(false) : null;
    }

    public async Task Shutdown()
    {
        if (_closing) return;
        _closing = true;
        _enabled = false;
        _retry.Stop();
        _shutdownStopId = await Stop().ConfigureAwait(false);
        if (_shutdownStopId is null) await FinishShutdown().ConfigureAwait(false);
        else _shutdownTimer.Start(500);
    }

    // --- the connection ---

    private Task Open()
    {
        if (!_enabled || _closing || _url is null) return Task.CompletedTask;
        AbortSocket();
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.Zero;
        var generation = ++_socketGeneration;
        _socket = socket;
        _connectTimeout.Start(_requestTimeoutMs);
        // The address is the user's own and may name a machine on their network, so it is never
        // written to the log - the same rule media folders follow.
        _ = ConnectAsync(socket, generation, _url);
        return Task.CompletedTask;
    }

    private async Task ConnectAsync(ClientWebSocket socket, int generation, Uri url)
    {
        try
        {
            await socket.ConnectAsync(url, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is WebSocketException or HttpRequestException or ObjectDisposedException or InvalidOperationException or OperationCanceledException)
        {
            Post(() => generation == _socketGeneration ? Fail("Intiface unavailable; retrying") : Task.CompletedTask);
            return;
        }
        Post(() => generation == _socketGeneration ? Connected(socket, generation) : Task.CompletedTask);
    }

    private async Task Connected(ClientWebSocket socket, int generation)
    {
        _retry.Stop();
        _failureStatus = "";
        _connectTimeout.Stop();
        _watchdog.Start(100);
        AppLog.Info(Log, "Intiface connection open; negotiating Buttplug v3");
        _ = ReceiveAsync(socket, generation);
        await Send("RequestServerInfo", ("ClientName", "GoonerApp"), ("MessageVersion", 3)).ConfigureAwait(false);
    }

    private async Task ReceiveAsync(ClientWebSocket socket, int generation)
    {
        var buffer = new byte[1 << 16];
        using var message = new MemoryStream();
        try
        {
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer, CancellationToken.None).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) break;
                message.Write(buffer, 0, result.Count);
                if (message.Length > MaxMessageBytes)
                {
                    Post(() => generation == _socketGeneration ? Fail("Invalid Intiface response; device sync stopped") : Task.CompletedTask);
                    return;
                }
                if (!result.EndOfMessage) continue;
                var raw = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                message.SetLength(0);
                Post(() => generation == _socketGeneration ? Receive(raw) : Task.CompletedTask);
            }
        }
        catch (Exception e) when (e is WebSocketException or ObjectDisposedException or InvalidOperationException or OperationCanceledException or IOException)
        {
        }
        Post(() => generation == _socketGeneration ? Disconnected() : Task.CompletedTask);
    }

    private void ClearConnection()
    {
        _gate.Invalidate();
        _ready = false;
        _device = null;
        _devices.Clear();
        _pending.Clear();
        _pendingMove = null;
        _lastMoveAt = 0;
        foreach (var timer in new[] { _connectTimeout, _ping, _watchdog, _moveTimer, _scanTimer }) timer.Stop();
        _deviceChanged("");
    }

    private async Task Disconnected()
    {
        AbortSocket();
        ClearConnection();
        if (_closing)
        {
            await FinishShutdown().ConfigureAwait(false);
        }
        else if (_enabled)
        {
            AppLog.Info(Log, "Intiface connection closed; device sync stopped");
            _statusChanged(_failureStatus.Length > 0 ? _failureStatus : "Disconnected; retrying — device sync stopped");
            _retry.Start(ReconnectMs);
        }
    }

    // Tears the connection down and arranges the retry. The latch keeps a failure raised while
    // failing from starting the teardown over again.
    private async Task Fail(string message)
    {
        if (_failing) return;
        _failing = true;
        try
        {
            _failureStatus = message;
            await Stop().ConfigureAwait(false);
            await CloseSocket().ConfigureAwait(false);
            ClearConnection();
        }
        finally
        {
            _failing = false;
        }
        if (_closing)
        {
            await FinishShutdown().ConfigureAwait(false);
        }
        else if (_enabled)
        {
            AppLog.Warning(Log, "Intiface device output stopped: {0}", message);
            _statusChanged(message);
            _retry.Start(ReconnectMs);
        }
    }

    // A graceful close, so what was just sent - the stop above all - is not dropped by a reset.
    private async Task CloseSocket()
    {
        var socket = _socket;
        _socketGeneration++;
        _socket = null;
        if (socket is null) return;
        if (socket.State == WebSocketState.Open)
        {
            using var limit = new CancellationTokenSource(500);
            try
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", limit.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException or IOException)
            {
            }
        }
        socket.Abort();
        socket.Dispose();
    }

    private void AbortSocket()
    {
        var socket = _socket;
        _socketGeneration++;
        _socket = null;
        socket?.Abort();
        socket?.Dispose();
    }

    private async Task<long?> Send(string kind, params (string Name, JsonNode? Value)[] fields)
    {
        if (_socket is not { State: WebSocketState.Open } socket) return null;
        var id = ++_nextId;
        _pending[id] = (kind, Monotonic.Now);
        var body = new JsonObject { ["Id"] = id };
        foreach (var (name, value) in fields) body[name] = value;
        var text = new JsonArray(new JsonObject { [kind] = body }).ToJsonString();
        try
        {
            await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is WebSocketException or ObjectDisposedException or InvalidOperationException or IOException)
        {
            if (!_failing) Post(() => Fail("Intiface unavailable; retrying"));
        }
        return id;
    }

    private Task CheckTimeouts()
    {
        var now = Monotonic.Now;
        return _pending.Values.Any(p => now - p.SentAt > _requestTimeoutMs / 1000.0)
            ? Fail("Intiface stopped responding; reconnecting — device sync stopped")
            : Task.CompletedTask;
    }

    // --- what the server says ---

    private async Task Receive(string raw)
    {
        try
        {
            await Handle(raw).ConfigureAwait(false);
        }
        catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException or KeyNotFoundException or InvalidCastException or OverflowException or ArgumentException)
        {
            await Fail("Invalid Intiface response; device sync stopped").ConfigureAwait(false);
        }
    }

    private async Task Handle(string raw)
    {
        if (JsonNode.Parse(raw) is not JsonArray messages) throw new FormatException("Expected a message array");
        foreach (var message in messages)
        {
            if (message is not JsonObject envelope || envelope.Count != 1) throw new FormatException("Invalid message envelope");
            var (kind, node) = envelope.First();
            if (node is not JsonObject body) throw new FormatException("Invalid message body");
            var id = body["Id"] is JsonValue idValue && idValue.TryGetValue<long>(out var parsed) ? parsed : body.ContainsKey("Id") ? -1 : 0;
            (string Kind, double SentAt)? request = _pending.Remove(id, out var found) ? found : null;
            if (kind == "Error")
            {
                if (request is { Kind: "StartScanning" or "StopScanning" })
                {
                    _statusChanged("Connected; scan unavailable — scan in Intiface Central");
                    continue;
                }
                await Fail("Intiface rejected a command; device sync stopped").ConfigureAwait(false);
                return;
            }
            if (request is { } sent && kind != ExpectedAnswers.GetValueOrDefault(sent.Kind, "Ok"))
                throw new FormatException("Response does not match request");
            if (_closing)
            {
                if (kind == "Ok" && id == _shutdownStopId) await FinishShutdown().ConfigureAwait(false);
                continue;
            }
            if (kind == "ServerInfo" && request is { Kind: "RequestServerInfo" })
            {
                if (Integer(body["MessageVersion"]) != 3) throw new FormatException("Server did not negotiate Buttplug v3");
                var maxPing = Integer(body["MaxPingTime"]);
                if (maxPing < 0) throw new FormatException("Invalid heartbeat interval");
                _ready = true;
                if (maxPing > 0) _ping.Start((int)Math.Clamp(maxPing / 2, MinPingMs, MaxPingMs));
                await Send("RequestDeviceList").ConfigureAwait(false);
                await Scan().ConfigureAwait(false);
                _statusChanged("Connected; looking for a linear device");
            }
            else if (kind == "DeviceList" && _ready && request is { Kind: "RequestDeviceList" })
            {
                _devices.Clear();
                foreach (var device in body["Devices"]!.AsArray())
                    if (device is JsonObject entry && Index(entry) is { } index) _devices[index] = entry;
                SelectDevice();
            }
            else if (kind == "DeviceAdded" && _ready)
            {
                if (Index(body) is { } index) _devices[index] = body;
                SelectDevice();
            }
            else if (kind == "DeviceRemoved" && _ready)
            {
                if (Index(body) is { } index) _devices.Remove(index);
                SelectDevice();
            }
        }
    }

    // int(x) on the Python side: a number, or text that reads as one.
    private static long Integer(JsonNode? node) => node switch
    {
        JsonValue v when v.GetValueKind() == JsonValueKind.Number => (long)v.GetValue<double>(),
        JsonValue v when v.GetValueKind() == JsonValueKind.String => long.Parse((string)v!, System.Globalization.CultureInfo.InvariantCulture),
        _ => throw new FormatException("not a number"),
    };

    // A device index that is not a whole number is never one a device can be selected by.
    private static long? Index(JsonObject device) =>
        device["DeviceIndex"] is JsonValue v && v.TryGetValue<long>(out var index) && index >= 0 ? index : null;

    private void SelectDevice()
    {
        var available = new List<(long Index, int Feature, JsonObject Device)>();
        foreach (var (index, device) in _devices.OrderBy(d => d.Key))
        {
            var gap = device["DeviceMessageTimingGap"];
            if (gap is not null && !(gap is JsonValue g && g.GetValueKind() == JsonValueKind.Number && g.TryGetValue<long>(out var ms) && ms is >= 0 and <= int.MaxValue))
                throw new FormatException("Invalid device timing gap");
            if ((device["DeviceMessages"] as JsonObject)?["LinearCmd"] is not JsonArray features) continue;
            for (var feature = 0; feature < features.Count; feature++)
            {
                if (features[feature] is not JsonObject) continue;
                available.Add((index, feature, device));
                break;
            }
        }
        // The chosen device stays while it exists: hotplugging another must not redirect motion.
        (long Index, int Feature, JsonObject Device)? chosen = available.Cast<(long, int, JsonObject)?>().FirstOrDefault(d => _device is { } current && d!.Value.Item1 == current.Index)
            ?? (available.Count > 0 ? available[0] : null);
        if (Same(chosen, _device))
        {
            if (chosen is null) _statusChanged("Connected; no linear device found");
            return;
        }
        _gate.Invalidate();
        _pendingMove = null;
        _moveTimer.Stop();
        _device = chosen;
        var name = "";
        if (chosen is { } selected)
        {
            name = selected.Device["DeviceDisplayName"] is JsonValue display && display.GetValueKind() == JsonValueKind.String && ((string)display!).Length > 0
                ? (string)display!
                : selected.Device["DeviceName"]?.ToString() ?? "Linear device";
        }
        // The model is left out on purpose: it is on screen in the Device tab, and a diagnostic log
        // is not the place to write down what someone owns.
        AppLog.Info(Log, "Linear device {0}", chosen is null ? "lost" : "selected");
        _deviceChanged(name);
        _statusChanged(chosen is null ? "Connected; no linear device found" : "Connected");
    }

    private static bool Same((long Index, int Feature, JsonObject Device)? a, (long Index, int Feature, JsonObject Device)? b) =>
        (a, b) switch
        {
            (null, null) => true,
            ({ } x, { } y) => x.Index == y.Index && x.Feature == y.Feature && x.Device.ToJsonString() == y.Device.ToJsonString(),
            _ => false,
        };

    // --- motion ---

    private async Task FlushMove()
    {
        if (_pendingMove is not { } move || _device is not { } device) return;
        var now = Monotonic.Now;
        var gap = device.Device["DeviceMessageTimingGap"] is JsonValue g && g.TryGetValue<long>(out var ms) ? Math.Max(0, ms) / 1000.0 : 0.0;
        if (!_gate.IsCurrent(move.Epoch) || move.Deadline <= now)
        {
            _pendingMove = null;
            return;
        }
        var wait = _lastMoveAt + gap - now;
        if (wait > 0)
        {
            _moveTimer.Start(Math.Max(1, (int)Math.Ceiling(wait * 1000)));
            return;
        }
        var duration = (long)Math.Clamp((move.Deadline - now) * 1000, 1, uint.MaxValue);
        _pendingMove = null;
        // Before the write, not after: a send that fails comes back through Fail, and this must
        // already be true by then.
        _lastMoveAt = now;
        await Send("LinearCmd",
            ("DeviceIndex", device.Index),
            ("Vectors", new JsonArray(new JsonObject { ["Index"] = device.Feature, ["Duration"] = duration, ["Position"] = move.Position }))).ConfigureAwait(false);
    }

    // --- shutting down ---

    private async Task FinishShutdown()
    {
        _shutdownTimer.Stop();
        _retry.Stop();
        ClearConnection();
        await CloseSocket().ConfigureAwait(false);
        _inbox.Writer.TryComplete();
        _finished();
    }

    // A QTimer on the actor: it fires as a step of the loop, and once stopped, a tick already on its
    // way is dropped rather than run. Its Timer lives only between Start and Stop, and shutting the
    // worker down stops every one, so there is nothing left to dispose.
#pragma warning disable CA1001
    private sealed class ActorTimer(IntifaceWorker worker, Func<Task> callback, bool single = true)
#pragma warning restore CA1001
    {
        private Timer? _timer;
        private int _generation;

        public void Start(int intervalMs)
        {
            Stop();
            var generation = _generation;
            _timer = new Timer(_ => worker.Post(async () =>
            {
                if (generation != _generation) return;
                if (single) Stop();
                await callback().ConfigureAwait(false);
            }), null, intervalMs, single ? Timeout.Infinite : intervalMs);
        }

        public void Stop()
        {
            _generation++;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
