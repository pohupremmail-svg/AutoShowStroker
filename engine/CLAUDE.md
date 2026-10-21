# engine/ — GoonerApp on Godot 4.7 + C# (.NET 10)

The rebuild of GoonerApp on a real engine. It lives next to the Python app in the same tree for
one reason: **both implementations sit in the same commit**, so the Python app can serve as the
oracle the C# port is compared against (see `parity/` at the repo root). Nothing in here ships
until the rebuild is complete; until then the Python app in `src/` is the product and keeps being
released as before.

**It stays local until it is finished.** The rebuild is meant as a surprise for the users, so the
branch it lives on (`engine-rebuild`) is never pushed, never turned into a PR, and never mentioned
in anything public - not in release notes, changelog entries, or the commits and PRs of ordinary
Python fixes that do go out. `main` is merged *into* this branch to pick up those fixes; the
other direction is the user's call, as every merge is.

Everything else in the root `CLAUDE.md` about privacy, testing and the user's review gate applies
here unchanged. This file only adds what is specific to this side.

## Layout

- `Gooner.Core/` — the app's logic, data and file formats. **No reference to Godot**, so it is
  tested with plain `dotnet test` in seconds and never needs an engine to run. Ports of the
  Python modules land here first.
- `Gooner.Core.Tests/` — xUnit. Test names mirror the Python test they port (`snake_case`, the
  `test_` prefix dropped), so a behaviour can be traced across both suites by name.
- `Gooner.Parity.Tests/` — compares the port against golden files the Python implementation
  writes (`python -m parity.generate`). Not part of the solution's default test run, because it
  needs the Python side; CI runs it in its own job.
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
  as QSettings does, and the parity run holds it against bytes a real PyQt6 QSettings wrote.
  The import runs before there is a log, so `GoonerApp` logs its report.
- **Until the parity release the engine does not take the profile over**:
  `AppPaths.TakesOverThePythonProfile` is false, so a run without `--data-dir` keeps its own folder,
  `%LOCALAPPDATA%\GoonerCock\GoonerApp.EnginePreview`, and imports nothing - the import deletes
  what it carries over, and the installed Python app is still in use. A test against a copy of a
  real profile names its sources: `--legacy-registry=<key under HKCU>` and
  `--legacy-data-dir=<folder>`. Never point `--legacy-registry` at the app's own key.
- **Video is the FFmpeg extension EIRTeam.FFmpeg**, in `godot/addons/ffmpeg/`, committed as the
  autobuild pinned in phase 0 left it (commit `84ea333`; release 1.1.4 never converts YUV on Godot
  4.7) - plain git, not LFS (the user's call, 2026-10-17: 14 MB that never change). The stage
  reaches the extension by class name, so a build without it still runs: every clip reports
  unplayable and the player moves on. Its FFmpeg is an **LGPL v3** build (`--enable-version3`, no
  GPL, no nonfree; H.264, MPEG-4, VP8/9, AAC, MP3, Opus, Vorbis - no HEVC); the wrapper is MIT.
  The package carries the LGPLv3/GPLv3 texts and the wrapper's MIT text (`engine/licenses`), and
  every release has the FFmpeg source of exactly that revision and the extension's source attached
  (`engine/third_party/ffmpeg`, see its README). A new build of the extension replaces all of it
  together; `Gooner.Build.Tests` and `LicenseShelfTests` fail until the archives and notices follow.
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
- **Every dialog of the Python app is a panel** (`godot/scripts/Panels/`), shown by the `PanelHost`
  over the whole window - start screen included, as a modal dialog blocked everything behind it -
  while the session plays on behind it. Panels are frosted glass (`Ui/Glass.cs`,
  `shaders/glass.gdshader`) and can be dragged by their title and thrown. Panels stack; Esc and the
  corner cross run the panel's own answer to being dismissed (`OnReject`; a question's escape
  button). Nothing waits on a panel the way `QDialog.exec()` did: a question calls back.
  `Confirm.Ask`/`Confirm.Tell` are `QMessageBox`.
  The pattern for each dialog: its logic is a model in `Gooner.Core.Panels` (`SettingsForm`,
  `StatisticsReport`, `PrivacyData`, `SavedSessionsShelf`, `SessionExplorer`, `MediaFolderPicker`,
  ...) carrying the ported tests of the dialog's Python test file; the panel only draws it and
  writes into it. Python tests about Qt itself (fixed dialog sizes, nested event loops, widget
  focus) are not ported.
