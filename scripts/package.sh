#!/usr/bin/env bash
# Builds the full app ("main") and assembles a distributable folder:
#   /mnt/c/Users/alhaz/NovaBar/dist/NovaBar/{NovaBar.exe, README.txt}
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
"$ROOT/scripts/build.sh" main
DIST=/mnt/c/Users/alhaz/NovaBar/dist/NovaBar
mkdir -p "$DIST"
cp /mnt/c/Users/alhaz/NovaBar/build/main/NovaBar.exe "$DIST/NovaBar.exe"
cat > "$DIST/README.txt" <<'TXT'
NovaBar — a lightweight taskbar replacement / widget bar for Windows 11
=======================================================================

Requirements: Windows 10/11 with .NET Framework 4.8 (built in). No installer, no dependencies.

Start:      double-click NovaBar.exe  (runs in the background; one instance per user)
Settings:   right-click the bar -> Settings   (or run: NovaBar.exe --settings)
Exit:       right-click the bar -> Exit NovaBar   (or run: NovaBar.exe --exit)

By default NovaBar replaces the Windows taskbar: the taskbar is hidden while NovaBar runs and
is restored automatically when NovaBar exits. If it ever stays hidden (e.g. NovaBar was killed
from Task Manager), run:   NovaBar.exe --restore-taskbar   — or simply start NovaBar again and exit it.

Bar tips:   drag a widget to reorder it, drag an empty spot of the bar to the top/bottom of the
            screen to move the bar, right-click for theme / position / style / size.
Start with Windows: Settings -> General -> "Launch at startup".

Your settings live in %APPDATA%\NovaBar (config.json, widget state, cache). API keys you enter
for account-based widgets are encrypted with Windows DPAPI (only your Windows user can read them).
Uninstall: exit NovaBar, delete this folder and %APPDATA%\NovaBar.
TXT
echo "PACKAGED -> $DIST"
ls -la "$DIST"
