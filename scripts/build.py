#!/usr/bin/env python
"""Build GoonerApp.exe locally via PyInstaller.

Anyone with a checkout of this repo can run this to produce their own
dist/GoonerApp.exe - it only builds, it never touches GitHub or pushes anything.

Usage:
    .venv\\Scripts\\python.exe scripts/build.py
"""
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def find_pyinstaller() -> str:
    candidate = Path(sys.executable).with_name("pyinstaller.exe")
    return str(candidate) if candidate.exists() else "pyinstaller"


def build_exe() -> Path:
    subprocess.run(
        [
            find_pyinstaller(), "--noconfirm", "--onefile", "--windowed",
            "--add-data", "res;res", "--add-data", "VERSION;.",
            "--icon", "res/icons/favicon.ico", "--name", "GoonerApp", "main.py",
        ],
        cwd=ROOT, check=True,
    )
    exe_path = ROOT / "dist" / "GoonerApp.exe"
    if not exe_path.exists():
        raise SystemExit(f"Build finished but {exe_path} was not found.")
    return exe_path


def main() -> None:
    exe_path = build_exe()
    print(f"Built {exe_path}")


if __name__ == "__main__":
    main()
