<#
.SYNOPSIS
    Regenerates every app-icon asset from branding/Battery_Intelligence_Icon.png.

.DESCRIPTION
    Source art is a square "squircle" tile on a white field. This script:
      1. Rescales the source to each target size (HighQualityBicubic).
      2. Flood-fills the white border in from the four corners and makes it
         transparent, so the rounded tile sits cleanly on a dark taskbar.
      3. Writes:
         - src/BatteryIntelligence.App/Assets/app.ico          (16..256, PNG frames)
         - src/BatteryIntelligence.App/Assets/Square44x44Logo.png
         - src/BatteryIntelligence.App/Assets/Square150x150Logo.png
         - src/BatteryIntelligence.App/Assets/Wide310x150Logo.png
         - src/BatteryIntelligence.App/Assets/StoreLogo.png
         - src/BatteryIntelligence.App/Assets/SplashScreen.png
         - website/favicon.ico, website/icon-512.png, website/icon-192.png,
           website/apple-touch-icon.png, website/og-image.png

    Windows PowerShell 5.1 (System.Drawing). Replaces the hand-rolled
    tools/generate-icon.ps1 and tools/generate-msix-assets.ps1 output.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/generate-app-icon.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repo    = Split-Path $PSScriptRoot -Parent
$src     = Join-Path $repo 'branding/Battery_Intelligence_Icon.png'
$assets  = Join-Path $repo 'src/BatteryIntelligence.App/Assets'
$web     = Join-Path $repo 'website'
if (-not (Test-Path $src)) { throw "Source art not found: $src" }
if (-not (Test-Path $web)) { New-Item -ItemType Directory -Path $web -Force | Out-Null }

$WHITE_THRESHOLD = 236   # R,G,B all >= this, from a corner, => background

# Returns a square Bitmap of the art at $size with the white border cleared.
function New-Tile([int]$size) {
    $source = [System.Drawing.Bitmap]::new($src)
    try {
        $bmp = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.DrawImage($source, [System.Drawing.Rectangle]::new(0, 0, $size, $size))
        $g.Dispose()
    }
    finally { $source.Dispose() }

    $rect = [System.Drawing.Rectangle]::new(0, 0, $size, $size)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadWrite, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $buf = New-Object 'byte[]' ($stride * $size)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buf, 0, $buf.Length)

    $visited = New-Object 'bool[]' ($size * $size)
    $stack = New-Object 'System.Collections.Generic.Stack[int]'
    foreach ($seed in @(0, ($size - 1), ($size * ($size - 1)), ($size * $size - 1))) { $stack.Push($seed) }

    while ($stack.Count -gt 0) {
        $p = $stack.Pop()
        if ($visited[$p]) { continue }
        $visited[$p] = $true

        $x = $p % $size
        $y = [int]([math]::Floor($p / $size))
        $i = ($y * $stride) + ($x * 4)      # B,G,R,A

        if ($buf[$i + 3] -ne 0) {
            $isWhite = ($buf[$i] -ge $WHITE_THRESHOLD) -and ($buf[$i + 1] -ge $WHITE_THRESHOLD) -and ($buf[$i + 2] -ge $WHITE_THRESHOLD)
            if (-not $isWhite) { continue }   # hit the tile edge — stop spreading here
            $buf[$i] = 0; $buf[$i + 1] = 0; $buf[$i + 2] = 0; $buf[$i + 3] = 0
        }

        if ($x -gt 0)          { $stack.Push($p - 1) }
        if ($x -lt $size - 1)  { $stack.Push($p + 1) }
        if ($y -gt 0)          { $stack.Push($p - $size) }
        if ($y -lt $size - 1)  { $stack.Push($p + $size) }
    }

    [System.Runtime.InteropServices.Marshal]::Copy($buf, 0, $data.Scan0, $buf.Length)
    $bmp.UnlockBits($data)
    return $bmp
}

