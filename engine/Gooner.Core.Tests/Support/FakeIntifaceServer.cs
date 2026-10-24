using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Gooner.Core.Tests.Support;

// A Buttplug v3 server on loopback, answering as the Python suite's QWebSocketServer fixture did:
// ServerInfo to RequestServerInfo, its device list to RequestDeviceList, Ok to everything else -
// unless told to stay silent. Never Intiface, never hardware. The WebSocket handshake is done by
// hand over a TcpListener, so no HTTP stack or URL reservation is involved.
internal sealed class FakeIntifaceServer : IAsyncDisposable
{
    private const string Magic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<JsonObject> _messages = [];
    private readonly List<(WebSocket Socket, SemaphoreSlim Sending)> _clients = [];
    private readonly Lock _lock = new();
    private readonly Task _accepting;

    public FakeIntifaceServer()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _accepting = AcceptAsync();
    }

    public int Port { get; }

    public string Url => $"ws://127.0.0.1:{Port}";

    public List<JsonObject> Offered { get; } = [LinearDevice()];

    public int PingInterval { get; set; } = 200;

    public volatile bool Respond = true;

    public int ClientCount
    {
        get
        {
            lock (_lock) return _clients.Count;
        }
    }

    public static JsonObject LinearDevice(int index = 7, string name = "Test Linear") => new()
    {
        ["DeviceIndex"] = index,
        ["DeviceName"] = name,
        ["DeviceMessages"] = new JsonObject { ["LinearCmd"] = new JsonArray(new JsonObject { ["ActuatorType"] = "Linear", ["StepCount"] = 100 }) },
    };

    // The bodies of every message of that kind received so far, in order.
    public List<JsonObject> Commands(string kind)
    {
        lock (_lock) return [.. _messages.Where(m => m.ContainsKey(kind)).Select(m => m[kind]!.AsObject())];
    }

    public async Task SendToLatest(string raw)
    {
        (WebSocket Socket, SemaphoreSlim Sending) client;
        lock (_lock) client = _clients[^1];
        await client.Sending.WaitAsync();
        try
        {
            await client.Socket.SendAsync(Encoding.UTF8.GetBytes(raw), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        finally
        {
            client.Sending.Release();
        }
    }

    public void AbortLatest()
    {
        lock (_lock) _clients[^1].Socket.Abort();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient tcp;
            try
            {
                tcp = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }
            _ = ServeAsync(tcp);
        }
    }

    private async Task ServeAsync(TcpClient tcp)
    {
        var stream = tcp.GetStream();
        var request = new StringBuilder();
        var buffer = new byte[1];
        while (!request.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            if (await stream.ReadAsync(buffer) == 0) return;
            request.Append((char)buffer[0]);
        }
        var key = request.ToString().Split("\r\n")
            .First(line => line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))[18..].Trim();
        // RFC 6455 fixes the handshake to SHA-1; nothing here is protected by it.
#pragma warning disable CA5350
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + Magic)));
#pragma warning restore CA5350
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            $"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"));
        var socket = WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null, keepAliveInterval: TimeSpan.Zero);
        var sending = new SemaphoreSlim(1);
        lock (_lock) _clients.Add((socket, sending));
        var received = new byte[1 << 16];
        var text = new MemoryStream();
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(received, _stop.Token);
                if (result.MessageType == WebSocketMessageType.Close) return;
                text.Write(received, 0, result.Count);
                if (!result.EndOfMessage) continue;
                var raw = Encoding.UTF8.GetString(text.ToArray());
                text.SetLength(0);
                foreach (var message in JsonNode.Parse(raw)!.AsArray())
                {
                    var envelope = message!.AsObject();
                    lock (_lock) _messages.Add(envelope);
                    if (!Respond) continue;
                    var (kind, body) = envelope.First();
                    var id = body!["Id"]!.DeepClone();
                    JsonObject answer = kind switch
                    {
                        "RequestServerInfo" => new() { ["ServerInfo"] = new JsonObject { ["Id"] = id, ["MessageVersion"] = 3, ["ServerName"] = "Test", ["MaxPingTime"] = PingInterval } },
                        "RequestDeviceList" => new() { ["DeviceList"] = new JsonObject { ["Id"] = id, ["Devices"] = new JsonArray([.. Offered.Select(d => d.DeepClone())]) } },
                        _ => new() { ["Ok"] = new JsonObject { ["Id"] = id } },
                    };
                    await sending.WaitAsync();
                    try
                    {
                        await socket.SendAsync(Encoding.UTF8.GetBytes(new JsonArray(answer).ToJsonString()), WebSocketMessageType.Text, true, _stop.Token);
                    }
                    finally
                    {
                        sending.Release();
                    }
                }
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException or ObjectDisposedException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        lock (_lock)
            foreach (var (socket, _) in _clients) socket.Abort();
        try
        {
            await _accepting;
        }
        catch (OperationCanceledException)
        {
        }
        _stop.Dispose();
    }
}
