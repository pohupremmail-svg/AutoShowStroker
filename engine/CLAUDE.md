# engine/ — GoonerApp on Godot 4.7 + C# (.NET 10)

The rebuild of GoonerApp on a real engine. It lives next to the Python app on `main`, not on a
branch of its own, for one reason: **both implementations sit in the same commit**, so the Python
app can serve as the oracle the C# port is compared against (see `parity/` at the repo root).
Nothing in here ships until the parity release; until then the Python app in `src/` is the
product and keeps being released as before.

Everything in the root `CLAUDE.md` about git, privacy, versioning and the user's review gate
applies here unchanged. This file only adds what is specific to this side.

## Layout

- `Gooner.Core/` — the app's logic, data and file formats. **No reference to Godot**, so it is
  tested with plain `dotnet test` in seconds and never needs an engine to run. Ports of the
  Python modules land here first.
- `Gooner.Core.Tests/` — xUnit. Test names mirror the Python test they port (`snake_case`, the
  `test_` prefix dropped), so a behaviour can be traced across both suites by name.
- `Gooner.Parity.Tests/` — compares the port against golden files the Python implementation
  writes (`python -m parity.generate`). Not part of the solution's default test run, because it
  needs the Python side; CI runs it in its own job.
- The Godot project comes later (phase 2); it references `Gooner.Core` and adds only picture,
  sound, input and effects.

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
```