- **Panic is read in `_Input`, before any control**, so a focused button in a panel cannot take
  Space for itself; only a text field keeps its spaces.
- **There is no menu bar and no button row** (phase 7). Between sessions the **start screen**
  (`Ui/StartScreen.cs`) has big cards for what one comes to do and every other menu entry along its
  foot; during a session a **glass dock** (`Ui/Dock.cs`) floats over the picture - draggable,
  throwable, fading while the pointer rests - with the session's buttons and a menu panel holding
  the rest. The old accelerators stay as keys (Ctrl+S settings, F1 guide, Ctrl+Q quit, Ctrl+O
  folder). Texts copied from the Python side that name a menu get a `Guide.EngineEdits` edit;
  `GuideTests` fails while any still sends the user to Help, Sessions or Statistics.
- **Texts copied from the Python side stay word for word.** `Changelog.Entries` and
  `Guide.PythonTopics` are generated from the Python source and compared by the parity run - an
  entry added on `main` and merged in fails there until it is copied over (and `LocaleFilesTests`
  until it is translated, see "The app's own language"). Where the engine works
  differently, the Guide gets an edit on top (`Guide.EngineEdits`, naming the exact sentence it
  replaces), never a change inside the copy.
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
- **The engine's own licenses** are in `engine/licenses/` (its notices, GPLv3/LGPLv3, the .NET
  runtime's), beside the executable once exported; Godot's are read from the engine itself.
  `LicenseShelfTests` keep the notices in step with the Godot SDK, the .NET version and the
  FFmpeg DLLs, as `test_third_party_licenses.py` does on the Python side.
- `res/` (callouts, click, icons) is read **from disk**, never through Godot's `res://`:
  `Gooner.Core.Data.ResourceRoot` finds the repo's own copy from a dev run and the one beside the
  executable once exported. The files never go through Godot's importer, so the engine reads
  exactly what the Python app reads.

## The look (phase 7)

- **The look is settings** (`Gooner.Core.App.Look`, group `Look`): the note track (`highway`,
  `ring`, `stream`, `tunnel`), the colour scheme, the effect strength, whether the Hypno Ring
  wanders and what it does to the picture around it (`censor`: off, blur, mosaic, dark). A stored
  choice this build does not know reads as the default. The Settings panel shows them on a Look tab.
