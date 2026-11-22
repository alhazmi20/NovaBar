# CI build (GitHub Actions, windows runner). Mirrors scripts/build.sh using the .NET Framework csc.exe (C# 5).
# Usage: pwsh scripts/build-ci.ps1 [-Out out]
param([string]$Out = "out")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$fw    = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$winmd = "C:\Windows\System32\WinMetadata"
$gac   = "C:\Windows\Microsoft.NET\assembly\GAC_MSIL"

Get-ChildItem -Recurse -Path src -Filter *.cs | ForEach-Object { '"' + (Resolve-Path -Relative $_.FullName).TrimStart('.','\') + '"' } |
    Set-Content -Encoding ascii files.rsp

$cargs = @("/nologo","/noconfig","/target:winexe","/optimize+","/platform:anycpu","/utf8output","/warn:0","/out:$Out\NovaBar.exe")
if (Test-Path src\App\app.manifest) { $cargs += "/win32manifest:src\App\app.manifest" }
if (Test-Path src\App\app.ico)      { $cargs += "/win32icon:src\App\app.ico" }
if (Test-Path data\reference-widgets.json) { $cargs += "/resource:data\reference-widgets.json,NovaBar.reference-widgets.json" }
Get-ChildItem data\logos\*.png -ErrorAction SilentlyContinue | ForEach-Object { $cargs += "/resource:data\logos\$($_.Name),NovaBar.logo.$($_.Name)" }
foreach ($r in "System.dll","System.Core.dll","System.Drawing.dll","System.Windows.Forms.dll","System.Web.Extensions.dll","System.Security.dll","System.Management.dll","Microsoft.CSharp.dll","System.Xml.dll","System.Xml.Linq.dll") { $cargs += "/r:$r" }
$cargs += "/r:$fw\System.Runtime.WindowsRuntime.dll"
$cargs += "/r:$gac\System.Runtime\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.dll"
$cargs += "/r:$fw\System.Threading.Tasks.dll"
$cargs += "/r:$gac\System.Runtime.InteropServices.WindowsRuntime\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.InteropServices.WindowsRuntime.dll"
foreach ($w in "Foundation","Media","Devices","UI","Storage","System","ApplicationModel","Networking","Data") { $cargs += "/r:$winmd\Windows.$w.winmd" }
$cargs += "@files.rsp"

& "$fw\csc.exe" @cargs
if ($LASTEXITCODE -ne 0 -or -not (Test-Path "$Out\NovaBar.exe")) { Write-Error "BUILD FAILED"; exit 1 }
Remove-Item files.rsp
Write-Host "BUILD OK -> $Out\NovaBar.exe"
