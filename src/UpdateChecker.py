import json

from PyQt6.QtCore import QObject, QUrl, pyqtSignal
from PyQt6.QtNetwork import QNetworkAccessManager, QNetworkReply, QNetworkRequest

from src import changelog


class UpdateChecker(QObject):
    """The app's one and only network code path - deliberately isolated in its own module
    so "what does this app send over the network" has one obvious place to audit. Only ever
    contacted when GoonerApp.check_for_updates() is explicitly triggered by the user."""

    GITHUB_RELEASES_API_URL = "https://api.github.com/repos/pohupremmail-svg/AutoShowStroker/releases/latest"

    update_available = pyqtSignal(str, str)  # latest_tag, release_html_url
    up_to_date = pyqtSignal()
    check_failed = pyqtSignal(str)  # human-readable error message

    def __init__(self, current_version: str, manager: QNetworkAccessManager | None = None, parent=None):
        super().__init__(parent)
        self.current_version = current_version
        self._manager = manager if manager is not None else QNetworkAccessManager(self)

    def check_now(self):
        request = QNetworkRequest(QUrl(self.GITHUB_RELEASES_API_URL))
        # GitHub's API rejects requests with no User-Agent.
        request.setRawHeader(b"User-Agent", b"GoonerApp-UpdateChecker")
        request.setTransferTimeout(10000)
        reply = self._manager.get(request)
        reply.finished.connect(lambda: self._handle_reply(reply))

    def _handle_reply(self, reply):
        reply.deleteLater()
        if reply.error() != QNetworkReply.NetworkError.NoError:
            self.check_failed.emit(reply.errorString())
            return
        self._process_body(bytes(reply.readAll()))

    def _process_body(self, body: bytes):
        try:
            data = json.loads(body.decode("utf-8"))
            tag = data["tag_name"]
            release_url = data.get("html_url", "")
            latest = changelog.parse_version(tag.lstrip("v"))
            current = changelog.parse_version(self.current_version)
        except (ValueError, KeyError, UnicodeDecodeError, json.JSONDecodeError):
            self.check_failed.emit("Couldn't understand GitHub's response.")
            return
        if latest > current:
            self.update_available.emit(tag, release_url)
        else:
            self.up_to_date.emit()
