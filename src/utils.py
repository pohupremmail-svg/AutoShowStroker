import sys
from pathlib import Path


def get_project_root() -> Path:
    """
    Findet das Projekt-Wurzelverzeichnis robust, sowohl als Skript
    als auch als PyInstaller-Exe.
    """
    # Fall 1: Läuft als PyInstaller Exe
    if hasattr(sys, '_MEIPASS'):
        return Path(sys._MEIPASS)

    # Fall 2: Läuft als Skript (PyCharm, Terminal)
    # Startpunkt ist die Datei, in der wir uns befinden (GoonerApp.py oder main.py)
    start_path = Path(__file__).resolve()

    # Wir durchsuchen die Eltern-Ordner nach einer Marker-Datei.
    # requirements.txt oder .git sind gute Marker.
    for parent in start_path.parents:
        if (parent / 'main.py').exists():
            return parent

    # Falls kein Marker gefunden wurde, nutzen wir den Fallback
    # (z.B. wenn man ohne Git arbeitet).
    # Hier könnte man hartkodiert `..` nutzen, falls GoonerApp.py in src/ liegt.
    return start_path.parent.parent  # Entspricht ../.. wenn start_path in src/ liesgt