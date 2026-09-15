"""The Device settings tab; connection controls apply independently of other settings."""
from PyQt6.QtWidgets import (
    QCheckBox,
    QDoubleSpinBox,
    QFormLayout,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QPushButton,
    QVBoxLayout,
    QWidget,
)


class IntifaceSettingsWidget(QWidget):
    def __init__(self, main_app, parent=None):
        super().__init__(parent)
        self.controller = main_app.intiface_controller
        controller = self.controller
        layout = QVBoxLayout(self)
        self.enabled = QCheckBox("Enable Intiface")
        self.enabled.setChecked(controller.enabled)
        layout.addWidget(self.enabled)

        explanation = QLabel(
            "Start Intiface Central and connect your linear device there. "
            "The first available device with linear movement support is used."
        )
        explanation.setWordWrap(True)
        layout.addWidget(explanation)
        form = QFormLayout()
        self.server = QLineEdit(controller.server_url)
        form.addRow("Server:", self.server)
        self.minimum = self._position_box(controller.min_position)
        self.maximum = self._position_box(controller.max_position)
        form.addRow("Minimum position (DOWN):", self.minimum)
        form.addRow("Maximum position (UP):", self.maximum)
        layout.addLayout(form)

        self.error = QLabel()
        self.error.setWordWrap(True)
        layout.addWidget(self.error)
        apply = QPushButton("Apply Device Settings")
        apply.clicked.connect(self._apply_clicked)
        layout.addWidget(apply)
        hint = QLabel("Apply changes before testing. Each test movement takes one second.")
        hint.setWordWrap(True)
        layout.addWidget(hint)

        self.status = QLabel()
        self.status.setWordWrap(True)
        self.device = QLabel()
        self.device.setWordWrap(True)
        self.sync_status = QLabel()
        for label in (self.status, self.device, self.sync_status):
            layout.addWidget(label)

        buttons = QHBoxLayout()
        self.test_up = QPushButton("Test Up")
        self.test_down = QPushButton("Test Down")
        self.stop = QPushButton("Emergency Stop")
        self.test_up.clicked.connect(controller.test_up)
        self.test_down.clicked.connect(controller.test_down)
        self.stop.clicked.connect(controller.emergency_stop)
        for button in (self.test_up, self.test_down, self.stop):
            buttons.addWidget(button)
        layout.addLayout(buttons)

        self.resume = QPushButton("Resume Device Sync")
        self.resume.clicked.connect(main_app.resume_intiface_sync)
        layout.addWidget(self.resume)
        scan = QPushButton("Scan for Devices")
        scan.clicked.connect(controller.scan)
        layout.addWidget(scan)
        note = QLabel(
            "Panic, Emergency Stop and reconnection leave device sync stopped. "
            "Use Resume Device Sync or start a new session to continue. "
            "Rhythm pauses resume automatically."
        )
        note.setWordWrap(True)
        layout.addWidget(note)
        layout.addStretch()
        controller.state_changed.connect(self.refresh)
        self.refresh()

    @staticmethod
    def _position_box(position):
        box = QDoubleSpinBox()
        box.setRange(0, 100)
        box.setDecimals(1)
        box.setSuffix(" %")
        box.setValue(position * 100)
        return box

    def values(self):
        return (
            self.enabled.isChecked(), self.server.text().strip(),
            self.minimum.value() / 100, self.maximum.value() / 100,
        )

    def validation_error(self):
        _enabled, url, low, high = self.values()
        try:
            self.controller.validate(url, low, high)
        except ValueError as error:
            return str(error)
        return None

    def apply_settings(self):
        values = self.values()
        current = tuple(getattr(self.controller, key) for key in self.controller.DEFAULTS)
        if values != current:
            self.controller.configure(*values)

    def _apply_clicked(self):
        error = self.validation_error()
        self.error.setText(error or "")
        if error is None:
            self.apply_settings()

    def refresh(self):
        controller = self.controller
        ready = controller.enabled and bool(controller.device_name)
        self.status.setText(controller.status)
        self.device.setText(f"Device: {controller.device_name or 'No linear device connected'}")
        if not controller.session_active:
            sync = "Device sync: waiting for a session"
        elif not controller.armed:
            sync = "Device sync: stopped"
        elif controller.paused:
            sync = "Device sync: rhythm pause"
        else:
            sync = "Device sync: active"
        self.sync_status.setText(sync)
        self.test_up.setEnabled(ready)
        self.test_down.setEnabled(ready)
        self.resume.setEnabled(ready and controller.session_active and not controller.armed)
