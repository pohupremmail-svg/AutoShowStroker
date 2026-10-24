using System.Globalization;

namespace Gooner.Core.App;

public sealed record ChangelogEntry(string Version, string Html);

// The user-facing changelog the What's New panel shows.
//
// Every feat (or critical fix) version bump adds an entry, in the app's own flirty, teasing voice
// for the end user - not a commit-log dump: a short suggestive lead sentence, an inline <ul><li>
// list of the real features, then a one-line closing hook. The markup is Qt's rich text;
// RichText.ToBbcode turns it into what the engine draws.
//
// The entries up to 0.14.3 are the Python app's, word for word - they say what that app did, so
// a word in them that no longer fits (a menu, a folder) stays as it was. Every entry is translated
// (LocaleFilesTests).
public static class Changelog
{
    public static IReadOnlyList<ChangelogEntry> Entries { get; } =
    [
        new("0.1.0",
            "GoonerApp's first release is here to edge you properly." +
            "<ul>" +
            "<li>Randomized slideshow playback that syncs perfectly to a fully configurable " +
            "Strokemeter</li>" +
            "<li>Multilingual teasing callouts in English, German, and French, whispering you " +
            "along the whole way</li>" +
            "<li>Difficulty ramping that slowly tightens the screws as your session builds</li>" +
            "<li>A full climax announcement system - real, ruined, denied, and the occasional " +
            "cruel fake-out</li>" +
            "</ul>" +
            "Load up your folder and see how long you actually last."),
        new("0.2.0",
            "GoonerApp got dressed up for you - dark, neon, and dripping in pink." +
            "<ul>" +
            "<li>A full cyber-erotic theme glow-up: deep purple backgrounds, glowing pink " +
            "accents, curves everywhere</li>" +
            "<li>Settings reorganized into tabs so nothing falls off your screen mid-session</li>" +
            "<li>A one-click \"Reset to defaults\" on every tab, no fumbling required</li>" +
            "<li>This very \"What's New\" popup, so you never miss what's new to play with</li>" +
            "</ul>" +
            "Go on, undress the new Settings menu and see for yourself."),
        new("0.3.0",
            "GoonerApp hands you the sticks - build your own rhythm from scratch." +
            "<ul>" +
            "<li>A brand-new Pattern Editor: drag each step's bar to set how long it lingers, " +
            "1 (longest) to 4 (shortest)</li>" +
            "<li>A dedicated Beat/Pause button per step, so a pause can tease just as long " +
            "(or as short) as you want it to</li>" +
            "<li>Live preview playback, so you can hear exactly what you're building before " +
            "you commit to it</li>" +
            "<li>Your own patterns sit right alongside the built-in ones in the Active Rhythms " +
            "list, ready to be picked at random</li>" +
            "</ul>" +
            "Head to Settings > Beat &amp; Rhythm > Manage Custom Patterns and compose your own edge."),
        new("0.4.0",
            "GoonerApp now remembers every session you've ever survived - and isn't shy about it." +
            "<ul>" +
            "<li>Break a personal best and the end-of-session recap throws up a glowing " +
            "\"New Personal Record\" card for it, right on the spot</li>" +
            "<li>A new Statistics menu holds your full history: sessions played, all-time bests, " +
            "and a trend chart charting how your stamina climbs over time</li>" +
            "<li>Fakeouts now count toward something - survive enough of them and that's a record " +
            "too</li>" +
            "</ul>" +
            "Check the Statistics menu after your next session and watch the line go up."),
        new("0.5.0",
            "GoonerApp finally lets you window-shop before you commit." +
            "<ul>" +
            "<li>A brand-new folder picker replaces the plain Windows dialog - stack up as many " +
            "folders as you want in one go, each weighted equally in the preview</li>" +
            "<li>A live thumbnail grid teases a random sample before you hit Start, so you're " +
            "never loading in blind again</li>" +
            "<li>Gifs already play right in the grid, and videos can too - flip on \"Animate " +
            "video clips\" for a looping 5-second peek at each one</li>" +
            "<li>Your last-used folders are remembered, ready the next time you open the picker</li>" +
            "</ul>" +
            "Open the folder picker and see what's waiting for you before you dive in."),
        new("0.5.1",
            "GoonerApp wants to stay close, and finally tells you how it all works." +
            "<ul>" +
            "<li>A new Socials menu with a one-click Join Discord, so the community is never " +
            "more than a click away</li>" +
            "<li>A brand-new Guide under Help - a plain-language breakdown of what the beat " +
            "pattern numbers actually mean, straight from the source</li>" +
            "<li>The Guide also walks you through adding a new callout language, no Python " +
            "required</li>" +
            "<li>Built to grow - more guide topics will slot in right alongside these as the app " +
            "gets bigger</li>" +
            "</ul>" +
            "Pop open Help &gt; Guide and finally learn what makes this thing tick."),
        new("0.6.0",
            "GoonerApp levels up on three fronts at once - discoverability, control, and trust." +
            "<ul>" +
            "<li>Every keyboard shortcut is finally documented - a new Keyboard Shortcuts tab in " +
            "the Guide, plus the README, list them all in one place</li>" +
            "<li>Two shortcuts that were missing outright: Ctrl+O opens/changes your folder, and " +
            "F1 pops the Guide open instantly</li>" +
            "<li>A new Mute button (or just hit M) silences the beat sound and video audio " +
            "together, instantly</li>" +
            "<li>Panic Mode - tap Space and the window minimizes and goes silent in one move, no " +
            "questions asked, no session lost</li>" +
            "<li>A new Privacy tab in the Guide (and right at the top of the README) spells it " +
            "out plainly: everything runs 100% locally, nothing ever phones home</li>" +
            "</ul>" +
            "Pop open Help &gt; Guide - it's got a lot more to say for itself now."),
        new("0.6.1",
            "GoonerApp finally makes an entrance instead of just barging in." +
            "<ul>" +
            "<li>A glowing logo fade-in greets you on launch now - the neon mark easing in, " +
            "holding for a beat, then dissolving away right before your session starts</li>" +
            "<li>Not in the mood for a tease before the tease? A new \"Show startup splash " +
            "animation\" toggle in Settings &gt; Playback turns it off instantly</li>" +
            "</ul>" +
            "Watch it once, then decide if you want the intro every time."),
        new("0.6.2",
            "GoonerApp now taunts you the moment a record is actually within reach." +
            "<ul>" +
            "<li>A glowing badge flashes up in the corner once you're closing in on a personal " +
            "best - total beats, duration, active speed, or fakeouts survived, whichever you're " +
            "nearest to breaking</li>" +
            "<li>Cross the line and it flips straight to \"New Record!\" without waiting for the " +
            "recap screen</li>" +
            "<li>Stays out of your way otherwise - no badge, no clutter, until you're actually " +
            "close</li>" +
            "<li>A new \"Show live personal-record chase\" toggle in Settings &gt; Playback turns " +
            "it off if you'd rather be surprised at the end</li>" +
            "</ul>" +
            "Keep playing - it'll let you know when it's time to get excited."),
        new("0.6.3",
            "GoonerApp can finally tell you when it's grown - if you ask nicely first." +
            "<ul>" +
            "<li>A new \"Check for Updates...\" action under Help asks GitHub, once, whether a " +
            "newer version exists</li>" +
            "<li>Always asks permission first - a clear warning pops up every single time before " +
            "anything gets sent, no exceptions</li>" +
            "<li>Nothing automatic, nothing silent - no background checks, no Settings toggle to " +
            "forget about, just a button you press when you're curious</li>" +
            "<li>Finds one? A one-click link straight to the Releases page</li>" +
            "</ul>" +
            "Pop open Help when you're curious - it'll only ever speak up if you ask."),
        new("0.6.4",
            "GoonerApp finally lets your own words into the mix, no code required." +
            "<ul>" +
            "<li>Settings &gt; Callouts &gt; Manage Custom Phrase Files lets you point the app at " +
            "your own phrase file, anywhere on your machine - same simple format the built-in " +
            "ones already use</li>" +
            "<li>Pick the language it's for, add it, and it's live immediately - no restart</li>" +
            "<li>Your phrases join the built-in ones in the mix, never replace them</li>" +
            "<li>Load as many files as you like, and drop any of them just as easily</li>" +
            "</ul>" +
            "Write it once, save it anywhere, and let GoonerApp read it back to you."),
        new("0.7.0",
            "GoonerApp's biggest single day yet, all bundled into one real release." +
            "<ul>" +
            "<li>A glowing splash greets you on launch now - the neon mark easing in before every " +
            "session</li>" +
            "<li>A live record-chase badge flashes up the moment you're closing in on (or " +
            "breaking) a personal best</li>" +
            "<li>Help &gt; Check for Updates - ask GitHub, on your terms, whether a newer version " +
            "exists</li>" +
            "<li>Settings &gt; Callouts &gt; Manage Custom Phrase Files - load your own teasing " +
            "lines from anywhere on your machine, no code required</li>" +
            "</ul>" +
            "Four new tricks in one build - go find all of them."),
        new("0.7.1",
            "GoonerApp now keeps its own quiet count of exactly how long you've been at it." +
            "<ul>" +
            "<li>A small glowing clock ticks away in the top-left the whole session, wall-clock " +
            "time, pauses included</li>" +
            "<li>A new \"Show session timer\" toggle in Settings &gt; Playback hides it if you'd " +
            "rather not watch the numbers climb</li>" +
            "<li>A new Guide tab, \"On-Screen Display\", finally explains everything glowing on " +
            "your screen - the timer and the record-chase badge alike</li>" +
            "</ul>" +
            "However long you think you've lasted, now you'll actually know."),
        new("0.8.0",
            "The Strokemeter stopped just blinking at you - now it shows you what's coming." +
            "<ul>" +
            "<li>A whole new animated Strokemeter: beats glide in from the right and land on the " +
            "hit line at the exact moment you hear them, so you feel every one approaching before " +
            "it takes you</li>" +
            "<li>A rhythm's silent steps read as the gaps between notes, so you can see the shape " +
            "of what's being done to you instead of guessing at it</li>" +
            "<li>Every change of pattern announces itself with a sweep of light across the track</li>" +
            "<li>Your session history and custom patterns moved out of the Windows registry into " +
            "proper files in your AppData folder - they carry themselves over on first launch, " +
            "nothing is lost, and your history is no longer capped at 200 sessions</li>" +
            "</ul>" +
            "Watch them come at you now. See how long you keep up."),
        new("0.10.0",
            "She only ever had one voice. Now you get to choose who is whispering at you - " +
            "and you do not have to choose just one." +
            "<ul>" +
            "<li><b>Eleven tones to pick from:</b> Flirty, Shy, Bratty, Drunk, Girlfriend " +
            "Experience, Nurturing, Dominant, Sadistic, Degrading, Degrading (Hard) and " +
            "Clinical - the same session, eleven very different voices in your ear</li>" +
            "<li><b>Tick as many as you like.</b> Tones mix evenly, so a sweet little apology " +
            "and a flat order can land back to back in the same session</li>" +
            "<li>Over 1700 brand-new lines. English has all eleven tones; German and French " +
            "have six so far, and Settings tells you which ones your language is missing</li>" +
            "<li>The mean ones are as mean as they sound - Sadistic enjoys the suffering, and " +
            "the hard Degrading goes after your size from the first line to the last. They " +
            "stay switched off until you tick them yourself. Nothing turns them on for you</li>" +
            "<li>Your old voice is still there, untouched, under <i>Flirty</i> - change nothing " +
            "and nothing changes</li>" +
            "<li>Your own phrase files can now be pinned to one tone, or left to speak in all " +
            "of them</li>" +
            "</ul>" +
            "Settings &gt; Callouts. Go find out which one of her gets to you."),
        new("0.11.0",
            "She used to make it up as she went. Now she knows exactly how your session ends " +
            "before you have finished the first stroke." +
            "<ul>" +
            "<li><b>She speeds up before she lets you finish.</b> The rhythm going into your " +
            "climax is pinned to the fastest she is allowed to go, and it is still going when " +
            "she tells you. No more stumbling over the edge on some lazy pattern</li>" +
            "<li><b>You decide how long she draws it out.</b> The old \"climax chance\" guessing " +
            "game is gone - set the earliest and latest point in the session she may say it, " +
            "and she picks her moment in between. Want her to let you finish before it even " +
            "gets hard? Untick \"Climax only after ramping finishes\" and she will</li>" +
            "<li>The difficulty ramp is a real curve now, climbing segment by segment instead " +
            "of rolling the dice again every time she changes her mind</li>" +
            "<li>Nothing interrupts her once she has said it - no new rhythm, no pause, no " +
            "chatter over the top. The beat you finish on is the beat she chose</li>" +
            "<li>The Strokemeter can see past her next change, so the notes keep flowing " +
            "towards you instead of stopping where her plan used to run out - and they now " +
            "fly in during a pause too, instead of snapping into place when it ends</li>" +
            "<li>Saving your settings mid-session no longer rips the beat out from under you - " +
            "she finishes what she started, then does it your way</li>" +
            "<li>\"Beat change chance\" is gone. Beat duration means what it says now</li>" +
            "</ul>" +
            "She has already decided. You just do not know it yet."),
        new("0.12.0",
            "Every session you have ever finished is gone the moment you look away. Not this one." +
            "<ul>" +
            "<li><b>Scrub back through what she just did to you.</b> A new Session Explorer " +
            "button on your end-of-session stats opens the whole thing as a seek bar - drag " +
            "along it and watch it play back</li>" +
            "<li><b>See what you were looking at.</b> Wherever you stop, the picture that was " +
            "on screen at that exact second comes back up, with the rhythm she was using on " +
            "you and how fast</li>" +
            "<li>That one you could not get enough of? Find it on the bar, hit <b>Show in " +
            "folder</b>, and it is yours. No more scrolling your whole collection hoping to " +
            "recognise it</li>" +
            "<li>The shape of the session is right there: the long slow stretches, every pause " +
            "she made you sit through, and the fast run-in marked where she finally let you</li>" +
            "<li>Videos give up a frame too, fetched quietly in the background so the bar never " +
            "stutters under your hand</li>" +
            "<li>None of it is written down. It lives only as long as the session you just " +
            "finished and never touches your disk - what you watch stays nobody else's business, " +
            "including the app's</li>" +
            "</ul>" +
            "Finish. Then go back and watch yourself lose it."),
        new("0.9.0",
            "Every corner of GoonerApp got picked apart and put back together. It takes more, " +
            "breaks less, and finally tells you what it knows about you." +
            "<ul>" +
            "<li>The crashes are gone. Leaving yourself no active rhythm, setting a pause " +
            "minimum above its maximum, or a damaged pattern file used to end your session on " +
            "the spot - now the app either talks you out of it or simply carries on</li>" +
            "<li>Stopping actually stops. A video used to keep playing behind your stats and " +
            "then start the slideshow up again on its own, with no session behind it</li>" +
            "<li>Walking out counts. Closing the window mid-session records it instead of " +
            "throwing the whole thing away</li>" +
            "<li>A clip your machine can't decode gets skipped instead of leaving you staring " +
            "at black until you take over</li>" +
            "<li>Everything is quicker: picking folders no longer locks up for seconds while " +
            "it previews your clips, big collections scan several times faster, large photos " +
            "land sooner, and the beat itself keeps stricter time</li>" +
            "<li>Callouts actually talk to you on a fresh install, and turning the volume down " +
            "finally survives a restart</li>" +
            "<li><b>New - Help &gt; Privacy &amp; Data:</b> exactly where everything about you " +
            "is kept, a button straight to the folder, and deletion by category. The folders " +
            "you pick no longer linger in the Windows registry, and your data moved off the " +
            "roaming half of your profile, where a work laptop could quietly copy it</li>" +
            "<li><b>New - an optional diagnostic log,</b> off unless you switch it on, so a " +
            "problem you report can be traced. It never writes down the folders you play " +
            "from</li>" +
            "</ul>" +
            "Go on then. See if you can still find the seams."),
        new("0.13.4",
            "The session that wrecked you does not have to be a one-off any more. Keep it. " +
            "Take it again. Hand it to someone else and watch them try." +
            "<ul>" +
            "<li><b>Save the session you just survived.</b> A Save button in the Session " +
            "Explorer keeps the whole run - every rhythm and how fast it drove you, every " +
            "pause, every fake-out, and the exact second you were finally allowed</li>" +
            "<li><b>Take it again, beat for beat.</b> Sessions &gt; Saved Sessions plays it " +
            "back exactly as it went, right down to how long each picture was allowed to keep " +
            "you. The callouts stay random, so she still surprises you - but the beat is " +
            "identical, which is the only way to find out whether you have actually gotten " +
            "better at this</li>" +
            "<li><b>The one that beat you gets another go.</b> Saved a session you did not " +
            "survive? It is marked <i>Stopped early</i>, and replaying it puts you through the " +
            "recording again and then keeps going on its own - so this time there is something " +
            "at the end of it</li>" +
            "<li><b>Give one away.</b> Export a session to a file, send it to whoever you want " +
            "to see struggle, import the one they send back. Missing their files? It replays " +
            "against your own collection instead - their pace, your pictures</li>" +
            "<li><b>And it asks before it tells on you.</b> Exporting asks every single time " +
            "whether to include your file paths, and says plainly what they give away. Saved " +
            "sessions are listed and deletable in Help &gt; Privacy &amp; Data like everything " +
            "else</li>" +
            "<li><b>And when you are about to lose it, say so.</b> Press <i>E</i> and she " +
            "gives you a breather - the beat stops, and comes back at the gentlest speed she " +
            "is currently willing to allow. The climax waits out your break instead of being " +
            "paid for with it, though. You edged, so you wait longer for it</li>" +
            "<li><b>And she finally asks what you actually did.</b> Every climax cue now offers " +
            "<i>I Came</i>, <i>I Ruined It</i>, <i>I Stopped</i> - because she only ever knew " +
            "what she <i>told</i> you to do. Obeying a denial and ignoring one used to look " +
            "identical in your stats. They do not any more</li>" +
            "<li><b>And the fake-outs ask too.</b> Same three buttons, exactly the same way, so " +
            "they never become the tell that gives one away - which also means she now finds " +
            "out when you fell for one, and has something to say about it. A denial cuts the " +
            "Strokemeter dead on the spot, too. Whatever you do after that is entirely on " +
            "you</li>" +
            "<li><b>And now there is a wall to put it all on.</b> Statistics &gt; Achievements " +
            "shows every one there is - including the ones you have not earned, with what " +
            "they want and how close you got. A few stay <i>???</i> until you trip over them. " +
            "Obedience only counts when it costs you something, naturally</li>" +
            "<li><b>And you can have one written for you.</b> A session is just a file, so an " +
            "AI can compose one - the format and a ready-made skill now ship in the repo. Ask " +
            "for forty punishing minutes that deny you at the end and import what it hands " +
            "back. It never touches your collection: a written session says only <i>when</i> " +
            "the picture changes, never which one</li>" +
            "</ul>" +
            "Find the one that broke you. Then find out if it still can."),
        new("0.14.0",
            "GoonerApp stops asking you to do the stroking." +
            "<ul>" +
            "<li><b>Your stroker moves with the Strokemeter.</b> Point <b>Settings &gt; Device</b> " +
            "at Intiface Central and every beat it plays, your device plays too - the same " +
            "rhythm, the same pauses, the same run-in to your climax, with the pattern change " +
            "it is about to throw at you already in hand. It does not chase the beat, it " +
            "arrives on it</li>" +
            "<li><b>Set how far it goes.</b> A minimum and a maximum, so the stroke is yours " +
            "and not the hardware's idea of one. Test both ends before you start</li>" +
            "<li><b>Space still stops everything.</b> The panic key mutes you, hides the window " +
            "<i>and</i> stops the device, in that order and immediately. So does ending the " +
            "session, losing the connection, and closing the app. Nothing starts moving " +
            "again by itself afterwards - a <b>Resume Device</b> button sits next to Mute " +
            "and waits for you to mean it</li>" +
            "<li><b>Off every single time you start.</b> Not remembered, not assumed - device " +
            "output is a switch you throw deliberately, and throwing it tells you exactly what " +
            "leaves this machine before anything does. The app ships with no address of its " +
            "own; the only one it will ever talk to is the one you type in yourself</li>" +
            "<li><b>And it is a folder you can delete.</b> No stroker, no interest? Remove " +
            "<code>src/plugins/intiface</code> and the app will not so much as blink</li>" +
            "</ul>" +
            "Marked Beta, because nobody has held one of these while it ran yet. Be the first, " +
            "and tell us on the Discord how it went."),
        new("0.14.1",
            "Stop now means stop." +
            "<ul>" +
            "<li><b>You hit Stop, it stops.</b> The question about how it ended used to arrive " +
            "while the beat was still going and the clip still running behind it - so the " +
            "longer you took to answer, the longer you were still being worked. Everything " +
            "falls silent first, and then you are asked</li>" +
            "</ul>" +
            "Your answer still counts for exactly as much as it did."),
        new("0.14.2",
            "She never stopped talking. You just could not hear her over the video." +
            "<ul>" +
            "<li><b>Callouts are back during clips.</b> Every line she picked while a video was " +
            "playing was thrown away behind the picture - so the moment your attention was most " +
            "held, she went quiet. She has been talking the whole time. Now you get to be told " +
            "about it</li>" +
            "<li>The session clock and your record chase were hidden the same way, and they are " +
            "back too</li>" +
            "</ul>" +
            "Found by AppropriateLynx, who also worked out why."),
        new("0.14.3",
            "If something breaks now, it tells you. It does not just walk out on you." +
            "<ul>" +
            "<li><b>An unexpected error no longer ends your session.</b> The app used to vanish " +
            "mid-stroke without a word - no message, no trace, nothing to report. Now it stays " +
            "up, says what went wrong, and lets you decide whether to carry on</li>" +
            "<li><b>And it hands you the details.</b> The technical part sits behind a button, " +
            "ready to copy straight into the Discord. Nothing is sent anywhere - it never was, " +
            "and it still is not</li>" +
            "</ul>" +
            "The same fault only interrupts you once, however often it happens."),
        new("0.15.0",
            "GoonerApp has a whole new body. Take a good look - she wants you to." +
            "<ul>" +
            "<li><b>Rebuilt from the ground up on a real game engine.</b> Every note lands " +
            "on its click to the frame, every effect runs on the graphics card, and the rhythm " +
            "you know is exactly the rhythm you get</li>" +
            "<li><b>She can finally look at you.</b> Bring your own <code>.vrm</code> character - " +
            "she waits for you on the start screen, watches you from her frame through the whole " +
            "session, comes closer with every line she says, and strokes right along with your " +
            "beat. Where to put her is in the Guide, under Characters</li>" +
            "<li><b>Four ways to watch the beat come for you:</b> a Highway racing at you, a " +
            "Hypno Ring that wanders across the picture and drags your eyes along - blurring, " +
            "pixelating or darkening the rest if you ask nicely - a Stream, or a Tunnel to fall " +
            "into. Pick it under Settings &gt; Look, with Neon or Boudoir colours and effects " +
            "from subtle to wild</li>" +
            "<li><b>The climax hits the whole screen</b> - a flash, a shake, colours tearing " +
            "apart. A fake one hits exactly as hard, right up until it does not</li>" +
            "<li><b>No more menu bar.</b> A start screen greets you in her voice, and during a " +
            "session your controls float on glass you can drag anywhere, or throw out of your " +
            "way</li>" +
            "<li><b>The app speaks German and French now too</b> - its own words, not just her " +
            "callouts. Settings &gt; Playback &gt; App Language</li>" +
            "<li><b>It comes as a folder now.</b> Unpack it anywhere: your settings, history, " +
            "patterns and saved sessions come along on their own the first time you start it, " +
            "and the old GoonerApp.exe can go</li>" +
            "</ul>" +
            "Same rhythm, same rules, a lot more to look at. Lean back."),
    ];

    public static IReadOnlyList<int> ParseVersion(string version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return [.. version.Split('.').Select(part => int.Parse(part, NumberStyles.Integer, CultureInfo.InvariantCulture))];
    }

    // The entries newer than the last one seen, up to the running version, in changelog order.
    public static IReadOnlyList<ChangelogEntry> EntriesSince(string lastSeenVersion, string currentVersion, IReadOnlyList<ChangelogEntry>? changelog = null)
    {
        changelog ??= Entries;
        var lastSeen = string.IsNullOrEmpty(lastSeenVersion) ? [0] : ParseVersion(lastSeenVersion);
        var current = ParseVersion(currentVersion);
        return [.. changelog.Where(entry =>
        {
            var version = ParseVersion(entry.Version);
            return Compare(lastSeen, version) < 0 && Compare(version, current) <= 0;
        })];
    }

    // Python's tuple order: element by element, and a prefix before what continues it.
    public static int Compare(IReadOnlyList<int> a, IReadOnlyList<int> b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        for (var i = 0; i < Math.Min(a.Count, b.Count); i++)
            if (a[i] != b[i]) return a[i].CompareTo(b[i]);
        return a.Count.CompareTo(b.Count);
    }
}
