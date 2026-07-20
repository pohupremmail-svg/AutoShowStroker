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
        "<p>The footer bar pulses on a randomized beat, driven by a <b>frequency</b> "
        "(beats per second, picked between your Min./Max. beat frequency settings) and a "
        "<b>rhythm pattern</b> that decides which beats land and which get skipped.</p>"
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
        "combination stays active before the app rolls for a new one.</li>"
        "<li><b>Pause chance/duration:</b> how often the Strokemeter takes a full countdown "
        "break, and how long that break lasts.</li>"
        "<li><b>Difficulty ramping:</b> gradually narrows the frequency range toward the "
        "faster end as the session goes on, so it starts gentle and tightens over time.</li>"
        "</ul>",
    ),
    (
        "Adding Languages",
        "<h3>Callouts are just JSON files</h3>"
        "<p>Every teasing phrase you see comes from a JSON file in <code>res/callouts/</code> "
        "- one file per language, named by its language code (<code>en.json</code>, "
        "<code>de.json</code>, ...). No Python knowledge is needed to add or translate "
        "phrases.</p>"
        "<h3>Adding a new language</h3>"
        "<ol>"
        "<li>Copy an existing file, e.g. <code>en.json</code>, to a new file named after "
        "your language code (e.g. <code>fr.json</code>).</li>"
        "<li>Translate the phrases in each array - or write entirely new ones, that's up to "
        "you.</li>"
        "<li>Keep every <b>Trigger Key</b> (the JSON keys like <code>beat_change_faster</code> "
        "or <code>pause_start</code>) exactly as they are - only the phrases inside each "
        "array should change. A misspelled key won't error, it'll just stay silent for that "
        "event.</li>"
        "</ol>"
        "<p>Once the file is saved in <code>res/callouts/</code>, it shows up automatically "
        "in Settings &gt; Callouts &gt; Language - no restart or extra setup needed.</p>"
        "<p>Adding new phrases to a language that already exists works the same way: open "
        "its file and append a string to the relevant array.</p>"
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
        "stop the session, and doesn't auto-unmute when you come back - press M or Space "
        "again once you're ready for sound.</li>"
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
        "<h3>No cloud, no calls home</h3>"
        "<p>Nothing in the app makes an outbound network request. Loading your folder, "
        "playing your files, tracking your stats - all of it happens entirely offline, "
        "on-device.</p>"
        "<h3>What that means going forward</h3>"
        "<p>Any future feature that would need the internet (like an opt-in update "
        "checker) will stay strictly opt-in and clearly disclosed here - never on by "
        "default, never silent.</p>",
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
