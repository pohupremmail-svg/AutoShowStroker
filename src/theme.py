"""Central color palette and global QSS for the "Cyber-Erotic Neon" theme.

Palette derived from the app logo (qUloN.png): neon pink line-art on a
near-black purple background.
"""

from PyQt6.QtGui import QColor, QPalette

BACKGROUND = "#2D1D3A"
ACCENT = "#FF00BF"
ACCENT_HOVER = "#FF33CC"
TEXT = "#FFC2ED"
SECONDARY = "#5A3D73"
SECONDARY_HOVER = "#6E4A8C"
SURFACE = "#3A2650"
SURFACE_DARK = "#241730"
DISABLED_BG = "#3A2A4A"
DISABLED_TEXT = "#7A6288"

# Strokemeter pulse state, distinct from ACCENT so a pause doesn't read as a beat flash.
PAUSE = "#8C3D73"

# Climax outcome colors: "cum" reuses ACCENT (matches the logo directly). Ruined/denied
# deliberately stay on different hues - three same-colored outcomes would defeat the point
# of a color-coded status banner.
RUINED = "#FFA733"
RUINED_DIM = "#CC7A00"
DENIED = "#FF3B3B"
DENIED_DIM = "#B31212"


def build_palette() -> QPalette:
    """QSS alone doesn't reliably color natively-drawn sub-elements (spin box up/down
    arrows, combo box drop-down arrow) - those follow QPalette. Without this, arrows render
    in the OS default color, which is nearly invisible against a dark theme."""
    palette = QPalette()
    palette.setColor(QPalette.ColorRole.Window, QColor(BACKGROUND))
    palette.setColor(QPalette.ColorRole.WindowText, QColor(TEXT))
    palette.setColor(QPalette.ColorRole.Base, QColor(SURFACE))
    palette.setColor(QPalette.ColorRole.AlternateBase, QColor(SURFACE_DARK))
    palette.setColor(QPalette.ColorRole.Text, QColor(TEXT))
    palette.setColor(QPalette.ColorRole.Button, QColor(SECONDARY))
    palette.setColor(QPalette.ColorRole.ButtonText, QColor(TEXT))
    palette.setColor(QPalette.ColorRole.BrightText, QColor(ACCENT))
    palette.setColor(QPalette.ColorRole.Highlight, QColor(ACCENT))
    palette.setColor(QPalette.ColorRole.HighlightedText, QColor(BACKGROUND))
    palette.setColor(QPalette.ColorRole.ToolTipBase, QColor(SURFACE))
    palette.setColor(QPalette.ColorRole.ToolTipText, QColor(TEXT))
    palette.setColor(QPalette.ColorGroup.Disabled, QPalette.ColorRole.Text, QColor(DISABLED_TEXT))
    palette.setColor(QPalette.ColorGroup.Disabled, QPalette.ColorRole.ButtonText, QColor(DISABLED_TEXT))
    return palette


