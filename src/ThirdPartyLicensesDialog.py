"""Help > Third-Party Licenses: what GoonerApp.exe carries of other people's software.

Several of those licenses only allow passing the software on when its license text and notices
travel with it, and a --onefile build has no folder anyone could open - so the texts are bundled
under res/licenses/ and shown here. THIRD_PARTY_NOTICES.txt comes first, as the index; every
other file in the folder can be picked from the list. tests/test_third_party_licenses.py keeps
the notices in step with what the build actually bundles.
"""
from PyQt6.QtWidgets import QComboBox, QDialog, QPlainTextEdit, QPushButton, QVBoxLayout

from src.utils import get_project_root

NOTICES = "THIRD_PARTY_NOTICES.txt"


def licenses_dir():
    return get_project_root() / "res" / "licenses"


class ThirdPartyLicensesDialog(QDialog):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Third-Party Licenses")
        self.resize(760, 620)

        layout = QVBoxLayout(self)

        self.chooser = QComboBox()
        self.chooser.addItem(NOTICES)
        for path in sorted(licenses_dir().glob("*.txt"), key=lambda p: p.name.lower()):
            if path.name != NOTICES:
                self.chooser.addItem(path.name)
        self.chooser.currentTextChanged.connect(self._show)
        layout.addWidget(self.chooser)

        self.text_view = QPlainTextEdit()
        self.text_view.setReadOnly(True)
        # License texts are laid out for a fixed-width font. Set by stylesheet: the global one in
        # theme.py names a font for every widget, and that wins over setFont().
        self.text_view.setStyleSheet("font-family: Consolas, 'Courier New', monospace; font-size: 12px;")
        layout.addWidget(self.text_view)

        close = QPushButton("Close")
        close.clicked.connect(self.accept)
        layout.addWidget(close)

        self._show(NOTICES)

    def _show(self, name):
        self.text_view.setPlainText((licenses_dir() / name).read_text(encoding="utf-8"))
