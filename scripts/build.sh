#!/usr/bin/env bash
# Build NovaBar with the .NET Framework csc.exe that ships with Windows (C# 5 only!).
# Usage: scripts/build.sh [OUTNAME] [extra source globs...]
#   no globs  -> builds everything under src/
#   globs     -> builds src/Core + src/Bar + src/App + the given globs (lets agents build their own group in isolation)
# Output: /mnt/c/Users/alhaz/NovaBar/build/<OUTNAME>/NovaBar.exe
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
NAME="${1:-main}"; shift || true
DEST="/mnt/c/Users/alhaz/NovaBar/build/$NAME"
mkdir -p "$DEST/src"
rsync -a --delete "$ROOT/src/" "$DEST/src/"
rsync -a "$ROOT/data/" "$DEST/data/" 2>/dev/null || true
cd "$DEST"; rm -f build.log
if [ $# -eq 0 ]; then
  find src -name '*.cs' > files.rsp.tmp
else
  { find src/Core src/Bar src/App -name '*.cs' 2>/dev/null; for g in "$@"; do (cd "$DEST" && ls -1 $g 2>/dev/null || true) ; done; } | sort -u > files.rsp.tmp
fi
sed 's#/#\\#g; s#^#"#; s#$#"#' files.rsp.tmp > files.rsp; rm files.rsp.tmp
FW='C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
WINMD='C:\Windows\System32\WinMetadata'
RES=""
[ -f data/reference-widgets.json ] && RES='/resource:data\reference-widgets.json,NovaBar.reference-widgets.json'
# service logos for the Accounts page: data/logos/<account id>.png -> NovaBar.logo.<id>.png
if [ -d data/logos ]; then for f in data/logos/*.png; do b=$(basename "$f"); RES="$RES /resource:data\\logos\\$b,NovaBar.logo.$b"; done; fi
MANI=""
[ -f src/App/app.manifest ] && MANI='/win32manifest:src\App\app.manifest'
ICON=""
[ -f src/App/app.ico ] && ICON='/win32icon:src\App\app.ico'
# (media agent) + System.Runtime.InteropServices.WindowsRuntime facade: lets csc bind WinRT events / EventRegistrationToken from the System32 winmds
/mnt/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe /nologo /noconfig /target:winexe /optimize+ /platform:anycpu /utf8output /warn:0 \
  /out:NovaBar.exe $MANI $ICON $RES \
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Security.dll /r:System.Management.dll /r:Microsoft.CSharp.dll /r:System.Xml.dll /r:System.Xml.Linq.dll \
  "/r:$FW\\System.Runtime.WindowsRuntime.dll" "/r:C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Runtime\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.dll" "/r:$FW\\System.Threading.Tasks.dll" \
  "/r:C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Runtime.InteropServices.WindowsRuntime\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.InteropServices.WindowsRuntime.dll" \
  "/r:$WINMD\\Windows.Foundation.winmd" "/r:$WINMD\\Windows.Media.winmd" "/r:$WINMD\\Windows.Devices.winmd" "/r:$WINMD\\Windows.UI.winmd" "/r:$WINMD\\Windows.Storage.winmd" "/r:$WINMD\\Windows.System.winmd" "/r:$WINMD\\Windows.ApplicationModel.winmd" "/r:$WINMD\\Windows.Networking.winmd" "/r:$WINMD\\Windows.Data.winmd" \
  @files.rsp 2>&1 | tr -d '\r' | tee build.log | sed "s#$(echo 'C:\\Users\\alhaz\\NovaBar\\build\\' | sed 's#\\#\\\\#g')$NAME\\\\##" | grep -v '^$' || true
grep -q "error CS" build.log 2>/dev/null && { echo "BUILD FAILED"; exit 1; }; [ -f NovaBar.exe ] && [ NovaBar.exe -nt files.rsp ] && echo "BUILD OK -> $DEST/NovaBar.exe" || { echo "BUILD FAILED"; exit 1; }
