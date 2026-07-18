#!/usr/bin/env python
"""Build GoonerApp.exe and publish it as a GitHub Release for the version in VERSION.

Usage:
    .venv\\Scripts\\python.exe scripts/release.py [--notes "..."] [--build-only]
"""
import argparse
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def read_version() -> str:
    return (ROOT / "VERSION").read_text(encoding="utf-8").strip()


def find_pyinstaller() -> str:
    candidate = Path(sys.executable).with_name("pyinstaller.exe")
    return str(candidate) if candidate.exists() else "pyinstaller"


def find_gh() -> str:
    for candidate in ("gh", r"C:\Program Files\GitHub CLI\gh.exe"):
        if shutil.which(candidate) or Path(candidate).exists():
            return candidate
    raise SystemExit("gh CLI not found on PATH or at the default Windows install location.")


def build_exe() -> Path:
    subprocess.run(
        [
            find_pyinstaller(), "--noconfirm", "--onefile", "--windowed",
            "--add-data", "res;res", "--name", "GoonerApp", "main.py",
        ],
        cwd=ROOT, check=True,
    )
    exe_path = ROOT / "dist" / "GoonerApp.exe"
    if not exe_path.exists():
        raise SystemExit(f"Build finished but {exe_path} was not found.")
    return exe_path


def create_release(version: str, exe_path: Path, notes: str) -> None:
    tag = f"v{version}"
    subprocess.run(
        [find_gh(), "release", "create", tag, str(exe_path), "--title", tag, "--notes", notes],
        cwd=ROOT, check=True,
    )


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--notes", default="", help="Release notes (defaults to a generic message)")
    parser.add_argument("--build-only", action="store_true", help="Build the .exe but skip publishing")
    args = parser.parse_args()

    version = read_version()
    print(f"Building GoonerApp v{version}...")
    exe_path = build_exe()
    print(f"Built {exe_path}")

    if args.build_only:
        return

    create_release(version, exe_path, args.notes or f"GoonerApp v{version}")
    print(f"Published release v{version}")


if __name__ == "__main__":
    main()