- **Colour schemes are `Gooner.Core.App.Palette.Schemes`** - Neon (src/theme.py's palette, unchanged)
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
  screen comes up - shown whether callouts are on or not. `res/` is shared, so the Python side
  lists `main_menu` among its keys too, to keep the files valid there; it never says it.
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
  what a catalogue lacks stays English. So the English app is exactly the code, and the parity run,
  which runs in English, never sees any of this.
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
  text copied from the Python side that changes when `main` is merged in fails here until its
  translations follow - write them then, in the app's voice, as the callouts are written.
- The climax words over the picture (CUM, RUINED, DENIED) stay English in every language (the
  user's call). The field names in a saved session's problems (`duration_sec`, ...) stay as they
  are written in the file.

## Characters (phase 8, begun)

- **Where models come from**: `res/characters/*.vrm`, and a folder of the user's own picked under
  Settings > Look (`CharacterChoice`; "own/<name>" for one from there). That folder's path is treated
  like a media folder: disclosed and deletable under Privacy & Data, **never logged** - nor is a
  model's name (`Character loaded` names none). The bundled ones are **gitignored** until there are
  ones the app may pass on (rights in writing, nothing explicit on GitHub, unmistakably adult), and
  the package leaves every `.vrm` out (`Package.BesideExecutable`). The chosen one stands on the
  start screen's pedestal; none, or one that will not load, leaves the logo there.
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
  What does not change - where the toy stands (clear of the clothes at her belly, `Figure.FrontAt`),
  the hand's size, the fingers' wrap - is measured once per figure; per frame only the arm and the
  hand move. Check by eye with `--grip-closeup --grip-down=<0..1>` on a smoke session (the camera on
  the hand, the stroke held; `--grip-turn=90` from her left, `--grip-tilt=40` from above, to see the
  thumb behind the shaft), on several models. A hand that folds its palm in half to close a finger
  is a model mapped wrong, not a grip to tune: its "proximal" finger bones are the ones in the palm,
  starting at the wrist (D.Va's export did that - `Index1` is her metacarpal). **The toy is a stand-in** built from
  primitives (`CharacterView.PlaceToy`) until the user brings a proper asset - which needs its licence
  checked like a character, and stays off GitHub if it is explicit.
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

## How the core keeps time

- **No timer fires on its own.** Every QTimer of the Python side is a `ClockTimer` on a
  `Timing.ClockScheduler`, and the host calls `Advance()` once a frame: everything that came due
  fires in due order (a tie goes to the timer created first), each at the moment it was due. One
  scheduler is shared by everything on the session clock, so the climax can never be overtaken by
  notes due after it.
- **The rhythm reads an `ISessionClock`** - seconds as a double. In the engine that is the audio
  clock. Not a `TimeProvider` for the rhythm: those count in 100 ns ticks. Wall-clock things
  (ScoreTracker, the recorder, callout teases) keep a `TimeProvider`.
- **Dice come from an `IRandomSource`**, with Python's `random.uniform/randint/choice/choices`
  reproduced on top of it, so the parity harness can replay the exact draws the Python side made.
- Note gaps stay the **whole milliseconds** Python schedules - exact at 48 kHz, and what keeps the
  two implementations comparable note for note.
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
- **Test-driven, same as the Python side**: when porting, the ported test comes first and must
  fail before the code exists. A behaviour the Python suite does not pin down is a question for
  the user, not a guess.
- **A port is a port.** Where the Python code has a quirk, the C# version either reproduces it or
  deviates *on purpose* - and a deliberate deviation is written down in the parity harness's
  allowlist with the reason, so it can never be mistaken for drift.
- **No `Console.WriteLine` in `Gooner.Core`.** Logging goes through the same rules as
  `src/applog.py`: opt-in, `INFO` as the floor, values as arguments, and **never a media folder
  path** - a bare file name only in a warning about that one file.
- **The data directory is `%LOCALAPPDATA%\GoonerCock\GoonerApp`**, the folder the Python app
  already uses, resolved through `Environment.SpecialFolder.LocalApplicationData`. Never Godot's
  `user://`, which is the roaming profile `src/user_data.py` deliberately moved away from.
- **File formats stay compatible with the Python app**, semantically: what one writes the other
  reads. The saved-session format is a public contract (`skills/gooner-session-builder/`), so its
  validator messages are compared word for word.
- Tests never touch the real data directory: every store takes its base directory as a
  parameter, exactly like `UserDataStore(base_dir=...)` on the Python side. Nor the real registry
  key: the import takes an `ILegacySettings` (`FakeLegacySettings` in tests), and the reader is
  tested against a throwaway key.
- **Settings are `settings.json`, never the registry** - only Windows has one, and a portable app
  has no uninstaller to clean it up. The Python app's key is read once, by the import.

## Build and release

- **The package is a folder, zipped**: `GoonerApp.exe` with the `.pck` embedded, the .NET runtime
  and assemblies in `data_Gooner.Engine_windows_x86_64/`, the FFmpeg libraries, and beside them
  what the engine reads from disk - `res/` (without `res/licenses`, which is the Python app's),
  `licenses/` (`engine/licenses`) and `VERSION`. About 200 MB unpacked, 84 MB zipped.
- **`engine/Gooner.Build`** builds and checks it (`Package`, `ExportCheck`, `RunCheck`): every part
  present and byte-identical to the repository's, the release FFmpeg libraries, **no file holding
  the builder's home folder** (a build path carries a name), and a headless smoke start of the
  result that finds `res/` and leaves no Godot folder behind. Do not publish a package it refuses.
  Like `scripts/build.py`, it only ever builds and never talks to a server.
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

## Release day: the switch

The engine replaces the Python app in one release, planned for Christmas together with the new
look (phase 7). Until that day the Python app on `main` is the product. On the day, in this order:

1. **Open items closed**: the FFmpeg licence finding (TODO.md - its own build, or every statically
   linked library's notice), and the phase 7 effect layer.
2. **Remove the Python app** in one commit: `src/`, `tests/`, `parity/`, `main.py`, `scripts/`
   (`build.py`, `verify_frozen.py`), `requirements*.txt`, `ruff.toml`, `pytest.ini`, the Python jobs
   in `ci.yml`, `engine.yml`'s parity job and `audit.yml`'s pip-audit. What only the parity run
   guarded is already guarded here (Changelog and Guide copies become the source; the session
   format by `SessionFormatDocTests`; callout files by `CalloutLanguageFilesTests`).
3. **Rewrite the docs for the engine**: the root `CLAUDE.md` (merge this file into it),
   `CONTRIBUTING.md` (callout files are checked with `dotnet test`, not pytest),
   `.claude/skills/add-callout-language`.
4. **Take the profile over**: `AppPaths.TakesOverThePythonProfile` to true - the real data folder,
   the registry import, the roaming move. Not before: every start without `--data-dir` would empty
   the registry key of a Python app still in use.
5. **Version and changelog**: the user picks the number (above the last Python release, or the
   installed apps' update check never announces it); the entry goes into `Changelog.Entries` in
   the app's own voice, and it is what What's New shows every updating user.
6. **PR into `main`**, which the user merges; then the tag, the release check, the draft. The notes
   say: unpack the folder, settings and history come along on their own, HEVC videos are skipped
   (unless the own FFmpeg build is in), and the old `.exe` can be deleted.

## Commands

```
dotnet build engine/GoonerEngine.sln
dotnet test engine/GoonerEngine.sln
.venv/Scripts/python.exe -m parity.generate      # from the repo root
dotnet test engine/Gooner.Parity.Tests
dotnet run --project engine/Gooner.Harness -- --seed 42 --climax 150:240   # options: top of Program.cs
dotnet build engine/godot/Gooner.Engine.csproj
"$GODOT" --headless --path engine/godot --import          # once, after a fresh checkout
"$GODOT" --headless --path engine/godot -- --smoke        # exit code 0 = the engine starts
"$GODOT" --path engine/godot -- --smoke-beat [--long]     # 0 = every click on its own frame
"$GODOT" --path engine/godot -- --smoke-session --media=DIR --data-dir=TMP [--climax-at=S] [--stop [--answer=came]] [--device=ws://...] [--panel=NAME] [--say=TRIGGER] [--grip-closeup [--grip-turn=DEG] [--grip-tilt=DEG]] [--grip-down=0..1] [--size=WxH] [--screenshot=PNG [--shots=N]]
"$GODOT" --path engine/godot -- --smoke-panel=NAME --data-dir=TMP --screenshot=PNG [--wait=S] [--media=DIR] [--scrub=S] [--size=WxH]
"$GODOT" --path engine/godot -- --data-dir=COPY --legacy-registry='Software\Scratch\GoonerApp' [--legacy-data-dir=ROAMING_COPY] ...
dotnet run --project engine/Gooner.Build -- build --godot "$GODOT" [--zip]   # dist/GoonerApp(-<version>-windows-x64.zip)
dotnet run --project engine/Gooner.Build -- verify [--dir DIR]
```

The registry tests (`WindowsLegacySettingsTests`, `RegistryParity`, the generator's `registry`
module) write throwaway keys of their own under `HKCU\Software` and delete them again - never the
app's key.

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