# Draw the (already cleaned) master tile centred on a transparent WxH canvas.
function New-Canvas([System.Drawing.Bitmap]$tile, [int]$w, [int]$h, [double]$fill = 1.0) {
    $bmp = [System.Drawing.Bitmap]::new($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $side = [int]([math]::Min($w, $h) * $fill)
    $g.DrawImage($tile, [int](($w - $side) / 2), [int](($h - $side) / 2), $side, $side)
    $g.Dispose()
    return $bmp
}

function Save-Png([System.Drawing.Bitmap]$bmp, [string]$path) {
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "  $([System.IO.Path]::GetFileName($path))  ($($bmp.Width)x$($bmp.Height))"
}

function Save-Ico([System.Drawing.Bitmap]$master, [string]$path, [int[]]$sizes) {
    $frames = @()
    foreach ($s in $sizes) {
        $f = New-Canvas $master $s $s 1.0
        $ms = New-Object System.IO.MemoryStream
        $f.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $f.Dispose()
        $frames += , (@{ Size = $s; Bytes = $ms.ToArray() })
    }
    $fs = [System.IO.File]::Create($path)
    $bw = New-Object System.IO.BinaryWriter($fs)
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$frames.Count)
    $offset = 6 + (16 * $frames.Count)
    foreach ($fr in $frames) {
        $dim = if ($fr.Size -ge 256) { 0 } else { $fr.Size }
        $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32)
        $bw.Write([uint32]$fr.Bytes.Length); $bw.Write([uint32]$offset)
        $offset += $fr.Bytes.Length
    }
    foreach ($fr in $frames) { $bw.Write($fr.Bytes) }
    $bw.Flush(); $fs.Close()
    Write-Host "  $([System.IO.Path]::GetFileName($path))  ($($sizes -join ', '))"
}

Write-Host '==> Building the master tile (512px)...' -ForegroundColor Cyan
$master = New-Tile 512

Write-Host '==> App icon' -ForegroundColor Cyan
Save-Ico $master (Join-Path $assets 'app.ico') @(16, 24, 32, 48, 64, 128, 256)

Write-Host '==> MSIX / tile assets' -ForegroundColor Cyan
Save-Png (New-Canvas $master 44  44  0.92) (Join-Path $assets 'Square44x44Logo.png')
Save-Png (New-Canvas $master 150 150 0.92) (Join-Path $assets 'Square150x150Logo.png')
Save-Png (New-Canvas $master 310 150 0.62) (Join-Path $assets 'Wide310x150Logo.png')
Save-Png (New-Canvas $master 50  50  0.92) (Join-Path $assets 'StoreLogo.png')
Save-Png (New-Canvas $master 620 300 0.55) (Join-Path $assets 'SplashScreen.png')

Write-Host '==> Website icons' -ForegroundColor Cyan
Save-Ico $master (Join-Path $web 'favicon.ico') @(16, 32, 48)
Save-Png (New-Canvas $master 512 512 1.0)  (Join-Path $web 'icon-512.png')
Save-Png (New-Canvas $master 192 192 1.0)  (Join-Path $web 'icon-192.png')
Save-Png (New-Canvas $master 180 180 1.0)  (Join-Path $web 'apple-touch-icon.png')

# Open Graph card: icon top-left on the brand navy, product name + tagline below.
$og = [System.Drawing.Bitmap]::new(1200, 630, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$gg = [System.Drawing.Graphics]::FromImage($og)
$gg.Clear([System.Drawing.Color]::FromArgb(255, 15, 27, 70))
$gg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$gg.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$gg.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$gg.DrawImage($master, 96, 96, 150, 150)

$fTitle = [System.Drawing.Font]::new('Segoe UI Semibold', 66, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$fTag   = [System.Drawing.Font]::new('Segoe UI', 33, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$white  = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
$muted  = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 168, 184, 224))
$accent = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 94, 234, 141))
$gg.DrawString('Battery Intelligence', $fTitle, $white, 96, 300)
$gg.DrawString('Understand your laptop battery, power draw and', $fTag, $muted, 100, 400)
$gg.DrawString('health - locally, with no account, no fabricated numbers.', $fTag, $muted, 100, 446)
$gg.DrawString('Free   /   Windows 10 and 11   /   Open source', $fTag, $accent, 100, 520)
$gg.Dispose()
Save-Png $og (Join-Path $web 'og-image.png')

$master.Dispose()
Write-Host '==> Done.' -ForegroundColor Green
