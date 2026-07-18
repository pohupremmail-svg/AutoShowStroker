# Auto Hero Generation (GoonerApp)

A specialized, interactive PyQt6 multimedia application designed to transform your local media library (images, GIFs, and videos) into a dynamic, personalized "Cock Hero" session. The app combines a randomized playlist with a configurable, interactive rhythm generator ("Strokemeter") and motivational text instructions (callouts).

## ✨ Features

* **Playlist Functionality:** Select any local folder. The application recursively searches for all supported media formats (`.mp4`, `.gif`, `.jpeg`, `.jpg`, `.png`).
* **Randomized Playback:** The order of the media is completely shuffled (`random.shuffle`) each time a folder is loaded.
* **Interactive Strokemeter (Beat Timer):** A dynamic rhythm generator located in the application's footer:
    * Automatically varying beat frequencies and rhythm patterns (e.g., *Standard Beat*, *Quick Swing*, *Simple Bounce*, *Double Tap*).
    * Visual feedback through rhythmic color changes ("UP" / "DOWN").
    * Audio feedback with a precise sound effect played on every beat.
* **Random Pause Phases:** The Strokemeter unexpectedly transitions into a controlled pause featuring a countdown display in the green-colored footer.
* **Multilingual Callouts (Teases):** Random text instructions tailored to the current event (e.g., during tempo changes, pauses, or media transitions). Supports German and English (expandable via JSON files).
* **Detailed Session Statistics:** At the end of each session, you receive a detailed evaluation (duration, number of beats, favorite rhythm, pause statistics).
* **Flexible Control:** Keyboard shortcuts for rapid navigation and adjustments during the session.

## 🚀 Installation & Execution (Developers)

To run the application locally, you need **Python 3.8+** and the appropriate dependencies.

### 1. Create and Activate a Virtual Python Environment
```bash
# Create a virtual environment
python -m venv .venv

# On Windows (PowerShell):
.\.venv\Scripts\activate.ps1

# On Linux/macOS or Git Bash:
source .venv/bin/activate
```

### 2. Install Dependencies
```bash
pip install --upgrade pip
pip install -r requirements.txt
```

### 3. Start the Application
```bash
python main.py
```

## 📦 Compile as a Standalone .exe (PyInstaller)

You can package the entire application, including all sounds and language files, into a single, portable `.exe` file. This eliminates the need for the end-user to have Python installed.

Ensure that your virtual environment is active and all dependencies from `requirements.txt` are installed. Then either run `python scripts/build.py`, or execute the following command directly in the main directory of the project:

```bash
pyinstaller --noconfirm --onefile --windowed --add-data "res;res" --icon "res/icons/favicon.ico" --name "GoonerApp" main.py
```

### What does this command do?
* `--onefile`: Packages everything into a single executable `.exe` file.
* `--windowed`: Prevents an unsightly console window from opening in the background when starting the app.
* `--add-data "res;res"`: Embeds the entire resource folder (sounds and callouts) directly into the `.exe`.
* `--icon "res/icons/favicon.ico"`: Sets the `.exe`'s icon.
* `--name "GoonerApp"`: Renames the final output file.

After a successful build, you will find the finished file **`GoonerApp.exe`** in the newly created **`dist/`** directory. You can now move and distribute this file as you wish!

## 🎮 Operation & Controls

1. **Load Folder:** Click "Add Gooning Folder" and select the directory containing your images and videos. The slideshow will start automatically.
2. **Navigation:**
    * **Next Media:** `Right Arrow` key or click "Skip >>".
    * **Previous Media:** `Left Arrow` key or click "<< Previous".
3. **Settings:** Press `Ctrl + S` or use the menu in the top left corner to adjust frequencies, beat patterns, pause durations, and the language of the callouts.
4. **Adjust Layout:** The boundary between the media area and the Strokemeter can be adjusted via drag-and-drop to customize the ratio to your liking.

## 📂 Project Structure

* `src/GoonerApp.py` - Main window, media control, and GUI layout.
* `src/BeatHandler.py` - Logic for rhythm, audio playback, and pauses.
* `src/CalloutHandler.py` - Management and selection of multilingual text instructions.
* `src/ScoreTracker.py` - Recording of session statistics.
* `res/callouts/` - JSON files for translations (`de.json`, `en.json`).

## 🌍 Contributing (New Languages & Phrases)

Want to add your own language, or new teasing phrases to an existing one? No Python knowledge required — see
[CONTRIBUTING.md](CONTRIBUTING.md) for the full guide, including which Trigger Key fires for which in-app event,
how to validate your file, and how to submit it as a Pull Request.

## 📸 Screenshots

![Main Screen](demo_screens/demo_main_screen.png)
*The main screen with loaded media and the inactive Strokemeter.*

![Running Session](demo_screens/demo_main_screen_running_censored.png)
*The running session with an active rhythm generator and color feedback.*

![Statistics Window](demo_screens/demo_stat_screen.png)
*The detailed evaluation at the end of a successful session.*