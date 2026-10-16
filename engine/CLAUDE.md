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
- The Godot project comes later (phase 2); it references `Gooner.Core` and adds only picture,
  sound, input and effects.

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
  parameter, exactly like `UserDataStore(base_dir=...)` on the Python side.

## Commands

```
dotnet build engine/GoonerEngine.sln
dotnet test engine/GoonerEngine.sln
.venv/Scripts/python.exe -m parity.generate      # from the repo root
dotnet test engine/Gooner.Parity.Tests
dotnet run --project engine/Gooner.Harness -- --seed 42 --climax 150:240   # options: top of Program.cs
```

Both test projects run their tests in sequence (`AssemblyInfo.cs`): the log is one sink for the
whole process, and a test with file logging on would otherwise catch other tests' warnings.
