"""Protocol tests use a local fake server, never Intiface or physical hardware."""
import json
import time

import pytest
from PyQt6.QtCore import QThread
from PyQt6.QtNetwork import QHostAddress
from PyQt6.QtWebSockets import QWebSocketServer

from src.IntifaceController import IntifaceController


def linear_device(index=7, name="Test Linear"):
    return {
        "DeviceIndex": index, "DeviceName": name,
        "DeviceMessages": {"LinearCmd": [{"ActuatorType": "Linear", "StepCount": 100}]},
    }


@pytest.fixture
def server(qtbot):
    server = QWebSocketServer("Test Intiface", QWebSocketServer.SslMode.NonSecureMode)
    assert server.listen(QHostAddress.SpecialAddress.LocalHost, 0)
    server.messages = []
    server.clients = []
    server.devices = [linear_device()]
    server.max_ping_time = 200
    server.respond = True

    def accept():
        socket = server.nextPendingConnection()
        server.clients.append(socket)

        def received(raw):
            for message in json.loads(raw):
                server.messages.append(message)
                kind, body = next(iter(message.items()))
                if not server.respond:
                    continue
                if kind == "RequestServerInfo":
                    answer = {"ServerInfo": {
                        "Id": body["Id"], "MessageVersion": 3,
                        "ServerName": "Test", "MaxPingTime": server.max_ping_time,
                    }}
                elif kind == "RequestDeviceList":
                    answer = {"DeviceList": {"Id": body["Id"], "Devices": server.devices}}
                else:
                    answer = {"Ok": {"Id": body["Id"]}}
                socket.sendTextMessage(json.dumps([answer]))

        socket.textMessageReceived.connect(received)

    server.newConnection.connect(accept)
    yield server
    for client in server.clients:
        client.abort()
    server.close()


@pytest.fixture
def controller(qsettings, qtbot):
    controller = IntifaceController(qsettings)
    yield controller
    controller.shutdown()
    qtbot.waitUntil(lambda: not controller.has_worker, timeout=3000)


def connect(controller, server, qtbot):
    controller.configure(True, f"ws://127.0.0.1:{server.serverPort()}", 0.15, 0.85)
    qtbot.waitUntil(lambda: bool(controller.device_name), timeout=3000)


def commands(server, kind):
    return [message[kind] for message in server.messages if kind in message]


def test_disabled_by_default_never_starts_a_worker(controller):
    assert not controller.enabled
    assert not controller.has_worker


@pytest.mark.parametrize("url,low,high", [
    ("https://localhost", 0.1, 0.9), ("ws://", 0.1, 0.9),
    ("ws://localhost:99999", 0.1, 0.9), ("ws://localhost", -0.1, 0.9),
    ("ws://localhost", 0.9, 0.1), ("ws://localhost", 0.5, 0.5),
    ("ws://localhost", float("nan"), 0.9),
])
def test_invalid_configuration_does_not_enable_networking(controller, url, low, high):
    with pytest.raises(ValueError):
        controller.configure(True, url, low, high)
    assert not controller.has_worker
    assert not controller.enabled


def test_handshake_scan_and_linear_selection_run_off_ui_thread(controller, server, qtbot):
    server.devices.insert(0, {"DeviceIndex": 0, "DeviceName": "Not linear", "DeviceMessages": {}})
    connect(controller, server, qtbot)
    assert controller.device_name == "Test Linear"
    assert controller._worker.thread() != QThread.currentThread()
    assert commands(server, "RequestServerInfo")[0]["MessageVersion"] == 3
    qtbot.waitUntil(lambda: bool(commands(server, "StartScanning")))
    qtbot.waitUntil(lambda: bool(commands(server, "Ping")))


@pytest.mark.parametrize("feature", [
    {"ActuatorType": "Position", "StepCount": 100},
    {"ActuatorType": None, "StepCount": 100},
    {"StepCount": 100},
], ids=["alternate-actuator-label", "null-actuator-label", "omitted-actuator-label"])
def test_linear_capability_accepts_simulated_device_metadata(controller, server, qtbot, feature):
    # LinearCmd is the capability. Simulated devices need not repeat "Linear" in
    # ActuatorType; filtering by that label prevented them from being selected.
    server.devices[0]["DeviceMessages"]["LinearCmd"] = [feature]
    connect(controller, server, qtbot)
    controller.test_up()
    qtbot.waitUntil(lambda: bool(commands(server, "LinearCmd")))
    move = commands(server, "LinearCmd")[-1]
    assert move["DeviceIndex"] == 7
    assert move["Vectors"][0]["Index"] == 0
    assert move["Vectors"][0]["Position"] == 0.85