GLOBAL_QSS = f"""
QMainWindow, QDialog, QWidget {{
    background-color: {BACKGROUND};
    color: {TEXT};
    font-family: 'Segoe UI', sans-serif;
}}
QPushButton {{
    background-color: {SECONDARY};
    color: {TEXT};
    border: 1px solid {SECONDARY};
    border-radius: 8px;
    padding: 6px 14px;
}}
QPushButton:hover {{
    background-color: {SECONDARY_HOVER};
}}
QPushButton:pressed {{
    background-color: {BACKGROUND};
}}
QPushButton:disabled {{
    background-color: {DISABLED_BG};
    color: {DISABLED_TEXT};
    border-color: {DISABLED_BG};
}}
QPushButton#primary {{
    background-color: {ACCENT};
    color: {BACKGROUND};
    font-weight: bold;
    border: 1px solid {TEXT};
}}
QPushButton#primary:hover {{
    background-color: {ACCENT_HOVER};
}}
QLabel {{
    color: {TEXT};
    background-color: transparent;
}}
QCheckBox {{
    color: {TEXT};
    spacing: 8px;
}}
QCheckBox::indicator {{
    width: 16px;
    height: 16px;
    border: 1px solid {SECONDARY};
    border-radius: 4px;
    background-color: {SURFACE};
}}
QCheckBox::indicator:checked {{
    background-color: {ACCENT};
    border-color: {ACCENT};
}}
QComboBox, QDoubleSpinBox, QSpinBox {{
    background-color: {SURFACE};
    color: {TEXT};
    border: 1px solid {SECONDARY};
    border-radius: 6px;
    padding: 4px 8px;
}}
QDoubleSpinBox, QSpinBox {{
    padding-right: 22px;
}}
QComboBox:hover, QDoubleSpinBox:hover, QSpinBox:hover {{
    border-color: {ACCENT};
}}
QComboBox QAbstractItemView {{
    background-color: {SURFACE};
    color: {TEXT};
    selection-background-color: {ACCENT};
    selection-color: {BACKGROUND};
    border: 1px solid {SECONDARY};
}}
QComboBox::drop-down {{
    subcontrol-origin: border;
    subcontrol-position: top right;
    width: 20px;
    border-left: 1px solid {SECONDARY};
    border-top-right-radius: 6px;
    border-bottom-right-radius: 6px;
    background-color: {SECONDARY};
}}
QComboBox::down-arrow {{
    width: 10px;
    height: 10px;
}}
QDoubleSpinBox::up-button, QSpinBox::up-button {{
    subcontrol-origin: border;
    subcontrol-position: top right;
    width: 20px;
    height: 13px;
    border-left: 1px solid {SECONDARY};
    border-bottom: 1px solid {SECONDARY};
    border-top-right-radius: 6px;
    background-color: {SECONDARY};
}}
QDoubleSpinBox::down-button, QSpinBox::down-button {{
    subcontrol-origin: border;
    subcontrol-position: bottom right;
    width: 20px;
    height: 13px;
    border-left: 1px solid {SECONDARY};
    border-bottom-right-radius: 6px;
    background-color: {SECONDARY};
}}
QDoubleSpinBox::up-button:hover, QSpinBox::up-button:hover,
QDoubleSpinBox::down-button:hover, QSpinBox::down-button:hover {{
    background-color: {ACCENT};
}}
QDoubleSpinBox::up-arrow, QSpinBox::up-arrow {{
    width: 8px;
    height: 8px;
}}
QDoubleSpinBox::down-arrow, QSpinBox::down-arrow {{
    width: 8px;
    height: 8px;
}}
QTableWidget {{
    background-color: {SURFACE_DARK};
    color: {TEXT};
    border: 1px solid {SECONDARY};
    border-radius: 10px;
    gridline-color: transparent;
    outline: none;
}}
QTableWidget::item {{
    padding: 6px 12px;
    border: none;
}}
QTableWidget::item:alternate {{
    background-color: {SURFACE};
}}
QHeaderView::section {{
    background-color: {SURFACE};
    color: {ACCENT};
    font-weight: bold;
    border: none;
    padding: 8px 12px;
}}
QHeaderView::section:first {{
    border-top-left-radius: 10px;
}}
QHeaderView::section:last {{
    border-top-right-radius: 10px;
}}
QSplitter::handle {{
    background-color: {SECONDARY};
}}
QSplitter::handle:hover {{
    background-color: {ACCENT};
}}
QScrollBar:vertical, QScrollBar:horizontal {{
    background-color: {BACKGROUND};
    border: none;
}}
QScrollBar::handle {{
    background-color: {SECONDARY};
    border-radius: 4px;
}}
QScrollBar::handle:hover {{
    background-color: {ACCENT};
}}
QTabWidget::pane {{
    border: 1px solid {SECONDARY};
    border-radius: 8px;
    top: -1px;
}}
QTabBar::tab {{
    background-color: {SURFACE};
    color: {TEXT};
    padding: 8px 16px;
    border-top-left-radius: 8px;
    border-top-right-radius: 8px;
    margin-right: 2px;
}}
QTabBar::tab:selected {{
    background-color: {ACCENT};
    color: {BACKGROUND};
    font-weight: bold;
}}
QTabBar::tab:hover {{
    background-color: {SECONDARY_HOVER};
}}
QScrollArea {{
    border: none;
}}
"""
