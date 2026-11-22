# One-shot generator for src/App/app.ico: rounded-square accent gradient with a white "N".
# Usage (from WSL): scripts/make_icon.sh   — or: powershell -File make_icon.ps1 -out C:\path\app.ico
param([string]$out = "app.ico")
Add-Type -AssemblyName System.Drawing
$sizes = 256, 64, 48, 32, 24, 16
$pngs = @()
foreach ($s in $sizes) {
  $b = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($b)
  $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'; $g.PixelOffsetMode = 'HighQuality'
  $g.Clear([System.Drawing.Color]::Transparent)
  $m = [Math]::Max(1, [int]($s * 0.04)); $w = $s - 2 * $m; $r = [single]($w * 0.26); $d = 2 * $r
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $p.AddArc($m, $m, $d, $d, 180, 90); $p.AddArc($m + $w - $d, $m, $d, $d, 270, 90)
  $p.AddArc($m + $w - $d, $m + $w - $d, $d, $d, 0, 90); $p.AddArc($m, $m + $w - $d, $d, $d, 90, 90); $p.CloseFigure()
  $rect = New-Object System.Drawing.RectangleF $m, $m, $w, $w
  $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 124, 140, 255)), ([System.Drawing.Color]::FromArgb(255, 86, 56, 220)), 45
  $g.FillPath($br, $p)
  if ($s -ge 32) { $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(70, 255, 255, 255)), ([single][Math]::Max(1, $s / 64)); $g.DrawPath($pen, $p) }
  $fs = [single]($w * 0.66)
  $f = New-Object System.Drawing.Font "Segoe UI", $fs, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
  $sf = New-Object System.Drawing.StringFormat; $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
  $tr = New-Object System.Drawing.RectangleF $m, ($m + $w * 0.02), $w, $w
  $g.DrawString("N", $f, [System.Drawing.Brushes]::White, $tr, $sf)
  $g.Dispose()
  $ms = New-Object System.IO.MemoryStream; $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $pngs += , $ms.ToArray(); $b.Dispose()
}
# ICO container with PNG-compressed entries (supported since Vista)
$fsOut = [System.IO.File]::Create($out); $bw = New-Object System.IO.BinaryWriter $fsOut
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
  $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
  $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$offset)
  $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $bw.Write($png) }
$bw.Close(); "wrote $out"
