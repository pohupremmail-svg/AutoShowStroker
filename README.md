# Auto Hero Generation (GoonerApp)

Eine spezialisierte, interaktive PyQt6-Multimedia-Anwendung, die Ihre lokale Medienbibliothek (Bilder, GIFs und Videos) in eine dynamische, personalisierte "Cock Hero"-Session verwandelt. Die App kombiniert eine zufällige Playlist mit einem konfigurierbaren, interaktiven Taktgeber ("Strokemeter") und motivierenden Textanweisungen (Callouts).

## ✨ Features

* **Playlist-Funktionalität:** Wählen Sie einen beliebigen lokalen Ordner aus. Die Anwendung sucht rekursiv nach allen unterstützten Medienformaten (`.mp4`, `.gif`, `.jpeg`, `.jpg`, `.png`).
* **Zufällige Wiedergabe:** Die Reihenfolge der Medien wird bei jedem Laden komplett zufällig gemischt (`random.shuffle`).
* **Interaktives Strokemeter (Beat Timer):** Ein dynamischer Taktgeber im Footer der Anwendung:
    * Automatisch variierende Beat-Frequenzen und Rhythmus-Muster (z. B. *Standard Beat*, *Quick Swing*, *Simple Bounce*, *Double Tap*).
    * Visuelles Feedback durch rhythmischen Farbwechsel ("UP" / "DOWN").
    * Akustisches Feedback durch einen präzisen Soundeffekt bei jedem Beat.
* **Zufällige Pausenphasen:** Das Strokemeter wechselt unerwartet in eine kontrollierte Pause mit Countdown-Anzeige im grün gefärbten Footer.
* **Mehrsprachige Callouts (Teases):** Zufällige Textanweisungen passend zum aktuellen Geschehen (z. B. bei Tempowechseln, Pausen oder Medienwechseln). Unterstützt Deutsch und Englisch (erweiterbar über JSON-Dateien).
* **Detaillierte Session-Statistiken:** Am Ende der Session erhalten Sie eine detaillierte Auswertung (Dauer, Anzahl der Beats, Lieblings-Rhythmus, Pausen-Statistiken).
* **Flexible Steuerung:** Tastatur-Shortcuts für schnelle Navigation und Anpassung während der Session.

## 🚀 Installation

Um die Anwendung lokal auszuführen, benötigen Sie **Python 3.8+** und die entsprechenden Abhängigkeiten.

### 1. Virtuelle Python-Umgebung erstellen und aktivieren
```bash
# Virtuelle Umgebung erstellen
python -m venv .venv

# Unter Windows (PowerShell):
.\.venv\Scripts\activate.ps1

# Unter Linux/macOS oder Git Bash:
source .venv/bin/activate
```

### 2. Abhängigkeiten installieren
```bash
pip install --upgrade pip
pip install -r requirements.txt
```

### 3. Anwendung starten
```bash
python main.py
```

## 🎮 Bedienung & Steuerung

1. **Ordner laden:** Klicken Sie auf "Add Gooning Folder" und wählen Sie das Verzeichnis mit Ihren Bildern und Videos aus. Die Diashow startet sofort automatisch.
2. **Navigation:**
    * **Nächstes Medium:** Taste `Pfeil rechts` oder Klick auf "Skip >>".
    * **Vorheriges Medium:** Taste `Pfeil links` oder Klick auf "<< Previous".
3. **Einstellungen:** Drücken Sie `Strg + S` oder nutzen Sie das Menü oben links, um Frequenzen, Beat-Muster, Pausendauern und die Sprache der Callouts anzupassen.
4. **Layout anpassen:** Die Grenze zwischen dem Medienbereich und dem Strokemeter lässt sich per Drag-and-Drop verschieben, um das Verhältnis nach eigenen Wünschen anzupassen.

## 📂 Projektstruktur

* `src/GoonerApp.py` - Hauptfenster, Mediensteuerung und GUI-Layout.
* `src/BeatHandler.py` - Logik für Rhythmus, Sound-Wiedergabe und Pausen.
* `src/CalloutHandler.py` - Verwaltung und Auswahl der mehrsprachigen Textanweisungen.
* `src/ScoreTracker.py` - Aufzeichnung der Session-Statistiken.
* `res/callouts/` - JSON-Dateien für Übersetzungen (`de.json`, `en.json`).

## 📸 Screenshots

![Hauptbildschirm](demo_screens/demo_main_screen.png)
*Der Hauptbildschirm mit geladenen Medien und dem inaktiven Strokemeter.*

![Laufende Session](demo_screens/demo_main_screen_running_censored.png)
*Die laufende Session mit aktivem Taktgeber und farblichem Feedback.*

![Statistik-Fenster](demo_screens/demo_stat_screen.png)
*Die detaillierte Auswertung am Ende einer erfolgreichen Session.*