def test_predictive_target_uses_remaining_deadline_and_configured_range(controller, server, qtbot):
    connect(controller, server, qtbot)
    controller.session_started()
    controller.on_movement_planned(1, True, time.monotonic() + 0.5)
    qtbot.waitUntil(lambda: bool(commands(server, "LinearCmd")))
    move = commands(server, "LinearCmd")[-1]
    assert move["DeviceIndex"] == 7
    vector = move["Vectors"][0]
    assert vector["Index"] == 0
    assert vector["Position"] == 0.85
    assert 1 <= vector["Duration"] <= 500
    controller.on_movement_planned(2, False, time.monotonic() + 0.5)
    qtbot.waitUntil(lambda: len(commands(server, "LinearCmd")) == 2)
    assert commands(server, "LinearCmd")[-1]["Vectors"][0]["Position"] == 0.15


def test_expired_and_duplicate_predictions_are_not_sent(controller, server, qtbot):
    connect(controller, server, qtbot)
    controller.session_started()
    controller.on_movement_planned(1, True, time.monotonic() - 1)
    deadline = time.monotonic() + 0.5
    controller.on_movement_planned(2, False, deadline)
    controller.on_movement_planned(2, False, deadline)
    qtbot.waitUntil(lambda: len(commands(server, "LinearCmd")) == 1)
    qtbot.wait(80)
    assert len(commands(server, "LinearCmd")) == 1


def test_emergency_stop_stays_latched_until_explicit_resume(controller, server, qtbot):
    connect(controller, server, qtbot)
    controller.session_started()
    controller.emergency_stop()
    qtbot.waitUntil(lambda: bool(commands(server, "StopDeviceCmd")))
    controller.on_movement_planned(1, True, time.monotonic() + 1)
    qtbot.wait(80)
    assert not commands(server, "LinearCmd")
    assert not controller.armed
    controller.resume_sync()
    controller.on_movement_planned(2, False, time.monotonic() + 1)
    qtbot.waitUntil(lambda: bool(commands(server, "LinearCmd")))


def test_pause_stops_but_next_segment_can_resume(controller, server, qtbot):
    connect(controller, server, qtbot)
    controller.session_started()
    controller.pause()
    qtbot.waitUntil(lambda: bool(commands(server, "StopDeviceCmd")))
    controller.on_movement_planned(1, True, time.monotonic() + 1)
    qtbot.wait(50)
    assert not commands(server, "LinearCmd")
    controller.pause_ended()
    controller.on_movement_planned(2, True, time.monotonic() + 1)
    qtbot.waitUntil(lambda: bool(commands(server, "LinearCmd")))


def test_test_move_is_bounded_and_does_not_resume_session_sync(controller, server, qtbot):
    connect(controller, server, qtbot)
    controller.session_started()
    controller.test_up()
    qtbot.waitUntil(lambda: bool(commands(server, "LinearCmd")))
    assert commands(server, "LinearCmd")[-1]["Vectors"][0]["Position"] == 0.85
    assert not controller.armed
    controller.test_down()
    qtbot.waitUntil(lambda: len(commands(server, "LinearCmd")) == 2)
    assert commands(server, "LinearCmd")[-1]["Vectors"][0]["Position"] == 0.15


def test_device_removal_discards_motion_and_requires_resume(controller, server, qtbot):
    connect(controller, server, qtbot)
    controller.session_started()
    server.clients[-1].sendTextMessage(json.dumps([{"DeviceRemoved": {"Id": 0, "DeviceIndex": 7}}]))
    qtbot.waitUntil(lambda: not controller.device_name)
    controller.on_movement_planned(1, True, time.monotonic() + 1)
    server.clients[-1].sendTextMessage(json.dumps([{"DeviceAdded": {"Id": 0, **linear_device(9)}}]))
    qtbot.waitUntil(lambda: bool(controller.device_name))
    assert not controller.armed
    assert not commands(server, "LinearCmd")


def test_reconnect_does_not_replay_or_resume_motion(controller, server, qtbot):
    connect(controller, server, qtbot)
    controller.session_started()
    server.clients[-1].abort()
    qtbot.waitUntil(lambda: not controller.device_name)
    controller.on_movement_planned(1, True, time.monotonic() + 10)
    qtbot.waitUntil(lambda: len(server.clients) == 2 and bool(controller.device_name), timeout=5000)
    assert not controller.armed
    assert not commands(server, "LinearCmd")


