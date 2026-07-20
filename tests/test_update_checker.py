from PyQt6.QtCore import QObject, pyqtSignal
from PyQt6.QtNetwork import QNetworkReply

from src.UpdateChecker import UpdateChecker


class _FakeReply(QObject):
    finished = pyqtSignal()

    def __init__(self, body=b"", error=QNetworkReply.NetworkError.NoError, error_string=""):
        super().__init__()
        self._body = body
        self._error = error
        self._error_string = error_string

    def error(self):
        return self._error

    def errorString(self):
        return self._error_string

    def readAll(self):
        return self._body

    def deleteLater(self):
        pass


class _FakeManager:
    def __init__(self, reply):
        self._reply = reply
        self.last_request = None

    def get(self, request):
        self.last_request = request
        return self._reply


def make_checker(current_version="0.1.0"):
    return UpdateChecker(current_version)


def test_process_body_emits_update_available_when_remote_is_newer():
    checker = make_checker("0.1.0")
    captured = {}
    checker.update_available.connect(lambda tag, url: captured.update(tag=tag, url=url))

    checker._process_body(b'{"tag_name": "v0.2.0", "html_url": "https://example.com/release"}')

    assert captured == {"tag": "v0.2.0", "url": "https://example.com/release"}


def test_process_body_emits_up_to_date_when_remote_is_same():
    checker = make_checker("0.2.0")
    called = {}
    checker.up_to_date.connect(lambda: called.setdefault("called", True))

    checker._process_body(b'{"tag_name": "v0.2.0"}')

    assert called.get("called") is True


def test_process_body_emits_up_to_date_when_remote_is_older():
    checker = make_checker("0.5.0")
    called = {}
    checker.up_to_date.connect(lambda: called.setdefault("called", True))

    checker._process_body(b'{"tag_name": "v0.2.0"}')

    assert called.get("called") is True


def test_process_body_strips_v_prefix_from_tag():
    checker = make_checker("0.1.0")
    captured = {}
    checker.update_available.connect(lambda tag, url: captured.update(tag=tag))

    checker._process_body(b'{"tag_name": "v9.9.9"}')

    assert captured["tag"] == "v9.9.9"  # emitted tag keeps the "v" for display


def test_process_body_emits_check_failed_on_malformed_json():
    checker = make_checker("0.1.0")
    message = {}
    checker.check_failed.connect(lambda msg: message.setdefault("text", msg))

    checker._process_body(b"not json")

    assert "text" in message


def test_process_body_emits_check_failed_on_missing_tag_name():
    checker = make_checker("0.1.0")
    message = {}
    checker.check_failed.connect(lambda msg: message.setdefault("text", msg))

    checker._process_body(b'{"html_url": "https://example.com"}')

    assert "text" in message


def test_process_body_emits_check_failed_on_non_numeric_version_segment():
    checker = make_checker("0.1.0")
    message = {}
    checker.check_failed.connect(lambda msg: message.setdefault("text", msg))

    checker._process_body(b'{"tag_name": "v0.2.0-beta"}')

    assert "text" in message


def test_check_now_sends_request_to_releases_api_and_wires_reply():
    reply = _FakeReply(body=b'{"tag_name": "v99.0.0", "html_url": "https://example.com/release"}')
    manager = _FakeManager(reply)
    checker = UpdateChecker("0.1.0", manager=manager)

    captured = {}
    checker.update_available.connect(lambda tag, url: captured.update(tag=tag, url=url))

    checker.check_now()
    reply.finished.emit()

    assert manager.last_request.url().toString() == UpdateChecker.GITHUB_RELEASES_API_URL
    assert captured == {"tag": "v99.0.0", "url": "https://example.com/release"}


def test_check_now_emits_check_failed_on_reply_error():
    reply = _FakeReply(error=QNetworkReply.NetworkError.HostNotFoundError, error_string="Host not found")
    manager = _FakeManager(reply)
    checker = UpdateChecker("0.1.0", manager=manager)

    message = {}
    checker.check_failed.connect(lambda msg: message.setdefault("text", msg))

    checker.check_now()
    reply.finished.emit()

    assert message["text"] == "Host not found"
