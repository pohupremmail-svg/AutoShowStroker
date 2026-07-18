"""Central color palette and global QSS for the "Cyber-Erotic Neon" theme.

Palette derived from the app logo (qUloN.png): neon pink line-art on a
near-black purple background.
"""

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
    border: none;
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
QTableWidget {{
    background-color: {SURFACE_DARK};
    color: {TEXT};
    gridline-color: {SECONDARY};
    border: 1px solid {SECONDARY};
    border-radius: 8px;
}}
QTableWidget::item:selected {{
    background-color: {ACCENT};
    color: {BACKGROUND};
}}
QHeaderView::section {{
    background-color: {SURFACE};
    color: {TEXT};
    border: none;
    border-bottom: 1px solid {SECONDARY};
    padding: 6px;
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
"""