def test_disable_stops_and_does_not_reconnect(controller, server, qtbot):
    connect(controller, server, qtbot)
    controller.configure(False, controller.server_url, 0.15, 0.85)
    qtbot.waitUntil(lambda: bool(commands(server, "StopDeviceCmd")))
    qtbot.waitUntil(lambda: not controller.device_name)
    assert not controller.enabled


def test_shutdown_sends_stop_before_worker_finishes(controller, server, qtbot):
    connect(controller, server, qtbot)
    controller.shutdown()
    qtbot.waitUntil(lambda: not controller.has_worker)
    assert commands(server, "StopDeviceCmd")


def test_stop_cancels_movement_waiting_for_device_timing_gap(controller, server, qtbot):
    server.devices[0]["DeviceMessageTimingGap"] = 300
    connect(controller, server, qtbot)
    controller.session_started()
    controller.on_movement_planned(1, True, time.monotonic() + 1)
    qtbot.waitUntil(lambda: len(commands(server, "LinearCmd")) == 1)
    controller.on_movement_planned(2, False, time.monotonic() + 1)
    controller.emergency_stop()
    qtbot.waitUntil(lambda: bool(commands(server, "StopDeviceCmd")))
    qtbot.wait(350)
    assert len(commands(server, "LinearCmd")) == 1


def test_device_timing_gap_coalesces_to_latest_unexpired_target(controller, server, qtbot):
    server.devices[0]["DeviceMessageTimingGap"] = 200
    connect(controller, server, qtbot)
    controller.session_started()
    controller.on_movement_planned(1, True, time.monotonic() + 1)
    qtbot.waitUntil(lambda: len(commands(server, "LinearCmd")) == 1)
    controller.on_movement_planned(2, False, time.monotonic() + 0.03)
    controller.on_movement_planned(3, True, time.monotonic() + 1)
    qtbot.waitUntil(lambda: len(commands(server, "LinearCmd")) == 2)
    assert commands(server, "LinearCmd")[-1]["Vectors"][0]["Position"] == 0.85


def test_unanswered_commands_disconnect_and_disarm(controller, server, qtbot, monkeypatch):
    monkeypatch.setattr("src.IntifaceController.REQUEST_TIMEOUT_MS", 300)
    connect(controller, server, qtbot)
    controller.session_started()
    server.respond = False
    controller.on_movement_planned(1, True, time.monotonic() + 1)
    qtbot.waitUntil(lambda: not controller.device_name, timeout=2000)
    assert not controller.armed
    assert "stopped responding" in controller.status


def test_malformed_server_message_stops_without_a_slot_exception(controller, server, qtbot):
    connect(controller, server, qtbot)
    controller.session_started()
    server.clients[-1].sendTextMessage('{broken')
    qtbot.waitUntil(lambda: not controller.device_name)
    assert not controller.armed
    assert commands(server, "StopDeviceCmd")


def test_unsupported_devices_never_receive_motion(controller, server, qtbot):
    server.devices = [{"DeviceIndex": 0, "DeviceName": "Not linear", "DeviceMessages": {"ScalarCmd": [{}]}}]
    controller.configure(True, f"ws://127.0.0.1:{server.serverPort()}", 0.1, 0.9)
    qtbot.waitUntil(lambda: "no linear device" in controller.status)
    controller.session_started()
    controller.test_up()
    qtbot.wait(50)
    assert not commands(server, "LinearCmd")


def test_window_close_waits_for_device_shutdown_without_blocking(app, server, qtbot):
    connect(app.intiface_controller, server, qtbot)
    app.show()
    app.close()
    qtbot.waitUntil(lambda: not app.intiface_controller.has_worker, timeout=3000)
    qtbot.waitUntil(lambda: not app.isVisible())
    assert commands(server, "StopDeviceCmd")


def test_invalid_device_gap_is_rejected_without_a_slot_exception(controller, server, qtbot):
    server.devices[0]["DeviceMessageTimingGap"] = "invalid"
    controller.configure(True, f"ws://127.0.0.1:{server.serverPort()}", 0.1, 0.9)
    qtbot.waitUntil(lambda: "Invalid Intiface response" in controller.status, timeout=1500)
    assert not controller.device_name
