# Projekt-Dokumentation: Auto Hero Generation (GoonerApp)

Dieses Dokument beschreibt die Architektur, die Funktionsweise und die Kernkomponenten der interaktiven PyQt6-Multimedia-Anwendung **GoonerApp**.

## 1. Projektübersicht
Die Anwendung generiert dynamisch und interaktiv ein "Cock Hero"-ähnliches Erlebnis aus einer lokalen Sammlung von Bildern, GIFs und Videos. Statt eines statischen Videos steuert die App die Medienwiedergabe, den Rhythmus (Beat) und die Anweisungen (Callouts/Teases) in Echtzeit auf Basis konfigurierbarer Parameter.

## 2. Systemarchitektur & Komponenten

Die Anwendung ist modular in Python mit **PyQt6** aufgebaut. Die wichtigsten Klassen und ihre Aufgaben sind:

### 2.1 `GoonerApp` (Hauptfenster & GUI)
* **Datei:** `src/GoonerApp.py` (Einstiegspunkt: `main.py`)
* **Aufgabe:** Verwaltet das Hauptfenster, das Layout (Splitter zwischen Medienbereich und Strokemeter), die Tastatur-Shortcuts (z. B. Pfeiltasten, `Ctrl+S` für Einstellungen) und die Medienwiedergabe (Bilder via `QLabel`, Videos via `QMediaPlayer` / `QVideoWidget`).
* **Ablauf:** Lädt Ordner rekursiv, mischt die Playlist zufällig und steuert das automatische Weiterschalten (Autoplay) basierend auf Timern.

### 2.2 `BeatHandler` (Der Taktgeber / Strokemeter)
* **Datei:** `src/BeatHandler.py`
* **Aufgabe:** Berechnet und steuert den Rhythmus.
* **Features:**
  * **Beat-Muster (Patterns):** Definiert verschiedene Rhythmen (z. B. "Standard Beat", "Quick Swing", "Simple Bounce", "Double Tap", "Syncopated 4/4") über Listen von Intervallen.
  * **Audio-Feedback:** Spielt bei jedem Beat einen Soundeffekt ab (`mixkit-cool-interface-click-tone-2568.wav`).
  * **Visuelles Feedback:** Wechselt die Anzeige im Footer rhythmisch zwischen "UP" und "DOWN" (Blink-Effekt).
  * **Zufällige Pausen:** Initiiert zufällige Pausenphasen (Stopp des Beats, Anzeige eines Countdowns im grünen Footer).

### 2.3 `CalloutHandler` (Interaktive Textanweisungen)
* **Datei:** `src/CalloutHandler.py`
* **Aufgabe:** Lädt lokalisierte Textdateien (`res/callouts/de.json`, `res/callouts/en.json`) und gibt in bestimmten Momenten (z. B. Session-Start, Medienwechsel, Tempowechsel, Pausen) zufällige, passende Textanweisungen ("Teases") aus.
* **Schnittstelle:** Löst Events aus, die von der GUI als Overlay oder Textzeile dargestellt werden.

### 2.4 `ScoreTracker` (Statistik-Erfassung)
* **Datei:** `src/ScoreTracker.py`
* **Aufgabe:** Protokolliert die gesamte Session im Hintergrund.
* **Metriken:**
  * Anzahl und Gesamtdauer der Pausen.
  * Gesamtzahl der Beats und Tempowechsel.
  * Bevorzugtes Beat-Muster (Favorit).
  * Durchschnittliche Pausendauer und durchschnittliche Geschwindigkeit.
  * Gesamtlaufzeit der Session.

### 2.5 `SettingsDialog` & `StatisticsDialog`
* **Dateien:** `src/SettingsDialog.py`, `src/StatisticsDialog.py`
* **Aufgabe:** 
  * `SettingsDialog` erlaubt die Live-Anpassung von Frequenzen, Pausendauern, Mustern und der Sprache der Callouts.
  * `StatisticsDialog` zeigt am Ende der Session eine detaillierte, tabellarische Zusammenfassung der erfassten Leistungsdaten des Nutzers.

## 3. Datenfluss & Event-Steuerung

Die Komponenten kommunizieren lose gekoppelt über **PyQt-Signale (Signals & Slots)**:
1. **Medien-Events:** Wenn der Nutzer ein Medium überspringt oder wiederholt, fängt der `CalloutHandler` dies ab und gibt einen passenden Kommentar aus. Gleichzeitig registriert der `ScoreTracker` die Aktivität.
2. **Beat-Events:** Der `BeatHandler` triggert bei jedem Takt ein Signal, das den Sound abspielt, die GUI blinken lässt und den `ScoreTracker` hochzählt.
3. **Pausen-Events:** Startet eine Pause, wird der Timer im Footer visualisiert, die Medienwiedergabe ggf. angepasst und ein Callout abgespielt.
