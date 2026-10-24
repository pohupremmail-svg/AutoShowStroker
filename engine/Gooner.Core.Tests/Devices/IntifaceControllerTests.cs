using System.Text.Json.Nodes;
using Gooner.Core.Data;
using Gooner.Core.Devices;
using Gooner.Core.Logging;
using Gooner.Core.Settings;
using Gooner.Core.Tests.Support;

namespace Gooner.Core.Tests.Devices;

// Port of tests/test_plugin_intiface.py: the Buttplug v3 connection, against a fake server on
// loopback - never Intiface, never hardware. The tests about Qt's own reentrancy (a lock held
// across a send, a close that re-enters the failure handler) have nothing to hold here: the
// worker is an actor, one step at a time, and the motion gate is an atomic counter, not a lock.
// The server is disposed in DisposeAsync, which xUnit calls through IAsyncLifetime.
#pragma warning disable CA1001
public sealed class IntifaceControllerTests : IAsyncLifetime
#pragma warning restore CA1001
{
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-intiface-");
    private readonly FakeIntifaceServer _server = new();
    private readonly JsonSettingsStore _settings;
    private IntifaceController? _controller;

    public IntifaceControllerTests() => _settings = new JsonSettingsStore(new UserDataStore(_tmp.FullName));

    private IntifaceController Controller => _controller ??= new IntifaceController(_settings);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_controller is not null)
        {
            _controller.Shutdown();
            await Eventually(() => !_controller.HasWorker);
        }
        await _server.DisposeAsync();
        AppLog.Configure(false, null);
        _tmp.Delete(recursive: true);
    }

    private static async Task Eventually(Func<bool> condition, int timeoutMs = 3000)
    {
        var until = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > until) throw new TimeoutException("the condition never held");
            await Task.Delay(10);
        }
    }

    private async Task Connect(IntifaceController? controller = null)
    {
        controller ??= Controller;
        controller.Configure(true, _server.Url, 0.15, 0.85);
        await Eventually(() => controller.DeviceName.Length > 0);
    }

    private List<JsonObject> Commands(string kind) => _server.Commands(kind);

    private static double Now => Monotonic.Now;

    [Fact]
    public void disabled_by_default_never_starts_a_worker()
    {
        Assert.False(Controller.Enabled);
        Assert.False(Controller.HasWorker);
    }

    [Theory]
    [InlineData("https://localhost", 0.1, 0.9)]
    [InlineData("ws://", 0.1, 0.9)]
    [InlineData("ws://localhost:99999", 0.1, 0.9)]
    [InlineData("ws://localhost", -0.1, 0.9)]
    [InlineData("ws://localhost", 0.9, 0.1)]
    [InlineData("ws://localhost", 0.5, 0.5)]
    [InlineData("ws://localhost", double.NaN, 0.9)]
    [InlineData("ws://user@localhost", 0.1, 0.9)]
    [InlineData("ws://localhost/#part", 0.1, 0.9)]
    public void invalid_configuration_does_not_enable_networking(string url, double low, double high)
    {
        Assert.ThrowsAny<ArgumentException>(() => Controller.Configure(true, url, low, high));

        Assert.False(Controller.HasWorker);
        Assert.False(Controller.Enabled);
    }

    // The address only has to be a real one when the feature is being switched on: a half-typed one
    // in a tab nobody is using must not block every other setting.
    [Fact]
    public void a_half_typed_address_is_fine_while_output_stays_off()
    {
        Controller.Configure(false, "ws://half", 0.1, 0.9);

        Assert.False(Controller.HasWorker);
        Assert.Equal("Intiface disabled", Controller.Status);
    }

    [Fact]
    public async Task handshake_scan_and_linear_selection()
    {
        _server.Offered.Insert(0, new JsonObject { ["DeviceIndex"] = 0, ["DeviceName"] = "Not linear", ["DeviceMessages"] = new JsonObject() });

        await Connect();

        Assert.Equal("Test Linear", Controller.DeviceName);
        Assert.Equal(3, (int)Commands("RequestServerInfo")[0]["MessageVersion"]!);
        Assert.Equal("GoonerApp", (string)Commands("RequestServerInfo")[0]["ClientName"]!);
        await Eventually(() => Commands("StartScanning").Count > 0);
        await Eventually(() => Commands("Ping").Count > 0);
    }

    // LinearCmd is the capability. A simulated device need not repeat "Linear" in its actuator
    // type; filtering by that label kept it from being selected.
    [Theory]
    [InlineData("""{"ActuatorType": "Position", "StepCount": 100}""")]
    [InlineData("""{"ActuatorType": null, "StepCount": 100}""")]
    [InlineData("""{"StepCount": 100}""")]
    public async Task linear_capability_accepts_simulated_device_metadata(string feature)
    {
        _server.Offered[0]["DeviceMessages"]!["LinearCmd"] = new JsonArray(JsonNode.Parse(feature));
        await Connect();

        Controller.TestUp();

        await Eventually(() => Commands("LinearCmd").Count > 0);
        var move = Commands("LinearCmd")[^1];
        Assert.Equal(7, (int)move["DeviceIndex"]!);
        Assert.Equal(0, (int)move["Vectors"]![0]!["Index"]!);
        Assert.Equal(0.85, (double)move["Vectors"]![0]!["Position"]!);
    }

    [Fact]
    public async Task predictive_target_uses_remaining_deadline_and_configured_range()
    {
        await Connect();

        Controller.OnTarget(true, Now + 0.5);

        await Eventually(() => Commands("LinearCmd").Count > 0);
        var vector = Commands("LinearCmd")[^1]["Vectors"]![0]!;
        Assert.Equal(7, (int)Commands("LinearCmd")[^1]["DeviceIndex"]!);
        Assert.Equal(0.85, (double)vector["Position"]!);
        Assert.InRange((long)vector["Duration"]!, 1, 500);

        Controller.OnTarget(false, Now + 0.5);
        await Eventually(() => Commands("LinearCmd").Count == 2);
        Assert.Equal(0.15, (double)Commands("LinearCmd")[^1]["Vectors"]![0]!["Position"]!);
    }

    // Its whole meaning is "be there by then". Once then has passed, sending it would only make the
    // device travel to an endpoint the beat has moved on from.
    [Fact]
    public async Task a_target_whose_note_has_already_landed_is_never_sent()
    {
        await Connect();

        Controller.OnTarget(true, Now - 1);
        await Task.Delay(80);
        Assert.Empty(Commands("LinearCmd"));

        Controller.OnTarget(false, Now + 0.5);
        await Eventually(() => Commands("LinearCmd").Count == 1);
    }

    // The epoch gate exists for the gap between the two threads: a target handed over a moment
    // before the stop must not still arrive a moment after it.
    [Fact]
    public async Task cancelling_motion_stops_the_device_and_voids_what_was_queued()
    {
        await Connect();
        _server.Offered[0]["DeviceMessageTimingGap"] = 0;

        Controller.OnTarget(true, Now + 1);
        Controller.CancelMotion();

        await Eventually(() => Commands("StopAllDevices").Count > 0);
        await Task.Delay(80);
        Assert.Empty(Commands("LinearCmd"));
    }

    [Fact]
    public async Task a_test_move_uses_the_configured_endpoints()
    {
        await Connect();

        Controller.TestUp();
        await Eventually(() => Commands("LinearCmd").Count > 0);
        Assert.Equal(0.85, (double)Commands("LinearCmd")[^1]["Vectors"]![0]!["Position"]!);

        Controller.TestDown();
        await Eventually(() => Commands("LinearCmd").Count == 2);
        Assert.Equal(0.15, (double)Commands("LinearCmd")[^1]["Vectors"]![0]!["Position"]!);
    }

    [Fact]
    public async Task a_movement_queued_for_a_lost_device_never_reaches_its_replacement()
    {
        await Connect();
        await _server.SendToLatest("""[{"DeviceRemoved": {"Id": 0, "DeviceIndex": 7}}]""");
        await Eventually(() => Controller.DeviceName.Length == 0);

        Controller.OnTarget(true, Now + 1);
        var added = FakeIntifaceServer.LinearDevice(9);
        added["Id"] = 0;
        await _server.SendToLatest(new JsonArray(new JsonObject { ["DeviceAdded"] = added }).ToJsonString());
        await Eventually(() => Controller.DeviceName.Length > 0);
        await Task.Delay(80);

        Assert.Empty(Commands("LinearCmd"));
    }

    [Fact]
    public async Task a_reconnect_never_replays_what_the_old_connection_was_holding()
    {
        await Connect();
        _server.AbortLatest();
        await Eventually(() => Controller.DeviceName.Length == 0);

        Controller.OnTarget(true, Now + 10);

        await Eventually(() => _server.ClientCount == 2 && Controller.DeviceName.Length > 0, 5000);
        Assert.Empty(Commands("LinearCmd"));
    }

    [Fact]
    public async Task disable_stops_and_does_not_reconnect()
    {
        await Connect();

        Controller.Configure(false, Controller.ServerUrl, 0.15, 0.85);

        await Eventually(() => Commands("StopAllDevices").Count > 0);
        await Eventually(() => Controller.DeviceName.Length == 0);
        Assert.False(Controller.Enabled);
        await Task.Delay(300);
        Assert.Equal(1, _server.ClientCount);
    }

    [Fact]
    public async Task shutdown_sends_stop_before_worker_finishes()
    {
        await Connect();

        Controller.Shutdown();

        await Eventually(() => !Controller.HasWorker);
        Assert.NotEmpty(Commands("StopAllDevices"));
    }

    [Fact]
    public async Task stop_cancels_movement_waiting_for_device_timing_gap()
    {
        _server.Offered[0]["DeviceMessageTimingGap"] = 300;
        await Connect();
        Controller.OnTarget(true, Now + 1);
        await Eventually(() => Commands("LinearCmd").Count == 1);

        Controller.OnTarget(false, Now + 1);
        Controller.CancelMotion();

        await Eventually(() => Commands("StopAllDevices").Count > 0);
        await Task.Delay(350);
        Assert.Single(Commands("LinearCmd"));
    }

    [Fact]
    public async Task device_timing_gap_coalesces_to_latest_unexpired_target()
    {
        _server.Offered[0]["DeviceMessageTimingGap"] = 200;
        await Connect();
        Controller.OnTarget(true, Now + 1);
        await Eventually(() => Commands("LinearCmd").Count == 1);

        Controller.OnTarget(false, Now + 0.03);
        Controller.OnTarget(true, Now + 1);

        await Eventually(() => Commands("LinearCmd").Count == 2);
        Assert.Equal(0.85, (double)Commands("LinearCmd")[^1]["Vectors"]![0]!["Position"]!);
    }

    [Fact]
    public async Task unanswered_commands_disconnect_and_stop()
    {
        _controller = new IntifaceController(_settings, requestTimeoutMs: 300);
        await Connect();
        _server.Respond = false;

        Controller.OnTarget(true, Now + 1);

        await Eventually(() => Controller.DeviceName.Length == 0, 2000);
        Assert.Contains("stopped responding", Controller.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task malformed_server_message_stops_without_a_slot_exception()
    {
        await Connect();

        await _server.SendToLatest("{broken");

        await Eventually(() => Controller.DeviceName.Length == 0);
        Assert.NotEmpty(Commands("StopAllDevices"));
    }

    [Fact]
    public async Task unsupported_devices_never_receive_motion()
    {
        _server.Offered.Clear();
        _server.Offered.Add(new JsonObject { ["DeviceIndex"] = 0, ["DeviceName"] = "Not linear", ["DeviceMessages"] = new JsonObject { ["ScalarCmd"] = new JsonArray(new JsonObject()) } });
        Controller.Configure(true, _server.Url, 0.1, 0.9);
        await Eventually(() => Controller.Status.Contains("no linear device", StringComparison.Ordinal));

        Controller.TestUp();
        await Task.Delay(50);

        Assert.Empty(Commands("LinearCmd"));
    }

    [Fact]
    public async Task invalid_device_gap_is_rejected_without_a_slot_exception()
    {
        _server.Offered[0]["DeviceMessageTimingGap"] = "invalid";

        Controller.Configure(true, _server.Url, 0.1, 0.9);

        await Eventually(() => Controller.Status.Contains("Invalid Intiface response", StringComparison.Ordinal), 1500);
        Assert.Equal("", Controller.DeviceName);
    }

    // --- the safety boundary: a far end that is broken or hostile ---

    // A message nested deeper than the parser will go stops the connection, as any other bad one
    // does. A process that fell over sends no stop, which would leave a device running.
    [Fact]
    public async Task a_message_that_blows_the_parser_stack_stops_rather_than_killing_the_process()
    {
        await Connect();

        await _server.SendToLatest(new string('[', 5000) + new string(']', 5000));

        await Eventually(() => Controller.DeviceName.Length == 0);
        Assert.NotEmpty(Commands("StopAllDevices"));
        Assert.Contains("Invalid Intiface response", Controller.Status, StringComparison.Ordinal);
    }

    // StopDeviceCmd needs a device index and there no longer is one. Hardware working through a
    // Duration it was already handed is exactly when a stop matters most.
    [Fact]
    public async Task a_stop_still_goes_out_after_the_device_has_been_removed()
    {
        await Connect();
        await _server.SendToLatest("""[{"DeviceRemoved": {"Id": 0, "DeviceIndex": 7}}]""");
        await Eventually(() => Controller.DeviceName.Length == 0);

        Controller.Shutdown();

        await Eventually(() => !Controller.HasWorker);
        Assert.NotEmpty(Commands("StopAllDevices"));
    }

    // A server asking to be pinged every millisecond gets a hundred: each ping books an entry in the
    // pending table, so obeying it literally would be a thousand messages a second.
    [Fact]
    public async Task a_hostile_heartbeat_interval_cannot_make_the_app_spin()
    {
        _server.PingInterval = 1;
        await Connect();

        await Task.Delay(400);

        Assert.InRange(Commands("Ping").Count, 1, 8);
    }

    // The user's address may name a machine on their network, which puts it under the same rule as
    // a media folder path; and what device someone owns is nobody's log entry.
    [Fact]
    public async Task the_server_address_never_reaches_the_log()
    {
        var written = new List<string>();
        AppLog.Sink = new ListSink(written);

        await Connect();
        await _server.SendToLatest("{broken");
        await Eventually(() => Controller.DeviceName.Length == 0);

        var log = string.Join("\n", written);
        Assert.NotEmpty(written);
        Assert.DoesNotContain(_server.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), log);
        Assert.DoesNotContain("127.0.0.1", log);
        Assert.DoesNotContain("Test Linear", log);
    }

    // The first thing most people will do is enable this before starting the server.
    [Fact]
    public async Task connecting_where_nothing_listens_reports_it_and_keeps_the_app_alive()
    {
        var dead = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        dead.Start();
        var port = ((System.Net.IPEndPoint)dead.LocalEndpoint).Port;
        dead.Stop();

        Controller.Configure(true, $"ws://127.0.0.1:{port}", 0.25, 0.75);

        await Eventually(() => Controller.Status.Contains("retrying", StringComparison.Ordinal), 5000);
        await Task.Delay(300);
        Assert.Equal("", Controller.DeviceName);
        Assert.True(Controller.HasWorker);
    }

    // --- what is remembered ---

    // Not persisted on purpose: a box ticked once months ago must not open a network connection on a
    // later start without the user saying so again. The address is remembered - retyping it every
    // time would only make people write it down.
    [Fact]
    public void device_output_is_off_at_every_launch()
    {
        _settings.SetValue("IntifaceController/enabled", true);
        _settings.SetValue("IntifaceController/server_url", "ws://a-machine-the-user-picked:1");

        var fresh = new IntifaceController(_settings);

        Assert.False(fresh.Enabled);
        Assert.False(fresh.HasWorker);
        Assert.Equal("ws://a-machine-the-user-picked:1", fresh.ServerUrl);
    }

    [Fact]
    public void configuring_remembers_the_address_and_the_range()
    {
        Controller.Configure(false, "ws://somewhere:12345", 0.2, 0.8);

        Assert.Equal("ws://somewhere:12345", _settings.GetString("IntifaceController/server_url", ""));
        Assert.Equal(0.2, _settings.GetDouble("IntifaceController/min_position", 0));
        Assert.Equal(0.8, _settings.GetDouble("IntifaceController/max_position", 0));
    }

    [Fact]
    public void broken_saved_settings_start_disabled_with_the_defaults()
    {
        _settings.SetValue("IntifaceController/min_position", 0.9);
        _settings.SetValue("IntifaceController/max_position", 0.1);

        var fresh = new IntifaceController(_settings);

        Assert.Equal("Invalid saved device settings; Intiface disabled", fresh.Status);
        Assert.Equal(0.25, fresh.MinPosition);
        Assert.Equal(0.75, fresh.MaxPosition);
    }

    // No address ships with the app: guessing on the user's behalf would mean baking a network
    // address into a program whose promise is that it contacts none.
    [Fact]
    public void no_address_ships_with_the_app() => Assert.Equal("", (string)IntifaceController.Defaults["server_url"]);

    private sealed class ListSink(List<string> lines) : ILogSink
    {
        public void Write(LogLevel level, string category, string message, object?[] args)
        {
            lock (lines) lines.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, message, args));
        }
    }
}
