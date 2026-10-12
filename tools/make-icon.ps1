<#
.SYNOPSIS
    Draws the onnoRDD logo mark and writes the application icon (.ico, 16-256 px) and a 512 px PNG.

.DESCRIPTION
    Same geometry as the vector mark in the app (src\MediaspotIngest.App\Controls\LogoMark.xaml), on a 24-unit grid:
    a blue rounded square (Mediaspot primary #2D6CE1), a white tape reel, the tape leaving it, and a yellow dot
    (Mediaspot accent #F7C035) where it lands: a reel going to Mediaspot.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
#>
param([string] $OutputDirectory = (Join-Path $PSScriptRoot '..\src\MediaspotIngest.App\Assets'))

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-Mark([int] $size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.ScaleTransform($size / 24.0, $size / 24.0)

    $blue = [System.Drawing.Color]::FromArgb(255, 0x2D, 0x6C, 0xE1)
    $white = [System.Drawing.Color]::White
    $yellow = [System.Drawing.Color]::FromArgb(255, 0xF7, 0xC0, 0x35)

    # Rounded square
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = 6.0
    $path.AddArc(0, 0, $r, $r, 180, 90); $path.AddArc(24 - $r, 0, $r, $r, 270, 90)
    $path.AddArc(24 - $r, 24 - $r, $r, $r, 0, 90); $path.AddArc(0, 24 - $r, $r, $r, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush $blue), $path)

    # Tape leaving the reel
    $tape = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF 8.2, 9.2), (New-Object System.Drawing.PointF 14.6, 4.6),
        (New-Object System.Drawing.PointF 18.4, 4.6), (New-Object System.Drawing.PointF 12.0, 9.2))
    $g.FillPolygon((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(230, 255, 255, 255))), $tape)

    # Reel and hub
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $white), 3.2, 9.0, 10.6, 10.6)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $blue), 6.9, 12.7, 3.2, 3.2)

    # Destination dot
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $yellow), 16.4, 13.4, 4.4, 4.4)

    $g.Dispose()
    return $bitmap
}

New-Item -ItemType Directory -Force $OutputDirectory | Out-Null

# ICO with PNG-compressed entries (supported since Windows Vista)
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($s in $sizes) {
    $bmp = New-Mark $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

$icoPath = Join-Path $OutputDirectory 'onnoRDD.ico'
$out = New-Object System.IO.BinaryWriter ([System.IO.File]::Create($icoPath))
$out.Write([UInt16]0); $out.Write([UInt16]1); $out.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $out.Write([byte]$dim); $out.Write([byte]$dim); $out.Write([byte]0); $out.Write([byte]0)
    $out.Write([UInt16]1); $out.Write([UInt16]32)
    $out.Write([UInt32]$images[$i].Length); $out.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $out.Write($img) }
$out.Close()

$png = New-Mark 512
$png.Save((Join-Path $OutputDirectory 'onnoRDD-512.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$png.Dispose()

"Icon written to $icoPath"
