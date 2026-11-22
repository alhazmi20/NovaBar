#!/usr/bin/env bash
# Replace the running NovaBar (dist) with build/main and restart it. Works even if a Settings window holds the old exe.
set -u
cd /mnt/c/Users/alhaz/NovaBar
timeout 30 ./dist/NovaBar/NovaBar.exe --exit >/dev/null 2>&1
for i in $(seq 1 15); do
  n=$(powershell.exe -NoProfile -Command "(Get-CimInstance Win32_Process -Filter \"Name='NovaBar.exe'\" | ? { \$_.CommandLine -notlike '*--settings*' -and \$_.CommandLine -notlike '*--render*' } | Measure).Count" | tr -d '\r')
  [ "$n" = "0" ] && break; sleep 1
done
cd dist/NovaBar
for f in NovaBar.old*.exe; do [ -e "$f" ] && rm -f "$f" 2>/dev/null; done
mv NovaBar.exe "NovaBar.old$(date +%s).exe" 2>/dev/null
cp ../../build/live/NovaBar.exe NovaBar.exe
powershell.exe -NoProfile -Command "Start-Process 'C:\Users\alhaz\NovaBar\dist\NovaBar\NovaBar.exe'"
echo "deployed $(date +%T)"
