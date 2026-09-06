# Generates Assets/app.ico — a simple battery glyph — without any image library.
#
# Writing the ICO bytes directly keeps the build free of a System.Drawing
# dependency and makes the icon reproducible from source rather than being an
# opaque binary checked into the tree.
#
# Usage:  powershell -ExecutionPolicy Bypass -File tools/generate-icon.ps1

$ErrorActionPreference = 'Stop'

$size = 32
$outPath = Join-Path $PSScriptRoot '..\src\BatteryIntelligence.App\Assets\app.ico'
$outDir = Split-Path $outPath -Parent
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

# BGRA pixel buffer, top-down while drawing; flipped on write.
$px = New-Object 'byte[]' ($size * $size * 4)

function Set-Pixel([int]$x, [int]$y, [byte]$b, [byte]$g, [byte]$r, [byte]$a) {
    if ($x -lt 0 -or $y -lt 0 -or $x -ge $size -or $y -ge $size) { return }
    $i = (($y * $size) + $x) * 4
    $px[$i] = $b; $px[$i + 1] = $g; $px[$i + 2] = $r; $px[$i + 3] = $a
}

function Fill-Rect([int]$x0, [int]$y0, [int]$x1, [int]$y1, [byte]$b, [byte]$g, [byte]$r, [byte]$a) {
    for ($y = $y0; $y -le $y1; $y++) {
        for ($x = $x0; $x -le $x1; $x++) { Set-Pixel $x $y $b $g $r $a }
    }
}

# Palette (BGRA). White shell reads on both light and dark taskbars;
# green fill matches the "healthy" semantic colour from docs/ui-navigation.md.
$shellB = 255; $shellG = 255; $shellR = 255
$fillB  = 95;  $fillG  = 203; $fillR  = 108

# Battery body outline: 2px border, x 3..25, y 8..23
Fill-Rect 3 8 25 23 $shellB $shellG $shellR 255
Fill-Rect 5 10 23 21 0 0 0 0          # hollow out the interior

# Positive terminal nub on the right
Fill-Rect 26 13 28 18 $shellB $shellG $shellR 255

# Charge level fill (about 70%)
Fill-Rect 7 12 18 19 $fillB $fillG $fillR 255

# --- assemble the ICO ------------------------------------------------------
$xorStride = $size * 4
$andStride = [math]::Ceiling($size / 32.0) * 4   # 1bpp rows padded to 4 bytes
$xorSize = $xorStride * $size
$andSize = $andStride * $size
$imageSize = 40 + $xorSize + $andSize

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)

# ICONDIR
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]1)
# ICONDIRENTRY
$bw.Write([byte]$size); $bw.Write([byte]$size); $bw.Write([byte]0); $bw.Write([byte]0)
$bw.Write([uint16]1); $bw.Write([uint16]32)
$bw.Write([uint32]$imageSize); $bw.Write([uint32]22)

# BITMAPINFOHEADER — height is doubled to cover the XOR and AND masks.
$bw.Write([uint32]40); $bw.Write([int32]$size); $bw.Write([int32]($size * 2))
$bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]0)
$bw.Write([uint32]($xorSize + $andSize))
$bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)

# XOR mask, bottom-up
for ($y = $size - 1; $y -ge 0; $y--) {
    $bw.Write($px, $y * $xorStride, $xorStride)
}

# AND mask — zeroed; transparency comes from the alpha channel.
$bw.Write((New-Object 'byte[]' $andSize))

$bw.Flush()
[System.IO.File]::WriteAllBytes((Resolve-Path -LiteralPath $outDir).Path + '\app.ico', $ms.ToArray())
$bw.Dispose(); $ms.Dispose()

Write-Output "Wrote $outPath ($imageSize bytes of image data)"
