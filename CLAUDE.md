# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

GoonerApp ("Auto Hero Generation", GitHub repo name `AutoShowStroker`): a desktop app that turns a
local folder of images/GIFs/videos into a randomized slideshow synced to a rhythm generator (the
"Strokemeter"), with multilingual text callouts, a climax system, device output through Intiface,
and an optional VRM character of the user's own. Built on **Godot 4.7 + C# (.NET 10)**, in
`engine/`.

Until 0.14.3 it was a PyQt6 app (`src/`, `main.py`, PyInstaller); 0.15.0 replaced it with this
rebuild, and the Python code is gone from the tree (git history has it). What is left of it on
purpose: an updating user's profile is carried over at the first start (see the Layout below), the
file formats stay readable, the changelog entries up to 0.14.3 are its own, and many comments and
test names still say which Python module or test a piece was ported from. Where the engine behaves
differently from the Python app, the comment says so ("the Python app keeps it"), so it is never
taken for an accident.

## Layout

Everything lives in `engine/`; `res/` (phrase files, translations, icons, the click) is beside it
at the repo root.

- `Gooner.Core/` — the app's logic, data and file formats. **No reference to Godot**, so it is
  tested with plain `dotnet test` in seconds and never needs an engine to run. Anything a test
  without an engine could check belongs here.
- `Gooner.Core.Tests/` — xUnit, test names in `snake_case` (the ported ones mirror the Python test
  they came from).
- `Gooner.Harness/` — a console app that plays a whole session without a screen
  (`dotnet run --project engine/Gooner.Harness -- --seed 42 --climax 150:240`). Developer tool,
  ships with nothing. The wiring it runs is `Gooner.Core.Simulation.HeadlessSession`, which is
  also the reference for how the engine wires the handlers together.
- `godot/` — the Godot project (`Gooner.Engine`, Godot 4.7.2 .NET). It references `Gooner.Core`
  and adds only picture, sound, input and effects; anything a test without an engine could check
  belongs in the core instead. It has its own `Gooner.Engine.sln` because Godot's build and
  export look for `<assembly name>.sln` beside `project.godot`; `GoonerEngine.sln` holds it too,
  so one build compiles everything.
- The window (`godot/scripts/Main.cs`) draws `Gooner.Core.App.GoonerApp`'s state and calls into
  it; the lifecycle, the wiring and every rule about what may happen when live in the core, with
  the ported tests of `test_gooner_app.py`. A widget here holds no state the core does not.
- **An existing install is carried over at startup** (`Gooner.Core.Migration.ProfileMigration`,
  before the stores are built): what is left in the old roaming folder moves out, as
  `user_data.py` moves it; every value in the Python app's registry key
  (`HKCU\Software\GoonerCock\GoonerApp`) goes into `settings.json`; the four data files that once
  lived in the registry go into their files; the keys of removed features are dropped. A value
  leaves the registry only once its copy reads back from the file, and what cannot be read stays
  there - the `user_data.py` rule. The key goes once it is empty. `QSettingsRegistry` reads values
  as QSettings does (checked against bytes a real PyQt6 QSettings wrote, while both apps were
  compared).
  The import runs before there is a log, so `GoonerApp` logs its report. It runs on the app's own
  data folder only: a run with `--data-dir` imports nothing unless told where from, which is how a
  copy of a real profile is tried out (`--legacy-registry=<key under HKCU>`,
  `--legacy-data-dir=<folder>`). **Never point `--legacy-registry` at the app's own key, and never
  start a development build without `--data-dir`** - it would carry the real profile over, and the
  import deletes what it carries.
