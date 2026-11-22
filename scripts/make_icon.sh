#!/usr/bin/env bash
# Regenerates src/App/app.ico via Windows PowerShell + System.Drawing.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TMP=/mnt/c/Users/alhaz/NovaBar/tmp-icon; mkdir -p "$TMP"
cp "$ROOT/scripts/make_icon.ps1" "$TMP/"
(cd "$TMP" && powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'C:\Users\alhaz\NovaBar\tmp-icon\make_icon.ps1' -out 'C:\Users\alhaz\NovaBar\tmp-icon\app.ico')
cp "$TMP/app.ico" "$ROOT/src/App/app.ico"; rm -rf "$TMP"; echo "-> src/App/app.ico"
