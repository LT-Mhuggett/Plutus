# Rasterises the Plutus mark to PNG/ICO using GDI+ (no node/ImageMagick needed).
# The geometry below MUST mirror <app>/public/icon.svg and src/PlutusMark.tsx
# (64-unit design grid) — change one, change all three, then re-run:
#
#   .\make-icons.ps1 -OutDirs .\Plutus.Frontend.WebApp\public, .\Plutus.Frontend.Portal\public
#
# ...and copy icon.svg across by hand. Windows PowerShell 5.1 (System.Drawing).
param([Parameter(Mandatory=$true)][string[]]$OutDirs)

Add-Type -AssemblyName System.Drawing

function New-RoundedRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = $r * 2
  $p.AddArc($x, $y, $d, $d, 180, 90)
  $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
  $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
  $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
  $p.CloseFigure()
  return $p
}

# The "P" glyph on the 64 grid, as a single filled path (three sub-figures).
function New-GlyphPath {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  # Winding, not the default Alternate: the three sub-figures overlap and must union,
  # while the bowl's counter-wound inner arc still punches its hole.
  $p.FillMode = [System.Drawing.Drawing2D.FillMode]::Winding
  # bowl: outer arc top->bottom (clockwise), then inner arc bottom->top
  $p.StartFigure()
  $p.AddArc(18.5, 9.5, 28.0, 28.0, -90, 180)   # outer r14 @ (32.5,23.5) — centred on the stem's right edge
  $p.AddLine(32.5, 37.5, 32.5, 30.5)
  $p.AddArc(25.5, 16.5, 14.0, 14.0, 90, -180)  # inner r7 @ (32.5,23.5)
  $p.CloseFigure()
  $p.StartFigure(); $p.AddRectangle((New-Object System.Drawing.RectangleF(24.5, 9.5, 8.0, 45.0)))  # stem
  $p.StartFigure(); $p.AddRectangle((New-Object System.Drawing.RectangleF(17.5, 43.0, 22.0, 6.0))) # currency bar
  return $p
}

# size px; $Maskable = full-bleed square tile with the glyph inset to the 80% safe zone.
function New-IconBitmap([int]$size, [bool]$Maskable) {
  $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $g.Clear([System.Drawing.Color]::Transparent)

  $s = $size / 64.0
  $g.ScaleTransform($s, $s)

  $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF(0, 64)),
    [System.Drawing.ColorTranslator]::FromHtml('#3d86b0'),
    [System.Drawing.ColorTranslator]::FromHtml('#2c698d'))

  if ($Maskable) {
    $g.FillRectangle($brush, (New-Object System.Drawing.RectangleF(0, 0, 64, 64)))
  } else {
    $tile = New-RoundedRect 0 0 64 64 13
    $g.FillPath($brush, $tile)
    $tile.Dispose()
  }

  $glyph = New-GlyphPath
  if ($Maskable) {
    # shrink the glyph about the tile centre so it survives Android's circular mask
    $m = New-Object System.Drawing.Drawing2D.Matrix
    $m.Translate(32, 32); $m.Scale(0.62, 0.62); $m.Translate(-32, -32)
    $glyph.Transform($m); $m.Dispose()
  }
  $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
  $g.FillPath($white, $glyph)

  $glyph.Dispose(); $white.Dispose(); $brush.Dispose(); $g.Dispose()
  return $bmp
}

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $bytes = $ms.ToArray(); $ms.Dispose(); return , $bytes  # leading comma: stop PS unrolling the byte[]
}

# PNG-compressed ICO (Vista+ / all modern browsers)
function Write-Ico([string]$path, [int[]]$sizes) {
  $pngs = @()
  foreach ($s in $sizes) { $b = New-IconBitmap $s $false; $pngs += , (Get-PngBytes $b); $b.Dispose() }
  $fs = [System.IO.File]::Create($path)
  $bw = New-Object System.IO.BinaryWriter($fs)
  $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
  $offset = 6 + 16 * $sizes.Count
  for ($i = 0; $i -lt $sizes.Count; $i++) {
    $w = $sizes[$i]; if ($w -ge 256) { $w = 0 }
    $bw.Write([byte]$w); $bw.Write([byte]$w); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
  }
  foreach ($p in $pngs) { $bw.Write([byte[]]$p, 0, $p.Length) }
  $bw.Flush(); $bw.Dispose(); $fs.Dispose()
}

foreach ($dir in $OutDirs) {
  if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
  foreach ($spec in @(@{n = 'apple-touch-icon.png'; s = 180 }, @{n = 'icon-192.png'; s = 192 }, @{n = 'icon-512.png'; s = 512 })) {
    $b = New-IconBitmap $spec.s $false
    $b.Save((Join-Path $dir $spec.n), [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
  }
  $b = New-IconBitmap 512 $true
  $b.Save((Join-Path $dir 'icon-512-maskable.png'), [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
  Write-Ico (Join-Path $dir 'favicon.ico') @(16, 32, 48)
  "wrote icons -> $dir"
}