- **Video is the FFmpeg extension EIRTeam.FFmpeg**, in `godot/addons/ffmpeg/`, committed as the
  autobuild pinned in phase 0 left it (commit `84ea333`; release 1.1.4 never converts YUV on Godot
  4.7) - plain git, not LFS (the user's call, 2026-10-17: 14 MB that never change). The stage
  reaches the extension by class name, so a build without it still runs: every clip reports
  unplayable and the player moves on. Its FFmpeg is an **LGPL v3** build (`--enable-version3`, no
  GPL, no nonfree; H.264, MPEG-4, VP8/9, AAC, MP3, Opus, Vorbis - no HEVC); the wrapper is MIT.
  The package carries the LGPLv3/GPLv3 texts and the wrapper's MIT text (`engine/licenses`), and
  every release has the FFmpeg source of exactly that revision and the extension's source attached
  (`engine/third_party/ffmpeg`, see its README). The libraries have others linked in (libvorbis,
  libogg, libopus, libiconv, zlib, the OpenCL loader, soxr, the MinGW-w64 runtime, winpthreads -
  read off the binaries, not the configure line): each has its notice, and the two under the LGPL
  (libiconv, soxr) go out with their source and the build scripts (the user's call, 2026-10-24: keep
  this FFmpeg and add the notices rather than build our own). A new build of the extension replaces
  all of it together; `Gooner.Build.Tests` and `LicenseShelfTests` fail until the archives and
  notices follow.
- **A picture or a GIF is never read on the main thread** (`Stage/MediaStage`): the player names the
  medium that comes next (`IMediaStage.Prepare`, from `PlaylistPlayer`; a replay's next recorded
  path through `SessionScript.PeekNextMediaPath`), and the stage reads and scales it on a worker
  while the current one is up; one not got ready ahead goes up when its worker is done, the last one
  staying up meanwhile. Read in the frame, a 24-megapixel photo stalled it 0.8 s (measured with the
  wall clock - Godot's `delta` is capped near 140 ms and hides it), the beat track and her hand
  jumping by as much. **A clip is not got ready**: EIRTeam.FFmpeg makes its texture as it opens the
  file, which only the render thread may do (`texture_clear` errors, a failing smoke run), and an
  open costs 10-40 ms. Scaling halves first (`ShrinkX2`) while the picture is twice too big, then
  Lanczos: Lanczos alone took half a second on 24 MP.
- **Every dialog is a panel** (`godot/scripts/Panels/`), shown by the `PanelHost`
  over the whole window - start screen included, as a modal dialog blocked everything behind it -
  while the session plays on behind it. Panels are frosted glass (`Ui/Glass.cs`,
  `shaders/glass.gdshader`) and can be dragged by their title and thrown. Panels stack; Esc and the
  corner cross run the panel's own answer to being dismissed (`OnReject`; a question's escape
  button). Nothing waits on a panel the way `QDialog.exec()` did: a question calls back.
  `Confirm.Ask`/`Confirm.Tell` are `QMessageBox`.
  The pattern for each dialog: its logic is a model in `Gooner.Core.Panels` (`SettingsForm`,
  `StatisticsReport`, `PrivacyData`, `SavedSessionsShelf`, `SessionExplorer`, `MediaFolderPicker`,
  ...) carrying the ported tests of the dialog's Python test file; the panel only draws it and
  writes into it.
- **Panic is read in `_Input`, before any control**, so a focused button in a panel cannot take
  Space for itself; only a text field keeps its spaces.
- **There is no menu bar and no button row** (phase 7). Between sessions the **start screen**
  (`Ui/StartScreen.cs`) has big cards for what one comes to do and every other menu entry along its
  foot; during a session a **glass dock** (`Ui/Dock.cs`) floats over the picture - draggable,
  throwable, fading while the pointer rests - with the session's buttons and a menu panel holding
  the rest. The accelerators are keys (Ctrl+S settings, F1 guide, Ctrl+Q quit, Ctrl+O folder).
  `GuideTests` fails while any text still sends the user to a menu, a footer or a button row the
  window does not have.
- **The Guide** (`Gooner.Core.Panels.Guide`) was ported from the Python app's and is this app's own
  now: change it where it is wrong, and translate what changed. **The changelog entries up to
  0.14.3** are the Python app's, word for word - they say what that app did.
- **Device output (Intiface) is `Gooner.Core.Devices`.** The connection is an actor
  (`IntifaceWorker`: one loop, one step at a time, as the Qt thread was), reporting to the window
  through `GoonerAppOptions.Post` (`CallDeferred`). The user's rules stand: **no address ships with
  the app**, and output is **off at every launch** until switched on, after a consent that names
  the address. Tests run against a Buttplug server faked on loopback
  (`Gooner.Core.Tests/Support/FakeIntifaceServer`); a test that switches output on uses a closed
  loopback port, so nothing is resolved or contacted. A development run can drive a server of its
  own: `-- --smoke-session ... --device=ws://127.0.0.1:<port>` (no consent question - never for a
  user).
- **An exception nobody catches** is caught by Godot and reported as an engine error;
  `CrashLogger` (registered with `OS.AddLogger` first thing in `_Ready`) picks out the .NET ones
  and hands them to `Gooner.Core.App.CrashReporter`: logged first, shown second, once per fault.
  The app carries on.
- **The licenses** are in `engine/licenses/` (the notices, GPLv3/LGPLv3, the .NET runtime's, every
  library linked into FFmpeg), beside the executable once exported and shown under Third-Party
  Licenses; Godot's are read from the engine itself. `LicenseShelfTests` keep the notices in step
  with the Godot SDK, the .NET version and the FFmpeg DLLs. **A dependency bump is not done until
  the notices say so.**
- `res/` (callouts, click, icons) is read **from disk**, never through Godot's `res://`:
  `Gooner.Core.Data.ResourceRoot` finds the repo's own copy from a dev run and the one beside the
  executable once exported. The files never go through Godot's importer.

## The look (phase 7)

- **The look is settings** (`Gooner.Core.App.Look`, group `Look`): the note track (`highway`,
  `ring`, `stream`, `tunnel`), the colour scheme, the effect strength, whether the Hypno Ring
  wanders and what it does to the picture around it (`censor`: off, blur, mosaic, dark). A stored
  choice this build does not know reads as the default. The Settings panel shows them on a Look tab.
- **Colour schemes are `Gooner.Core.App.Palette.Schemes`** - Neon (the Python app's palette, unchanged)
  and Boudoir. `Palette.Use` switches; everything asks `Palette` when it draws or builds, the window
  rebuilds the theme and refreshes the glass when the setting changes. In every scheme the three
  climax outcomes stay apart in colour (`PaletteTests`). The scheme also names the display face of
  the big words (`UiTheme.Display`).
- **The note track is drawn from `BeatHandler.Forecast`**: the exact note moments the click gets
  (`UpcomingNotes` up to a pause) plus the stretches of the plan - so coming pauses and changes of
  rhythm fly in with the notes, and the notes behind a pause are known. The geometry of each track
  is in `Gooner.Core.Track`, and every track puts a note on its beat on the hit zone
  (`TrackGeometryTests`) - that, and drawing from the click's own moments, is what lands a note on
  its click. `godot/scripts/Track/NoteTrackView` paints it on two layers (normal and additive).
- **A finale is drawn like any other rhythm**, and the climax overlay (`Fx/ClimaxOverlay`) reacts only
  to what `ClimaxBanner` says: a fake climax is "cum" until it is revealed, so it runs the real one's
  effects until then. Anything that painted the run-in differently would tell the user which climax
  is the real one.
- **The effect layer is sprites and shaders, not HDR**: glow is one radial texture tinted and drawn
  additively (`Fx/Glow`), particles are drawn by hand (`Fx/Particles`, on their own time so the
  climax can slow them - never the session clock). `shaders/stage_filter.gdshader` censors round the
  Hypno Ring and splits the colours at the climax; it is not drawn at all while there is nothing to
  do, so the screen is not copied for nothing.

- **The start screen speaks in the app's voice**: the line under the name is a `main_menu` line of
  the phrase files, in the language and tones picked (`CalloutHandler.TitleLine`), new each time the
  screen comes up - shown whether callouts are on or not.
- **The window opens full screen the first time** and after that as it was last left
  (`Gooner.Core.App.WindowState`, remembered the moment the user switches).

## The app's own language

- **The app's own words come in English, German and French** (`Gooner.Core.Localization`), picked
  under Settings > Playback > App Language - apart from the language the callouts speak in. It is
  English until the user picks another, whatever Windows is set to (the user's call). `UiLanguage`
  (group `Ui`) holds the choice; `GoonerApp` applies it before it builds anything that words
  something, and the window builds the start screen, the dock and the answers under the climax
  again when it changes (`Main.ApplyLanguage`) - a panel is worded when it opens.
- **English is the code.** Every text is written in English where it is used and looked up in
  `res/locales/<language>/*.json` (English text -> translation, every file of the folder together);
  what a catalogue lacks stays English. So the English app is exactly the code.
- **Only literals go in**: `L10n.T("...")`; `L10n.F("... {0}", value)` for a text with values in it,
  never a `$"..."` into L10n; `L10n.P(count, one, other)` for a count, where French takes the
  singular for none as well (a singular that shows the number says `{0}`, not `1`); `L10n.N("...")`
  marks a name that is also stored or compared - a settings tab, a rhythm, a tone - which is
  translated (`T`) only where it is shown. A static table of display texts is translated where it
  is read, never in its initializer, which would keep whatever language was in use first. Log lines
  are never translated.
- **`LocaleFilesTests` holds every catalogue to the code**: `SourceTexts` reads every literal handed
  to L10n out of Gooner.Core and the Godot scripts, and the test fails on a text without a
  translation, a translation of a text that is gone, and one that lost a placeholder or a tag.
  What is translated as a whole where it is shown is added from the running code: the Guide block by
  block (`Guide.Translated`, `RichText.Blocks`), every What's New entry, every achievement. So a
  changed text fails here until its translations follow - write them then, in the app's voice, as
  the callouts are written (`du` in German, `tu` in French).
- The climax words over the picture (CUM, RUINED, DENIED) stay English in every language (the
  user's call). The field names in a saved session's problems (`duration_sec`, ...) stay as they
  are written in the file.

## Characters (phase 8, begun)

- **Where models come from**: `res/characters/*.vrm`, and a folder of the user's own picked under
  Settings > Look (`CharacterChoice`; "own/<name>" for one from there). That folder's path is treated
  like a media folder: disclosed and deletable under Privacy & Data, **never logged** - nor is a
  model's name (`Character loaded` names none). **The app's own figure is Zazurona**
  (`res/characters/Zazurona.vrm`, the user's VRoid figure, dressed; `CharacterChoice.Default`): she
  stands there until another is picked, is the one file of `res/characters` in git (the `.gitignore`
  exception) and in the package (`Package.Characters`), and is credited in the notices.
  `ProjectTests` fail if she is not committed or her VRM meta names anyone but `GoonerCock`. Every
  other file there is a local test figure - franchise, explicit, or not ours to pass on - and stays
  out of git and the package. A new own figure needs rights in writing, must be unmistakably adult
  and not explicit, and goes into `Package.Characters` and the notices. The chosen one stands on the
  start screen's pedestal; none, or one that will not load, leaves the logo there.
- **A figure that takes the app down is switched off** (`CharacterChoice.LoadStarting/LoadSettled`,
  `Main.WatchCharacterLoad`): the pick is loaded at every start, so a model that crashes the app
  while it loads, or as it is first drawn, would crash every start after it, with no way into the
  settings. Which one is loading goes into `settings.json` (`Character/loading`) and comes out once it
  has been on screen 3 s, or when the app is closed; a start that still finds it switches that figure
  to none and says so (`CharacterChoice.Abandoned`). Picking it again tries it again.
- **One figure, shared** (`Characters/Figure.cs`): `Figures` loads the chosen model once, and
  whichever `CharacterView` is on screen takes it into its own 3D world - the pedestal between
  sessions, the telepathy frame during one. A view leaving the tree takes the figure out of its
  world first, so a view being freed (the start screen rebuilt for a language) never frees it.
- **The face is driven, not played** (`FaceRig`): godot-vrm makes each expression an animation that
  only writes the shapes it keys, so one played after another left the first one's shapes behind
  (the eyes stayed shut). Every expression is read as blend shapes at their full weight, any number
  are mixed at once - mood, blink, the vowel being said - and every touched shape is written each
  frame; the AnimationPlayer is never played.
- **The telepathy frame** (`Ui/TelepathyFrame`, `shaders/telepathy.gdshader`): her bust at the top
  right from the session's start to its end, a neon oval or an old tube screen (`Look.TelepathyFrame`,
  the user's two picks), off with `Character/in_session`. Each callout draws it closer and brighter;
  she wears `Reactions.For(trigger, tone)` and her mouth follows `Visemes` of the line. **The line
  goes in a speech bubble beside her** (`Ui/SpeechBubble`, the user's pick of three mocks: dark glass
  with the frame's neon rim), typed in on the mouth's own timeline (`Visemes.Shown`), its tail at the
  height of her mouth - found a twentieth of her height below her eyes, since the head bone sits at
  the mouth on one model and at the neck on another. Only while she is in the frame
  (`TelepathyFrame.Present`): without a figure, with the frame switched off or while she still loads,
  the line is the big one over the picture as before. A fake climax
  speaks with `climax_real`'s lines until revealed, so it gets the same face - keep it that way.
- **She stands as a person stands** (`Gooner.Core.Characters.IdleMotion`, applied in `CharacterView`),
  on the pedestal and in the frame alike - the user found her stiff, "nicht wirklich menschlich":
  her weight on one leg and then the other (held 6-14 s, moved over in 1.4 s), hips over it and
  tilted, spine and chest leaning against it, the free knee forward and its heel up while the feet
  stay put (`Figure.Plant`, two-bone IK, `Hold` keeps the foot flat); a breath in quicker than out
  with a rest; a wander on three slow incommensurate waves rather than a sine; arms loose and not
  alike; hands at ease (`Figure.Ease`, curled towards the palm the hand itself reports); eyes that
  rest and dart, now and then glancing aside with the head following slower. Pure sines read as a
  machine - that was the old pose. Judge motion from a video, not stills: Godot's Movie Maker mode
  writes every frame at a fixed rate (`--write-movie out.avi --fixed-fps 30` before the `--`, then
  `--smoke-panel=start --wait=20 --screenshot=...` or a smoke session), ffmpeg from the spikes
  crops it. D.Va's and Shadowheart's exports carry no spring bones, so their hair does not move -
  an export-side job; the VRoid sample's does.
- **Her face is never blank** (`Gooner.Core.Characters.FaceMotion`, laid under the callout's face in
  `CharacterView.Face` - the user found it "tot", "lustlos"): a smile that never quite goes and
  drifts; every 2.5-7 s a micro-expression (a smile breaking through, raised brows, a contented look);
  while she strokes in the frame heavier lids, lips parted and breathing, a wink every 15-40 s; a
  small smile after each line. A cross or sad callout face takes the smile away as it comes, and
  heavy lids plus a smile that narrows the eyes never add up to closed ones. `--face-closeup` puts
  the camera on her face in the frame, for a Movie Maker recording.
- **She moves to the beat** (`CharacterView.Rhythm`, fed the notes either side of the clock and one
  beyond each by `Main`): one stroke per note - the hand down on the note, up near the tip halfway to
  the next (the user's spec: the beat is the stroke's frequency) - leaning and nodding with it; in a
  pause she eases to a stop. **A fast beat gets a shorter stroke, kept at the top**
  (`Gooner.Core.Characters.StrokeMotion`, the user's call): a whole one at five notes a second moved a
  quarter of the toy per frame and read as the hand teleporting. A note's depth is set by the shorter
  of the gaps either side of it, known ahead because the beat is planned, so the hand is already up
  when a fast run begins and never jumps at a note. What still jumps is a long frame - a media change
  that stalls the main thread is a stroke skipped. The arm reaches by two-bone IK, the **forearm takes the roll**
  (a wrist turned alone twists a long glove's cuff into a loop), the hand is turned so the palm faces
  the shaft and the fingers run round its front, and each finger is laid **joint by joint as chords of
  the circle round the shaft through its own knuckle** (`Figure.Reach`, `Orient`, `Wrap`) - aimed, not
  bent by set angles, which is what fits any model's hands. The fingers go round the front from the
  palm, the **thumb the other way round**, behind the shaft, lying on it - as a hand closes on
  anything (the user's correction: a thumb running the fingers' way is not how anyone holds it). The palm's
  side comes from the hand's chirality (little finger to index across the knuckles, wrist to middle
  knuckle along), never from the fingers' bend at rest, which some models have slightly backwards.
  What does not change - where the toy stands (a set gap in front of her belly and clothes over its whole
  height, from what sticks out of it towards her, `Figure.FrontAt`; and far enough out that her
  **wrist** is clear too - the hand closes on the shaft from in front, so the wrist is a hand's length
  behind the knuckles, and on wide figures it sat in her belly; when her arm cannot reach both ends of
  the stroke it is first held up to 6 cm higher, then brought back towards her - **an arm is never
  stretched, the toy goes into her belly instead**, the user's rule; it follows her hips as they sway),
  which way her elbow bends (out to her side and back, unless that takes her arm through her - a wide
  waist did, the user saw it - then the nearest direction, forward, back or raised out to her side,
  where neither part of the arm passes through her rest-pose points at either end of the stroke,
  `Gooner.Core.Characters.ElbowChoice`), how far down her free arms hang (by her side, unless her hips
  are in the way - then up to 25 degrees further out, `ArmHang`; her hair is not counted as her body,
  `Figure.RestPoints`),
  the hand's size, the fingers' wrap - is measured once per figure; per frame only the arm and the
  hand move. Check by eye with `--grip-closeup --grip-down=<0..1>` on a smoke session (the camera on
  the hand, the stroke held; `--grip-turn=90` from her left, `--grip-tilt=40` from above, to see the
  thumb behind the shaft), on several models. A hand that folds its palm in half to close a finger
  is a model mapped wrong, not a grip to tune: its "proximal" finger bones are the ones in the palm,
  starting at the wrist (D.Va's export did that - `Index1` is her metacarpal). **The toy is the first
  `.glb` in `res/props`** (`Characters/Toy.cs`), read once off the main thread under the same lock as a
  character (godot-vrm's extensions are global while one loads) and **measured where it is read**
  (`Gooner.Core.Characters.ToyShape`): length, shaft thickness slice by slice, the middle of the shaft
  at each height - a real one curves, and a straight line through its foot left her fingers standing off
  it - and how far balls and foot stand out behind it, which side goes towards her and sets the gap
  from her belly. Real size, times her size. Her hand waits the moment it takes to read. `res/props` is
  gitignored and never packaged (the user's model, licensed to them, not ours to pass on) - the `.glb`
  is made from it by `GoonerSpikes/blender/scripts/prop_export.py` (decimated, colour baked, 2K
  textures). **Without one she holds the app's own** (`Gooner.Core.Characters.ToyModel`, the user's
  pick of a procedural one over a model file, 2026-10-24): built from code - suction foot, a shaft
  curving up away from its balls with veins on top and a seam below, a head whose ridge falls towards
  the seam, a slit, two balls in one folded sac - so it needs no licence, puts nothing explicit on
  GitHub and is in every package. **Its colour is a setting** (Settings > Look > Toy colour,
  `Look.ToyColor`; `ToyColours`: seven skin tones from ebony to rosy, then ogre, orc, pink, blue,
  purple - the user's list): the mesh carries what part each vertex is (head, vein, sac, near the foot)
  in its vertex colour, and `shaders/toy.gdshader` mixes the picked colours from that exactly as
  `ToyTint.Albedo` does, so a change shows at once and a preview rendered from the core is what the
  engine draws. Its head leans towards the seam by turning about its axis, not by lowering the
  underside - that left a step below the tip. `ToyModelTests` hold it to what `ToyShape` reads off it; judge its
  looks from a render, not the frame (`--built-toy` leaves `res/props` aside for a smoke run). Only
  if building it fails is there the stand-in of primitives in `CharacterView`.
- Loading is godot-vrm's runtime path as phase 0 proved it (GoonerSpikes/RESULTS.md, "(c)
  Nachtrag"): the VRM extensions registered with **first priority**, fresh instances per load,
  `vrm/retarget` off (a patch of ours in `addons/vrm`, named in the notices), import flags 8|16,
  bone poses reset. Do not "simplify" any of it; each one was a crash or a distorted mesh.
- A model loads **off the main thread** (`Figures`, one at a time - godot-vrm's extensions are
  registered globally for the length of a load); the logo holds the pedestal until the figure fades
  in. A large model takes minutes (an 878 MB one, 100 s). Quitting while one loads ends the process
  with `OS.Kill` at the end of `Main.Quit`, after everything of the app's own is written: Godot's
  import running on into the engine going down segfaults (reproduced, exit 139). Panic only
  minimizes, so it is not affected.
- Motion is written against humanoid bone names, in model space (`CharacterView.Turn`), never in a
  bone's local axes - that is what makes one motion fit every model.
- **Cut-out materials get soft edges** (`Figure.SmoothCutouts`): hair cards, lashes and lace come in
  as alpha scissor, all or nothing per pixel, which sorts cleanly but steps their edges; with the
  views' MSAA, alpha to coverage softens them. Sheer stockings are blended in the export itself
  (`GoonerSpikes/blender/scripts/vrm_materials.py`), and hair, tail and breasts swing on VRM spring
  bones the exports add (`vrm_springs.py`) - godot-vrm runs them.

## How the core keeps time

- **No timer fires on its own.** Every timer is a `ClockTimer` on a
  `Timing.ClockScheduler`, and the host calls `Advance()` once a frame: everything that came due
  fires in due order (a tie goes to the timer created first), each at the moment it was due. One
  scheduler is shared by everything on the session clock, so the climax can never be overtaken by
  notes due after it.
- **The rhythm reads an `ISessionClock`** - seconds as a double. In the engine that is the audio
  clock. Not a `TimeProvider` for the rhythm: those count in 100 ns ticks. Wall-clock things
  (ScoreTracker, the recorder, callout teases) keep a `TimeProvider`.
- **Dice come from an `IRandomSource`**, with Python's `random.uniform/randint/choice/choices`
  reproduced on top of it, so a seed replays a whole session (the harness, the tests).
- Note gaps are **whole milliseconds** - exact at 48 kHz, and what a saved session records.
- **In the engine the clock is the audio** (`godot/scripts/Audio/SessionAudio.cs`): the frames
  the mixer has consumed, so an underrun delays the clock instead of breaking the rhythm. The
  stream is filled from its own thread, never from a frame. Each frame the picture clock
  (`Timing.PictureClock`) follows it into a `FrameClock`, the scheduler is advanced, and
  `BeatHandler.UpcomingNotes` - the exact moments, not `UpcomingBeats`' approximation - goes to
  the `Audio.ClickTrack`, which mixes each click on its note's own frame. A note is committed
  once written, so how far ahead the stream is written (80 ms, more if the device runs dry) is
  how late a note can be learned of and still click on time.

## Rules

- **.NET 10, not 8.** .NET 8 leaves support in November 2026; Godot 4.7 exports .NET 10 fine
  (measured). The SDK is pinned in `global.json`.
- **Warnings are errors** (`Directory.Build.props`), analyzers on. That is this side's `ruff`.
- **Logging and privacy**: see Logging below. The crash panel and its log line are cut like a log
  line (`AppLog.Scrub`).
- **The data directory is `%LOCALAPPDATA%\GoonerCock\GoonerApp`**, resolved through
  `Environment.SpecialFolder.LocalApplicationData` - the Python app's folder too. Never Godot's
  `user://`, which is the roaming profile the Python app deliberately moved away from (OneDrive,
  folder redirection).
- **File formats stay readable from what the Python app wrote** - an updating user's history,
  patterns and saved sessions. The saved-session format is a public contract
  (`skills/gooner-session-builder/`).
- **Every object that reads a setting carries its own `Defaults` and its own `SettingsGroup`**
  (`ISettingsOwner`), and no key is claimed by two of them - Reset to defaults looks a fallback up on
  whichever object a field is bound to, and a second copy elsewhere would be a value to hold in step.
- **Settings are `settings.json`, never the registry** - only Windows has one, and a portable app
  has no uninstaller to clean it up. The Python app's key is read once, by the import.

## Build and release

- **The package is a folder, zipped**: `GoonerApp.exe` with the `.pck` embedded, the .NET runtime
  and assemblies in `data_Gooner.Engine_windows_x86_64/`, the FFmpeg libraries, and beside them
  what the engine reads from disk - of `res/` only `callouts/`, `locales/`, `icons/`, the click and
  the app's own figures (named in `Package`, never "all but": the rest is gitignored and private),
  `licenses/` (`engine/licenses`) and `VERSION`. About 200 MB unpacked, 84 MB zipped.
- **`engine/Gooner.Build`** builds and checks it (`Package`, `ExportCheck`, `RunCheck`): every part
  present and byte-identical to the repository's, the release FFmpeg libraries, **no file holding
  the builder's home folder** (a build path carries a name), and a headless smoke start of the
  result that finds `res/` and leaves no Godot folder behind. Do not publish a package it refuses.
  It only ever builds and never talks to a server.
- **Godot writes its shader and pipeline caches, and nothing else**: its file log is off in
  `project.godot`; the caches are on (the user's call, 2026-10-21 - they had been off so that nothing
  of Godot's was left behind). They live in `%APPDATA%\Godot\app_userdata\GoonerApp` (`shader_cache`,
  `vulkan\pipelines.*.cache`; 7 MB after one session) and are shown and deletable under Privacy &
  Data as the graphics cache (`PrivacyData`, given `OS.GetUserDataDir()`). Measured with them: the
  start a second faster, her first appearance in the frame 176 -> 69 ms. Without them every start
  compiled all shaders again and the first session stalled on it. What is left at the start of a
  session (about 120 ms) is first-draw work, not compiling - check with Godot's
  `Performance.Monitor.PipelineCompilations*` monitors and the wall clock, not `delta`. The empty
  folder Godot makes at every start is still removed when nothing is in it
  (`Main.RemoveGodotUserDir`). `Gooner.Build.Tests` holds these settings, the export preset (no
  console wrapper, no debug symbols) and `godot/icon.ico` - a copy of `res/icons/favicon.ico` the
  export embeds as the executable's icon.
- **An exported build has no `res://` on disk**: `GlobalizePath("res://")` is empty there, so
  anything found relative to the Godot project needs a fallback beside the executable (`AppPaths`).
- **The release check** (`.github/workflows/release-check.yml`, on a version tag) installs Godot and
  its export templates pinned by SHA-512, runs the build tool and attaches the zip to a draft
  release. Godot's export templates are 1.2 GB, which is why packages are not built on every PR.
- The first export after a change compiles the C# project and can take minutes; later ones take
  seconds.

## Commands

```
dotnet build engine/GoonerEngine.sln
dotnet test engine/GoonerEngine.sln
dotnet run --project engine/Gooner.Harness -- --seed 42 --climax 150:240   # options: top of Program.cs
dotnet build engine/godot/Gooner.Engine.csproj
"$GODOT" --headless --path engine/godot --import          # once, after a fresh checkout
"$GODOT" --headless --path engine/godot -- --smoke        # exit code 0 = the engine starts
"$GODOT" --path engine/godot -- --smoke-beat [--long]     # 0 = every click on its own frame
"$GODOT" --path engine/godot -- --smoke-session --media=DIR --data-dir=TMP [--climax-at=S] [--stop [--answer=came]] [--device=ws://...] [--panel=NAME] [--say=TRIGGER] [--face-closeup] [--grip-closeup [--grip-turn=DEG] [--grip-tilt=DEG]] [--grip-down=0..1] [--size=WxH] [--screenshot=PNG [--shots=N]]
"$GODOT" --path engine/godot -- --smoke-panel=NAME --data-dir=TMP --screenshot=PNG [--wait=S] [--media=DIR] [--scrub=S] [--size=WxH]
"$GODOT" --path engine/godot -- --data-dir=COPY --legacy-registry='Software\Scratch\GoonerApp' [--legacy-data-dir=ROAMING_COPY] ...
dotnet run --project engine/Gooner.Build -- build --godot "$GODOT" [--zip]   # dist/GoonerApp(-<version>-windows-x64.zip)
dotnet run --project engine/Gooner.Build -- verify [--dir DIR]
```

The registry tests (`WindowsLegacySettingsTests`) write throwaway keys of their own under
`HKCU\Software` and delete them again - never the app's key.

`--smoke-panel` opens one panel by name (the list is `SmokePanels()` in `Main.Panels.cs`:
`settings`, `settings-look`, `settings-device`, `statistics`, `explorer`, `picker`, `privacy`,
`crash`, `menu`, `start` - the start screen alone - ...), waits for it to fade in and saves
the window - how every panel was checked by eye. `--size=1280x720` checks a small laptop screen
(a panel of a set height scrolls its body rather than run off the window); `--panel=NAME` on a
smoke session opens the panel over a running session, to see the glass over a picture.

The smoke runs never touch a real data folder when given `--data-dir`; give them one. A look to
check by eye goes into that folder's `settings.json` first (`{"Look/note_track":"ring",
"Look/censor":"blur"}`). HDR 2D stays off - the effect layer glows with sprites - because with it on
a screenshot of the viewport comes out in linear colour and every check of the palette reads too
dark.

`$GODOT` is the Godot 4.7.2 .NET *console* binary (`Godot_v4.7.2-stable_mono_win64_console.exe`);
the plain one detaches from the terminal and its exit code is lost. Godot's own packages restore
from the NuGet cache the editor fills, so there is deliberately no `nuget.config` here.

Both test projects run their tests in sequence (`AssemblyInfo.cs`): the log is one sink for the
whole process, and a test with file logging on would otherwise catch other tests' warnings.

## Logging

**Never `Console.WriteLine` in `Gooner.Core`.** The shipped app has no console, so it would go
nowhere and a user's bug report would have nothing to show. Use `AppLog` (`Gooner.Core.Logging`):

- **Values go in as arguments** (`AppLog.Warning(Log, "Could not load the callout file {0}: {1}", name,
  AppLog.Describe(e))`), never an interpolated string - formatting is skipped when the line is
  filtered out, and the call site stays greppable.
- **Levels: `INFO` is the floor.** There is deliberately no debug/trace level - this log exists to
  explain a problem to a human, not to trace execution, and per-beat chatter would bury the one
  line that matters. `INFO` for lifecycle events worth seeing in order (session start/end, settings
  saved, update check, data migration, a destructive action the user asked for), `WARNING` for
  something the app recovered from, `ERROR` for something the user will notice being broken.
- **Privacy rules - not negotiable in this app.** Never log a media *folder* path: that reveals
  where the collection lives, which is exactly what Privacy & Data exists to keep under the user's
  control. Nor the folder of the user's own characters, nor a model's name. Prefer counts
  (`"Playlist loaded: {0} files"`). A bare *file name* is allowed in a warning or error about that
  one file. Paths the app itself owns (the data directory, `res/`) are fine. **An exception goes in
  as `AppLog.Describe(e)`, never `e.Message`**: .NET writes the whole path into it, and `Describe`
  cuts every path to its file name (`LogPrivacyTests` fails on a `.Message` in a log call).
- The log is **opt-in** (off by default) and the user picks its threshold; both controls, plus
  deletion, live in **Privacy & Data**, not in Settings - it writes extra data about the user, so it
  belongs where that data is disclosed and deleted. A development run's console shows everything
  regardless.
- Log lines are never translated.

## Testing

`dotnet test engine/GoonerEngine.sln` (set `DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1`). The
logic is tested without an engine; what only Godot can show is checked by smoke runs and by eye
(see Commands).

- **Work test-driven**: for new behaviour or a bug fix, write the failing test first and see it
  fail, then implement. When the expected behaviour is unclear (edge cases, ambiguous UX, "what
  should happen when..."), ask the user before writing the test rather than encoding a guess.
- Tests never touch real data: every store takes its base directory as a parameter, the registry
  import takes an `ILegacySettings` (`FakeLegacySettings`), and the reader is tested against a
  throwaway key of its own. Never the real data folder, never the app's registry key.
- **What guards what:** `CalloutLanguageFilesTests` every phrase file in `res/callouts` (schema,
  keys, tones - run it first when adding a language or tone, see
  `.claude/skills/add-callout-language/SKILL.md`); `LocaleFilesTests` every translation;
  `SessionFormatDocTests` the session format document in `skills/`; `LicenseShelfTests` and
  `Gooner.Build.Tests` the notices, the FFmpeg sources and what the package carries;
  `GuideTests` the in-app Guide.
- **No tombstone tests**: never assert that removed code is gone - such a test can never fail
  again.

## Git workflow

Feature branches and PRs into `main`, never direct commits to `main`. **Never push without the
user's approval for that push, and never merge a PR** - the merge is the user's review gate. No
Claude/AI attribution in commit messages. Commit each meaningful change as it lands rather than
batching; squashing can happen before the PR if wanted.

## Versioning & releases

The version lives in `VERSION` at the repo root (plain semver), nowhere else. The packaged app
reads it, and the What's New panel shows the changelog entries newer than the last version the user
saw.

- **Before opening a PR, ask the user whether it bumps the version, and to what** - don't apply the
  rules below mechanically; a small feature may be folded into a nearby patch.
- `feat:` bumps MINOR, a critical `fix:` (crash, data loss, a core feature broken) bumps PATCH,
  as the last commit on the branch: `chore: bump version to X.Y.Z`. **The same commit adds the
  `Changelog.Entries` entry** (`engine/Gooner.Core/App/Changelog.cs`) in the app's own flirty,
  teasing voice - a suggestive lead, a short list of what is new, a closing hook; not a commit log -
  **and its German and French translations** (`res/locales/*/changelog.json`; `ChangelogTests` fails
  without the entry, `LocaleFilesTests` without the translations).
- Routine `fix:`/`test:`/`content:`/`docs:`/`chore:` PRs bump nothing.
- **After a version-bumping PR is merged**:
  1. Build and check: `dotnet run --project engine/Gooner.Build -- build --godot "$GODOT" --zip`.
     **Do not publish a package it refuses.**
  2. Tag and push the tag on its own: `git tag v<VERSION> && git push origin v<VERSION>`.
  3. The **Release check** workflow (`.github/workflows/release-check.yml`) refuses a tag that
     disagrees with `VERSION`, builds the package on a clean runner from what is *committed*, runs
     the same checks, and - only if they pass - attaches the zip and the FFmpeg source archives to a
     **draft** release for the tag.
  4. The user writes the notes in the app's own voice and publishes the draft.
- **The published package is the one CI built**, not the one on a disk: a local build can only
  check the working tree, so a file that exists locally but was never committed passes there and
  fails in CI. **The tag goes up before the release** on purpose - `gh release create` would make
  the tag itself and fire the workflow after the asset was public. **Nothing reaches users without
  a person pressing publish.**
- `Gooner.Build` only builds and checks - it never calls `gh` or touches GitHub. **Keep it that
  way**: anyone who clones the repo runs it on their own machine. The release workflow is the one
  exception, scoped to `contents: write`, a `v*` tag, a passed check, and a draft.
- New or changed shortcuts go into the Guide's shortcut list (`Guide.cs`, translated) and the
  README by hand; nothing generates them.

## Session files people write by hand

`skills/gooner-session-builder/` is a self-contained folder someone drops into their own AI
assistant: `SKILL.md` (how to shape a session worth playing) plus `SESSION_FORMAT.md` (the field
reference). It lives at the repo root rather than in `.claude/skills/` on purpose - that directory
only reaches Claude Code working *in this repo*, which is the wrong audience entirely.

`SessionFiles.ValidateSession` is what makes that safe to offer: a hand-written file gets every
detail wrong that a serialiser gets right for free. It collects every problem rather than raising on
the first, because somebody iterating on a generated file wants the whole list. Its messages are a
public contract, word for word. `SessionFormatDocTests` keeps the document honest - it fails if it
names a rhythm the app cannot play, omits one it can, or carries an example the validator rejects.

## Content note

`res/callouts/*/*.json` contain sexually explicit text (this is an adult-themed app by design) -
expected content when editing those files. The `degrading` and `hard_degrading` tones are
humiliation play by design and are deliberately harsher than the rest (`hard_degrading` is
small-penis humiliation throughout); both stay opt-in and are never part of a
default or fallback selection. Callouts in German and French are written natively, never
translated line for line.

**Nothing explicit goes on GitHub beyond those texts**: no explicit character models, no explicit
screenshots (GitHub's Acceptable Use Policy covers release assets too). Bundled characters need
rights in writing, must be unmistakably adult and are dressed; explicit variants stay local. No
public screenshot shows a figure holding the toy.

## Naming

The local folder is `Gooner`, the GitHub repo is `AutoShowStroker`, and the app itself is
`GoonerApp`. All three refer to the same project.
