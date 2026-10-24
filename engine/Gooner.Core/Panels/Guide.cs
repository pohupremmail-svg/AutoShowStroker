using Gooner.Core.App;
using Gooner.Core.Localization;

namespace Gooner.Core.Panels;

public sealed record GuideTopic(string Title, string Html);

// The Guide, one tab per topic, each a page of Qt-style rich text (RichText.ToBbcode draws it).
// Ported from the Python app's HELP_TOPICS (src/HelpDialog.py) and since rewritten where this app
// works differently.
public static class Guide
{
    public static IReadOnlyList<GuideTopic> Topics { get; } =
    [
        new("Beats & Rhythm",
            "<h3>How the Strokemeter works</h3>" +
            "<p>The note track carries notes to its hit zone on a randomized beat, driven " +
            "by a <b>frequency</b> (beats per second, picked between your Min./Max. beat " +
            "frequency settings) and a <b>rhythm pattern</b> that decides which beats land and " +
            "which get skipped.</p>" +
            "<h3>What the pattern numbers mean</h3>" +
            "<p>A pattern is a list of numbers, e.g. <code>[1, 2, 2, -1, -1]</code>. Each number " +
            "is one step of the pattern:</p>" +
            "<ul>" +
            "<li><b>Sign:</b> positive = an audible beat (sound + flash). Negative = a silent " +
            "step - the rhythm keeps counting but nothing plays, creating a pause inside the " +
            "pattern itself.</li>" +
            "<li><b>Size (1-4):</b> how long the step lasts, and it's an <i>inverse</i> scale - " +
            "<b>1 is the longest</b> step, <b>4 is the shortest</b>. A step of size 4 plays a " +
            "quarter as long as a step of size 1.</li>" +
            "</ul>" +
            "<p>So <code>[1, 2, 2, -1, -1]</code> reads as: one long beat, two shorter beats, " +
            "then two long silent steps - a swing feel with a built-in breather at the end.</p>" +
            "<h3>Building your own</h3>" +
            "<p>Settings &gt; Beat &amp; Rhythm &gt; Manage Custom Patterns opens a step " +
            "sequencer: drag each step's bar to set its length, click it to toggle Beat/Pause, " +
            "and hit Preview to hear it before saving. Your patterns then show up in the Active " +
            "Rhythms list right alongside the built-in ones.</p>" +
            "<h3>Everything else on the Beat &amp; Rhythm tab</h3>" +
            "<ul>" +
            "<li><b>Beat frequency (min/max):</b> how fast beats can play, in beats per " +
            "second.</li>" +
            "<li><b>Beat duration (min/max):</b> how long a single chosen pattern/frequency " +
            "combination stays active before the next one takes over. The app plans several " +
            "of these ahead, so it always knows what is coming.</li>" +
            "<li><b>Pause chance/duration:</b> how often the Strokemeter takes a full countdown " +
            "break, and how long that break lasts.</li>" +
            "<li><b>Difficulty ramping:</b> gradually narrows the frequency range toward the " +
            "faster end as the session goes on, so it starts gentle and tightens over time.</li>" +
            "</ul>"),
        new("On-Screen Display",
            "<h3>Session Explorer</h3>" +
            "<p>The end-of-session statistics have a <b>Session Explorer</b> button. It opens " +
            "the whole session as a seek bar: move along it and you see what was on screen at " +
            "that moment, together with the rhythm that was playing and how fast. Pauses and " +
            "the run-in to the climax are marked, so the shape of the session is visible at a " +
            "glance.</p>" +
            "<p>It exists mostly for one thing: spotting the picture you particularly liked " +
            "and getting back to the file. <b>Show in folder</b> opens it where it lives.</p>" +
            "<p>This is kept in memory for the session you just finished and nothing more - it " +
            "is gone when you start the next one or close the app, unless you save it with the " +
            "button in the Explorer itself.</p>" +
            "<h3>Saving and replaying a session</h3>" +
            "<p><b>Save this session</b> in the Session Explorer keeps the whole run: every " +
            "rhythm and how fast it was, the pauses, the fake-outs, the climax and its outcome, " +
            "and how long each medium stayed up. <b>Saved Sessions</b> on the start screen plays it " +
            "back exactly as it went.</p>" +
            "<p>The callouts are deliberately <i>not</i> saved. They stay random, so a session " +
            "still feels different the second time while the part you are measuring yourself " +
            "against - the beat - is identical.</p>" +
            "<p>A session you stopped before it ever climaxed is listed as <b>Stopped early</b> " +
            "- usually the ones worth going back to, since you saved them because you did not " +
            "last. Replaying one plays the recording out and then simply carries on as a normal " +
            "session, with a climax of its own, so this time you can actually finish it.</p>" +
            "<p><b>Export...</b> writes one session to a file you can hand to somebody else, and " +
            "<b>Import...</b> puts one they sent you on your shelf. A saved session records the " +
            "paths of the media it showed so a replay can find them again; exporting asks every " +
            "single time whether to include them, because with them the file tells the recipient " +
            "your Windows user name, your folder layout and your file names. Without them it " +
            "still replays every beat and the pacing - against whatever collection they have, " +
            "which is also what happens when you replay a session whose files have moved.</p>" +
            "<h3>Sessions an AI can write for you</h3>" +
            "<p>A saved session is a plain JSON file, so it does not have to be recorded - it " +
            "can be composed. The format is documented in the repository under " +
            "<b>skills/gooner-session-builder/</b>: hand that folder to an AI assistant and " +
            "ask it for a session of a given length, harshness and ending, then bring the " +
            "file in with <b>Import...</b>.</p>" +
            "<p>Nothing about your collection is involved. A composed session names no files " +
            "at all - it only says when the picture should change - so it plays against " +
            "whatever you have. If anything in the file is wrong, the import lists every " +
            "problem rather than refusing flatly, so it can be fixed in one pass.</p>" +
            "<h3>Session Timer</h3>" +
            "<p>Top-left of the media area, shows how long the current session has been running " +
            "(<code>⏱ mm:ss</code>, or <code>h:mm:ss</code> past an hour). It's a wall-clock " +
            "reading - pauses count too. Toggle it off in Settings &gt; Playback &gt; Show " +
            "session timer.</p>" +
            "<h3>Achievements</h3>" +
            "<p><b>Achievements</b> on the start screen lists every one there is, not only the " +
            "ones you already hold. Earned ones show their mark lit and the date you got " +
            "them; locked ones show what they want and how close your last session came. A " +
            "few are marked <b>???</b> - those are found rather than aimed at.</p>" +
            "<p>Obedience only counts when it costs something: being denied, or told to ruin " +
            "it, and doing as you were told. Being told to come and coming is not obedience, " +
            "it is what you were going to do anyway. They are judged at the end of a session " +
            "and deletable under Privacy &amp; Data.</p>" +
            "<h3>Record-Chase Badge</h3>" +
            "<p>Top-center of the media area. It stays hidden most of the session and only " +
            "appears once you're within reach of a personal record (currently within 20%), then " +
            "flips to \"New Record!\" the moment you actually break it - a deliberate " +
            "anticipation moment, not a permanent stats display. Toggle it off in " +
            "Settings &gt; Playback &gt; Show live personal-record chase.</p>" +
            "<h3>Callouts</h3>" +
            "<p>The teasing phrases that fire on beat changes and session events, in big glowing " +
            "letters over the picture - or, while your character is in her frame, in a speech " +
            "bubble beside her. See the Languages &amp; Tones tab for how these are sourced.</p>" +
            "<h3>The note track</h3>" +
            "<p>Notes fly at you and land in the glowing hit zone exactly when the beat sounds, so " +
            "you can see each beat coming before you hear it. A rhythm's silent steps show up as " +
            "the wider gaps between notes. Changes of rhythm and pauses come flying in with the " +
            "notes, named as they arrive, because the app already knows what is coming next - and " +
            "the notes behind a pause fly in during it, so you can see the beat returning.</p>" +
            "<p>It comes in four styles, picked in Settings &gt; Look: <b>Highway</b>, a lane " +
            "running into the picture with the notes coming towards you; <b>Hypno Ring</b>, notes " +
            "spiralling into a ring that can wander across the picture and take your eyes with " +
            "it, blurring, pixelating or darkening everything outside it if you like; " +
            "<b>Stream</b>, the classic strip, notes streaming in from the right; and " +
            "<b>Tunnel</b>, a tunnel you fly through, the notes corkscrewing out of its depth. " +
            "The colours and how wild the effects get are picked there too.</p>" +
            "<h3>Reaching your edge</h3>" +
            "<p>Press <b>E</b> (or its button in the dock) the moment you are about to lose it. " +
            "The Strokemeter stops for a breather and comes back at the bottom of its current " +
            "speed range - a gap on its own is not relief.</p>" +
            "<p>The climax waits out the break rather than being paid for with it: it sits on its " +
            "own clock, so without that the pause would simply cost you the rhythm leading up to " +
            "it. You edged, so you wait longer for it. There is a cooldown, or holding the key " +
            "down would turn the session into a nap - the button counts it down for you. Both the " +
            "pause length and the cooldown live in Settings &gt; Beat &amp; Rhythm, and the whole " +
            "thing can be switched off there.</p>" +
            "<h3>Climax Banner</h3>" +
            "<p>Slams in over the picture with the session's outcome (Cum / Ruined / Denied) " +
            "once it's decided.</p>" +
            "<h3>What actually happened</h3>" +
            "<p>Underneath it, three buttons: <b>I Came</b>, <b>I Ruined It</b>, <b>I Stopped</b>. " +
            "The app knows what it <i>told</i> you to do; only you know what you did. Being denied " +
            "and obeying, and being denied and doing it anyway, are the same announcement and " +
            "opposite sessions - and the difference only exists in your statistics if you say so.</p>" +
            "<p>They appear at fake climax cues exactly as they do at the real one. That is " +
            "deliberate: if they only turned up for the real thing they would <i>be</i> the " +
            "announcement, and a fake-out only works while it is indistinguishable. It also means " +
            "the app finds out whether you fell for one.</p>" +
            "<p>A denied climax stops the Strokemeter the instant it is announced - there is " +
            "nothing left to stroke to, and whatever you do next is entirely on you. The session " +
            "waits for your answer before showing the recap. Stopping a session yourself asks once, " +
            "too, because a session with no answer counts as one that never finished and drags " +
            "every average you are tracking. You can always decline to say, and you can switch the " +
            "whole question off in Settings &gt; Climax.</p>"),
        new("Characters",
            "<h3>Who is talking to you</h3>" +
            "<p>Zazurona, the app's own, or a character of your choice stands on the start screen, " +
            "and during a session she " +
            "watches you from a frame at the top right: every callout draws her closer, her face " +
            "follows what she says, the line appears in a speech bubble beside her, and she " +
            "strokes in time with the beat. The toy in her hand is the app's own - its colour is " +
            "yours to pick under Settings &gt; Look &gt; Toy colour.</p>" +
            "<h3>Bringing your own</h3>" +
            "<p>Any other character is a <b>.vrm</b> model - VRM 0.x or 1.0, the format VRoid " +
            "Studio exports and most avatar sites offer. Here is where yours go:</p>" +
            "<ol>" +
            "<li>Put your <code>.vrm</code> files together in a folder of their own, anywhere on " +
            "your machine - outside the app's folder, so they stay where they are when you unpack " +
            "a new version.</li>" +
            "<li>Open <b>Settings &gt; Look</b>, and next to <b>Your own models</b> press " +
            "<b>Choose Folder...</b> and pick that folder.</li>" +
            "<li>Every <code>.vrm</code> directly in it (not in folders inside it) now appears " +
            "under <b>On the start screen</b>, by its file name. Pick one and press <b>Save &amp; Close Settings</b>.</li>" +
            "</ol>" +
            "<p>A <code>.vrm</code> dropped into <code>res\\characters</code> inside the app's own " +
            "folder is offered as well - create that folder if it is not there - but it does not " +
            "come along when you unpack a new version.</p>" +
            "<p>Make your own in VRoid Studio, or use one whose author lets you download it. The " +
            "app does not read a model's terms of use for you: many authors rule out sexual use, " +
            "so check them before a model of someone else's goes in here.</p>" +
            "<h3>What to expect</h3>" +
            "<ul>" +
            "<li>Large models take a while to load - a very large one, minutes. The logo holds " +
            "her place until she fades in, and everything else works meanwhile.</li>" +
            "<li>Hair, tails and the like swing if the model was made with spring bones.</li>" +
            "<li>A model that takes the app down while it loads is switched off at the next " +
            "start, and the app tells you which one it was, so one broken file can never lock " +
            "you out. Pick it again to give it another try.</li>" +
            "<li><b>None</b> leaves the logo on the start screen; untick <b>Shown during a " +
            "session</b> to keep her out of your sessions. <b>Frame</b> picks a neon oval or an " +
            "old tube screen.</li>" +
            "<li>The folder you chose stays on this machine: it is never logged, and Privacy " +
            "&amp; Data shows it and forgets it on request.</li>" +
            "</ul>"),
        new("Languages & Tones",
            "<h3>Callouts are just JSON files</h3>" +
            "<p>Every teasing phrase you see comes from a JSON file under <code>res/callouts/</code>, " +
            "one folder per language and one file per tone inside it - " +
            "<code>en/flirty.json</code>, <code>de/dominant.json</code>, and so on. No programming " +
            "knowledge is needed to add or translate phrases.</p>" +
            "<h3>Language and tone are two separate choices</h3>" +
            "<p>You pick one language and as many tones as you like in " +
            "Settings &gt; Callouts. Tick several and the app mixes them evenly - each ticked tone " +
            "speaks about as often as the others, no matter how many phrases were written for it.</p>" +
            "<ul>" +
            "<li><b>Flirty</b> - playful and teasing. The app's original voice, and the default.</li>" +
            "<li><b>Shy</b> - timid and hesitant, a little embarrassed to be bossing you around.</li>" +
            "<li><b>Dominant</b> - commanding and certain. Orders, not suggestions.</li>" +
            "<li><b>Degrading</b> - humiliation play, and considerably harsher than the rest. It is " +
            "off unless you tick it yourself, and it never switches itself on.</li>" +
            "<li><b>Degrading (Hard)</b> - the same, turned all the way up: it goes after your size " +
            "and how little you measure up, start to finish. Opt-in, same as above.</li>" +
            "<li><b>Girlfriend Experience</b> - warm and affectionate, sweet to gently bossy, never " +
            "degrading.</li>" +
            "</ul>" +
            "<h3>Adding a new language</h3>" +
            "<ol>" +
            "<li>Copy an existing language folder, e.g. <code>en</code>, to a new folder named after " +
            "your language code (e.g. <code>es</code>), keeping the tone files you want inside it.</li>" +
            "<li>Translate the phrases in each array - or write entirely new ones, that's up to " +
            "you - and keep each file in its own tone's voice.</li>" +
            "<li>Keep every <b>Trigger Key</b> (the JSON keys like <code>beat_change_faster</code> " +
            "or <code>pause_start</code>) exactly as they are - only the phrases inside each " +
            "array should change. A misspelled key won't error, it'll just stay silent for that " +
            "event.</li>" +
            "<li>Only <code>flirty.json</code> is required; the rest are optional. A tone your " +
            "language doesn't have is simply skipped, and Settings marks it <i>(not in de)</i> so " +
            "you can see why it went quiet.</li>" +
            "</ol>" +
            "<p>Once the folder is saved under <code>res/callouts/</code>, it shows up automatically " +
            "in Settings &gt; Callouts &gt; Language - no restart or extra setup needed. Adding a new " +
            "tone works the same way in the other direction: one <code>&lt;tone&gt;.json</code> in " +
            "every language folder.</p>" +
            "<p>Adding new phrases to a language that already exists works the same way: open " +
            "the file for the language and tone you're writing for and append a string to the " +
            "relevant array.</p>" +
            "<h3>Or skip editing the app entirely</h3>" +
            "<p>Settings &gt; Callouts &gt; Manage Custom Phrase Files lets you point GoonerApp at " +
            "a JSON file anywhere on your own machine (same format as above) - pick which " +
            "language and tone it's for and add it, no need to touch anything inside the app " +
            "itself. Leave the tone on <i>Any tone</i> and it will be heard whatever mix you have " +
            "ticked. Add or remove files any time, changes apply immediately.</p>" +
            "<p>For the full Trigger Key reference and validation steps, see " +
            "<code>CONTRIBUTING.md</code> in the project repository.</p>"),
        new("Keyboard Shortcuts",
            "<h3>Playback</h3>" +
            "<ul>" +
            "<li><b>Ctrl+O</b> - Set/Change Gooning Folder</li>" +
            "<li><b>Right Arrow</b> - Next media (same as Skip in the dock)</li>" +
            "<li><b>Left Arrow</b> - Previous media (same as Previous in the dock)</li>" +
            "<li><b>Ctrl+Space</b> - Stop the session</li>" +
            "<li><b>E</b> - I reached my Edge: a pause right now, and a gentler rhythm behind " +
            "it. Hidden once the climax has been announced.</li>" +
            "</ul>" +
            "<h3>Audio &amp; Panic</h3>" +
            "<ul>" +
            "<li><b>M</b> - Toggle mute (beat sound and video audio together)</li>" +
            "<li><b>Space</b> - Panic: instantly minimizes the window, mutes audio and stops " +
            "any connected device. The device stays stopped until you press <b>Resume " +
            "Device</b> in the dock - the window comes back by accident often enough " +
            "that it must never restart on its own. Does not " +
            "stop the session, and doesn't auto-unmute when you come back - press <b>M</b> once " +
            "you're ready for sound. (Space always mutes and minimizes, it never restores.)</li>" +
            "</ul>" +
            "<h3>Window</h3>" +
            "<ul>" +
            "<li><b>F</b> or <b>F11</b> - Toggle fullscreen</li>" +
            "<li><b>Escape</b> - Leave fullscreen</li>" +
            "</ul>" +
            "<h3>App</h3>" +
            "<ul>" +
            "<li><b>Ctrl+S</b> - Open Settings</li>" +
            "<li><b>F1</b> - Open this Guide</li>" +
            "<li><b>Ctrl+Q</b> - Quit</li>" +
            "</ul>"),
        new("Privacy",
            "<h3>Everything stays on your machine</h3>" +
            "<p>GoonerApp runs 100% locally. Your media folders, session history, and settings " +
            "never leave your machine - there's no account to create, no login, and no " +
            "telemetry of any kind.</p>" +
            "<h3>No cloud, no calls home - with two opt-in exceptions</h3>" +
            "<p>Nothing in the app makes an outbound network request on its own. Loading your " +
            "folder, playing your files, tracking your stats - all of it happens entirely " +
            "offline, on-device.</p>" +
            "<p>The first exception is Check for Updates, at the foot of the start screen: only when you click it and " +
            "confirm the warning, GoonerApp sends a single request to GitHub.com to check the " +
            "latest release version. Nothing else is sent, and it never runs automatically.</p>" +
            "<p>The second is device output in <b>Settings &gt; Device</b>, for driving a " +
            "stroker through Intiface. It ships switched off, it switches itself off again at " +
            "every launch, and turning it on asks you first and spells out what goes over the " +
            "wire. The app knows no addresses of its own - it connects to the one you type in, " +
            "and if that is not this machine, those commands cross your network. What it sends " +
            "is the client name, device discovery and heartbeat messages, and movement and stop " +
            "commands. No media, no paths, no callouts, no session history.</p>" +
            "<h3>Where your data actually sits</h3>" +
            "<p>Staying on your machine still means it is written somewhere, so here it is. " +
            "Your session history, custom rhythm patterns, custom phrase files, saved sessions " +
            "and the media folders the picker remembers are plain JSON files in " +
            "<b>%LOCALAPPDATA%\\GoonerCock\\GoonerApp</b>. Your settings - sliders, toggles, " +
            "language - sit right next to them, in <b>settings.json</b>.</p>" +
            "<p>GoonerApp is portable, so deleting its folder does <i>not</i> remove any of " +
            "that. Open <b>Privacy &amp; Data</b> - on the start screen, or in the dock's menu - to see the exact paths on this " +
            "machine, open the data folder, or delete any of it - per category, so you can " +
            "wipe the remembered folder paths without losing your stats.</p>" +
            "<p>One thing more is written, by the graphics engine rather than the app: a " +
            "graphics cache of compiled shaders in <b>%APPDATA%\\Godot\\app_userdata\\GoonerApp</b>, " +
            "so the start and the first moments of a session do not stall. It holds nothing about " +
            "you or your media, and Privacy &amp; Data shows and deletes it too.</p>" +
            "<h3>The one thing that stores media paths</h3>" +
            "<p>Nothing the app writes contains the paths of your media - with one exception it " +
            "only makes when you ask for it. A session you <b>save</b> for replaying records " +
            "them, because that is how a replay finds the same files again. It is listed and " +
            "deletable in Privacy &amp; Data like everything else, and exporting one asks every " +
            "time whether the copy you hand on should carry them.</p>" +
            "<h3>The diagnostic log</h3>" +
            "<p>There is also an optional log file, <b>off by default</b>. It lives in the same " +
            "Privacy &amp; Data dialog - switch it on there only if something is misbehaving " +
            "and you want to see why, and pick how much it records (everything, problems " +
            "only, or errors only). It notes what the app is doing, which also means it " +
            "notes <i>when</i> you used it. It never contains the folders you play from; a " +
            "file that failed to load is named, nothing else is. It sits with your other " +
            "data and can be deleted on its own, right below the switch.</p>" +
            "<h3>What that means going forward</h3>" +
            "<p>Any future feature that would need the internet stays held to the same bar: " +
            "strictly opt-in and clearly disclosed here - never on by default, never silent.</p>"),
    ];

    // The topics in the language in use: the title, and every block of the page - a heading, a
    // paragraph, a list - looked up on its own, so a sentence reworded costs the translation of its
    // paragraph rather than of the whole topic.
    public static IReadOnlyList<GuideTopic> Translated() =>
        [.. Topics.Select(topic => new GuideTopic(L10n.T(topic.Title), string.Concat(RichText.Blocks(topic.Html).Select(L10n.T))))];
}
