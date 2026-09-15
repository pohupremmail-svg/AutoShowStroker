from PyQt6.QtCore import Qt
from PyQt6.QtWidgets import (
    QDialog,
    QLabel,
    QPushButton,
    QScrollArea,
    QTabWidget,
    QVBoxLayout,
)

from src import theme

# Each entry is (tab title, rich-text body). Adding a new help topic later is just
# appending here - HelpDialog turns every entry into its own scrollable tab.
HELP_TOPICS = [
    (
        "Beats && Rhythm",  # QTabWidget treats a lone & as a mnemonic marker - && renders one literal &
        "<h3>How the Strokemeter works</h3>"
        "<p>The footer track scrolls notes toward its hit line on a randomized beat, driven "
        "by a <b>frequency</b> (beats per second, picked between your Min./Max. beat "
        "frequency settings) and a <b>rhythm pattern</b> that decides which beats land and "
        "which get skipped.</p>"
        "<h3>What the pattern numbers mean</h3>"
        "<p>A pattern is a list of numbers, e.g. <code>[1, 2, 2, -1, -1]</code>. Each number "
        "is one step of the pattern:</p>"
        "<ul>"
        "<li><b>Sign:</b> positive = an audible beat (sound + flash). Negative = a silent "
        "step - the rhythm keeps counting but nothing plays, creating a pause inside the "
        "pattern itself.</li>"
        "<li><b>Size (1-4):</b> how long the step lasts, and it's an <i>inverse</i> scale - "
        "<b>1 is the longest</b> step, <b>4 is the shortest</b>. A step of size 4 plays a "
        "quarter as long as a step of size 1.</li>"
        "</ul>"
        "<p>So <code>[1, 2, 2, -1, -1]</code> reads as: one long beat, two shorter beats, "
        "then two long silent steps - a swing feel with a built-in breather at the end.</p>"
        "<h3>Building your own</h3>"
        "<p>Settings &gt; Beat &amp; Rhythm &gt; Manage Custom Patterns opens a step "
        "sequencer: drag each step's bar to set its length, click it to toggle Beat/Pause, "
        "and hit Preview to hear it before saving. Your patterns then show up in the Active "
        "Rhythms list right alongside the built-in ones.</p>"
        "<h3>Everything else on the Beat &amp; Rhythm tab</h3>"
        "<ul>"
        "<li><b>Beat frequency (min/max):</b> how fast beats can play, in beats per "
        "second.</li>"
        "<li><b>Beat duration (min/max):</b> how long a single chosen pattern/frequency "
        "combination stays active before the next one takes over. The app plans several "
        "of these ahead, so it always knows what is coming.</li>"
        "<li><b>Pause chance/duration:</b> how often the Strokemeter takes a full countdown "
        "break, and how long that break lasts.</li>"
        "<li><b>Difficulty ramping:</b> gradually narrows the frequency range toward the "
        "faster end as the session goes on, so it starts gentle and tightens over time.</li>"
        "</ul>",
    ),
    (
        "On-Screen Display",
        "<h3>Session Explorer</h3>"
        "<p>The end-of-session statistics have a <b>Session Explorer</b> button. It opens "
        "the whole session as a seek bar: move along it and you see what was on screen at "
        "that moment, together with the rhythm that was playing and how fast. Pauses and "
        "the run-in to the climax are marked, so the shape of the session is visible at a "
        "glance.</p>"
        "<p>It exists mostly for one thing: spotting the picture you particularly liked "
        "and getting back to the file. <b>Show in folder</b> opens it where it lives.</p>"
        "<p>This is kept in memory for the session you just finished and nothing more - it "
        "is gone when you start the next one or close the app. Your media paths are never "
        "written to the data folder or to the diagnostic log.</p>"
        "<h3>Session Timer</h3>"
        "<p>Top-left of the media area, shows how long the current session has been running "
        "(<code>⏱ mm:ss</code>, or <code>h:mm:ss</code> past an hour). It's a wall-clock "
        "reading - pauses count too. Toggle it off in Settings &gt; Playback &gt; Show "
        "session timer.</p>"
        "<h3>Record-Chase Badge</h3>"
        "<p>Top-right of the media area. It stays hidden most of the session and only "
        "appears once you're within reach of a personal record (currently within 20%), then "
        "flips to \"New Record!\" the moment you actually break it - a deliberate "
        "anticipation moment, not a permanent stats display. Toggle it off in "
        "Settings &gt; Playback &gt; Show live personal-record chase.</p>"
        "<h3>Callouts</h3>"
        "<p>Bottom-center of the media area - the teasing phrases that fire on beat changes "
        "and session events. See the Languages &amp; Tones tab for how these are sourced.</p>"
        "<h3>The Strokemeter</h3>"
        "<p>The track along the bottom. Notes travel right-to-left and land on the glowing "
        "hit line near the left edge exactly when the beat sounds, so you can see each beat "
        "coming before you hear it. A rhythm's silent steps show up as the wider gaps between "
        "notes. When the rhythm changes, a sweep of light crosses the track to mark it - the "
        "notes themselves keep flowing straight through, because the app already knows what "
        "is coming next. They even fly in during a break, so you can see the beat returning. "
        "The caption on the right shows the current pattern, or the countdown while the "
        "Strokemeter is taking one.</p>"
        "<h3>Climax Banner</h3>"
        "<p>Sits just above the Strokemeter and lights up with the session's outcome "
        "(Cum / Ruined / Denied) once it's decided.</p>",
    ),
    (
        "Languages && Tones",
        "<h3>Callouts are just JSON files</h3>"
        "<p>Every teasing phrase you see comes from a JSON file under <code>res/callouts/</code>, "
        "one folder per language and one file per tone inside it - "
        "<code>en/flirty.json</code>, <code>de/dominant.json</code>, and so on. No Python "
        "knowledge is needed to add or translate phrases.</p>"
        "<h3>Language and tone are two separate choices</h3>"
        "<p>You pick one language and as many tones as you like in "
        "Settings &gt; Callouts. Tick several and the app mixes them evenly - each ticked tone "
        "speaks about as often as the others, no matter how many phrases were written for it.</p>"
        "<ul>"
        "<li><b>Flirty</b> - playful and teasing. The app's original voice, and the default.</li>"
        "<li><b>Shy</b> - timid and hesitant, a little embarrassed to be bossing you around.</li>"
        "<li><b>Dominant</b> - commanding and certain. Orders, not suggestions.</li>"
        "<li><b>Degrading</b> - humiliation play, and considerably harsher than the rest. It is "
        "off unless you tick it yourself, and it never switches itself on.</li>"
        "<li><b>Degrading (Hard)</b> - the same, turned all the way up: it goes after your size "
        "and how little you measure up, start to finish. Opt-in, same as above.</li>"
        "<li><b>Girlfriend Experience</b> - warm and affectionate, sweet to gently bossy, never "
        "degrading.</li>"
        "</ul>"
        "<h3>Adding a new language</h3>"
        "<ol>"
        "<li>Copy an existing language folder, e.g. <code>en</code>, to a new folder named after "
        "your language code (e.g. <code>es</code>), keeping the tone files you want inside it.</li>"
        "<li>Translate the phrases in each array - or write entirely new ones, that's up to "
        "you - and keep each file in its own tone's voice.</li>"
        "<li>Keep every <b>Trigger Key</b> (the JSON keys like <code>beat_change_faster</code> "
        "or <code>pause_start</code>) exactly as they are - only the phrases inside each "
        "array should change. A misspelled key won't error, it'll just stay silent for that "
        "event.</li>"
        "<li>Only <code>flirty.json</code> is required; the rest are optional. A tone your "
        "language doesn't have is simply skipped, and Settings marks it <i>(not in de)</i> so "
        "you can see why it went quiet.</li>"
        "</ol>"
        "<p>Once the folder is saved under <code>res/callouts/</code>, it shows up automatically "
        "in Settings &gt; Callouts &gt; Language - no restart or extra setup needed. Adding a new "
        "tone works the same way in the other direction: one <code>&lt;tone&gt;.json</code> in "
        "every language folder.</p>"
        "<p>Adding new phrases to a language that already exists works the same way: open "
        "the file for the language and tone you're writing for and append a string to the "
        "relevant array.</p>"
        "<h3>Or skip editing the app entirely</h3>"
        "<p>Settings &gt; Callouts &gt; Manage Custom Phrase Files lets you point GoonerApp at "
        "a JSON file anywhere on your own machine (same format as above) - pick which "
        "language and tone it's for and add it, no need to touch anything inside the app "
        "itself. Leave the tone on <i>Any tone</i> and it will be heard whatever mix you have "
        "ticked. Add or remove files any time, changes apply immediately.</p>"
        "<p>For the full Trigger Key reference and validation steps, see "
        "<code>CONTRIBUTING.md</code> in the project repository.</p>",
    ),
    (
        "Keyboard Shortcuts",
        "<h3>Playback</h3>"
        "<ul>"
        "<li><b>Ctrl+O</b> - Set/Change Gooning Folder</li>"
        "<li><b>Right Arrow</b> - Next media (same as Skip &gt;&gt;)</li>"
        "<li><b>Left Arrow</b> - Previous media (same as &lt;&lt; Previous)</li>"
        "<li><b>Ctrl+Space</b> - Stop the session</li>"
        "</ul>"
        "<h3>Audio &amp; Panic</h3>"
        "<ul>"
        "<li><b>M</b> - Toggle mute (beat sound and video audio together)</li>"
        "<li><b>Space</b> - Panic: instantly minimizes the window and mutes audio. Does not "
        "stop the session, and doesn't auto-unmute when you come back - press <b>M</b> once "
        "you're ready for sound. (Space always mutes and minimizes, it never restores.)</li>"
        "</ul>"
        "<h3>Window</h3>"
        "<ul>"
        "<li><b>F</b> or <b>F11</b> - Toggle fullscreen</li>"
        "<li><b>Escape</b> - Leave fullscreen</li>"
        "</ul>"
        "<h3>App</h3>"
        "<ul>"
        "<li><b>Ctrl+S</b> - Open Settings</li>"
        "<li><b>F1</b> - Open this Guide</li>"
        "<li><b>Ctrl+Q</b> - Quit</li>"
        "</ul>",
    ),
    (
        "Privacy",
        "<h3>Everything stays on your machine</h3>"
        "<p>GoonerApp runs 100% locally. Your media folders, session history, and settings "
        "never leave your machine - there's no account to create, no login, and no "
        "telemetry of any kind.</p>"
        "<h3>Optional connections</h3>"
        "<p>Loading your folder, playing your files, and tracking your stats happen "
        "offline, on-device. Help &gt; Check for Updates: only when "
        "you click it and confirm the warning, GoonerApp sends a single request to "
        "GitHub.com to check the latest release version. Nothing else is sent, and it "
        "never runs automatically.</p>"
        "<p>Intiface support is off by default. Enabling it in <b>Settings &gt; Device</b> "
        "connects to your configured server, including on later launches. It sends the "
        "app name, discovery/heartbeat messages, and movement/stop commands. No media, "
        "paths, callouts, or session history are sent. The default address "
        "<b>ws://127.0.0.1:12345</b> stays on your machine; a remote address sends device "
        "commands over your network.</p>"
        "<h3>Where your data actually sits</h3>"
        "<p>Staying on your machine still means it is written somewhere, so here it is. "
        "Your session history, custom rhythm patterns, custom phrase files and the media "
        "folders the picker remembers are plain JSON files in "
        "<b>%LOCALAPPDATA%\\GoonerCock\\GoonerApp</b>. Your settings - sliders, toggles, "
        "language - live in the registry under "
        "<b>HKEY_CURRENT_USER\\Software\\GoonerCock\\GoonerApp</b>.</p>"
        "<p>GoonerApp is portable, so deleting the .exe does <i>not</i> remove either of "
        "those. Open <b>Help &gt; Privacy &amp; Data</b> to see the exact paths on this "
        "machine, open the data folder, or delete any of it - per category, so you can "
        "wipe the remembered folder paths without losing your stats.</p>"
        "<h3>The diagnostic log</h3>"
        "<p>There is also an optional log file, <b>off by default</b>. It lives in the same "
        "Privacy &amp; Data dialog - switch it on there only if something is misbehaving "
        "and you want to see why, and pick how much it records (everything, problems "
        "only, or errors only). It notes what the app is doing, which also means it "
        "notes <i>when</i> you used it. It never contains the folders you play from; a "
        "file that failed to load is named, nothing else is. It sits with your other "
        "data and can be deleted on its own, right below the switch.</p>"
        "<h3>What that means going forward</h3>"
        "<p>Any future feature that would need the internet stays held to the same bar: "
        "strictly opt-in and clearly disclosed here - never on by default, never silent.</p>",
    ),
]


class HelpDialog(QDialog):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Guide")
        self.setModal(True)
        self.resize(560, 520)

        layout = QVBoxLayout(self)

        title = QLabel("Guide")
        title.setStyleSheet(f"font-size: 22px; font-weight: bold; color: {theme.ACCENT}; margin-bottom: 8px;")
        layout.addWidget(title)

        self.tabs = QTabWidget()
        for tab_title, body_html in HELP_TOPICS:
            self.tabs.addTab(self._build_tab(body_html), tab_title)
        layout.addWidget(self.tabs)

        self.button = QPushButton("Close")
        self.button.setObjectName("primary")
        self.button.clicked.connect(self.accept)
        layout.addWidget(self.button)

    @staticmethod
    def _build_tab(body_html):
        label = QLabel(body_html)
        label.setTextFormat(Qt.TextFormat.RichText)
        label.setWordWrap(True)
        label.setAlignment(Qt.AlignmentFlag.AlignTop)
        label.setStyleSheet(f"color: {theme.TEXT}; font-size: 13px; background-color: transparent; padding: 10px;")

        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setWidget(label)
        return scroll
