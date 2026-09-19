from PyQt6.QtCore import QRectF, QSizeF, Qt
from PyQt6.QtGui import QBrush, QColor
from PyQt6.QtMultimediaWidgets import QGraphicsVideoItem
from PyQt6.QtWidgets import QFrame, QGraphicsScene, QGraphicsView


class VideoDisplay(QGraphicsView):
    """Video painted with the other widgets, so labels can be layered above it.

    QVideoWidget embeds a native QWindow that can cover sibling callout/timer
    labels even though Qt reports them as visible and above the video widget.
    QGraphicsVideoItem keeps both the frame and those labels in QWidget painting.
    """

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setScene(QGraphicsScene(self))
        self.setFrameShape(QFrame.Shape.NoFrame)
        self.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        self.setVerticalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        self.setBackgroundBrush(QBrush(QColor("black")))
        self.setInteractive(False)
        self.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        self.setMinimumSize(1, 1)

        self.video_item = QGraphicsVideoItem()
        self.video_item.setAspectRatioMode(Qt.AspectRatioMode.KeepAspectRatio)
        self.scene().addItem(self.video_item)

    def resizeEvent(self, event):
        super().resizeEvent(event)
        size = QSizeF(self.viewport().size())
        self.setSceneRect(QRectF(0, 0, size.width(), size.height()))
        self.video_item.setSize(size)
