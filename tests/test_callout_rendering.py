"""Check rendered pixels: isVisible() alone misses native video windows hiding text."""

import pytest
from PyQt6.QtCore import QPoint, Qt
from PyQt6.QtGui import QColor, QImage
from PyQt6.QtMultimedia import QVideoFrame

from src import theme
from src.VideoDisplay import VideoDisplay


@pytest.mark.parametrize("size", [(1000, 750), (650, 950)])
def test_video_and_callout_render_together(app, qtbot, monkeypatch, size):
    app.resize(*size)
    app.player.setCurrentWidget(app.player.video_widget)
    app.show()
    qtbot.waitUntil(app.isVisible)

    # Use the real player's output, with a synthetic frame instead of a decoder/audio.
    # Match the media area's aspect ratio so the pixel checks avoid letterboxing.
    frame_image = QImage(app.player.video_widget.size(), QImage.Format.Format_RGB32)
    frame_image.fill(QColor("#168040"))
    app.player.media_player.videoSink().setVideoFrame(QVideoFrame(frame_image))
    monkeypatch.setattr(app.callout_handler, "pick_phrase", lambda _: "Callout visibility test")
    app.callout_handler.active_callout = True
    app.callout_handler.talking_chance = 1.0
    app.callout_handler.session_started()

    def rendered_together():
        snapshot = app.hud.grab().toImage()
        snapshot.setDevicePixelRatio(1)
        scale = app.hud.devicePixelRatioF()
        # Video must be present in the same composed image as the text.
        point = app.player.video_widget.mapTo(app.hud, QPoint(20, 20))
        if snapshot.pixelColor(int(point.x() * scale), int(point.y() * scale)) != QColor("#168040"):
            return False
        label = app.hud.callout_label
        origin = label.mapTo(app.hud, QPoint(0, 0))
        text_color = QColor(theme.ACCENT)
        return any(
            snapshot.pixelColor(x, y) == text_color
            for y in range(int(origin.y() * scale), int((origin.y() + label.height()) * scale))
            for x in range(int(origin.x() * scale), int((origin.x() + label.width()) * scale))
        )

    qtbot.waitUntil(rendered_together, timeout=1000)
    assert app.hud.callout_label.isVisible()

    # The overlays must survive both frame updates and switching away from video.
    app.player.setCurrentWidget(app.player.image_label)
    app.player.setCurrentWidget(app.player.video_widget)
    app.player.media_player.videoSink().setVideoFrame(QVideoFrame(frame_image))
    qtbot.waitUntil(rendered_together, timeout=1000)


@pytest.mark.parametrize("frame_size", [(160, 90), (90, 160)])
def test_video_preserves_aspect_ratio_after_resize(qtbot, frame_size):
    display = VideoDisplay()
    qtbot.addWidget(display)
    display.show()
    frame = QImage(*frame_size, QImage.Format.Format_RGB32)
    frame.fill(QColor("#168040"))
    display.video_item.videoSink().setVideoFrame(QVideoFrame(frame))

    for window_size in [(640, 360), (360, 640)]:
        display.resize(*window_size)

        def correctly_scaled():
            snapshot = display.viewport().grab().toImage()
            expected = frame.size().scaled(snapshot.size(), Qt.AspectRatioMode.KeepAspectRatio)
            x = (snapshot.width() - expected.width()) // 2
            y = (snapshot.height() - expected.height()) // 2
            # Check the center and opposite corners inside the video, plus the bars.
            video_color = QColor("#168040")
            if any(snapshot.pixelColor(point) != video_color for point in [
                snapshot.rect().center(),
                QPoint(x + 3, y + 3),
                QPoint(x + expected.width() - 4, y + expected.height() - 4),
            ]):
                return False
            if x > 4 and snapshot.pixelColor(1, snapshot.height() // 2) != QColor("black"):
                return False
            if y > 4 and snapshot.pixelColor(snapshot.width() // 2, 1) != QColor("black"):
                return False
            return True

        qtbot.waitUntil(correctly_scaled, timeout=1000)
