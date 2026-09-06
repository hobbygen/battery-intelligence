# Generates the MSIX visual assets under src/BatteryIntelligence.App/Assets/ --
# a solid tile with the same battery glyph and palette as generate-icon.ps1 --
# without any image library. PNGs are written by hand (IHDR + a single stored
# zlib IDAT + IEND) so the assets are reproducible from source, matching the
# "assets from source, not opaque binaries" convention (see generate-icon.ps1).
#
# A designer can replace these with proper artwork at any time; the file names
# are what Package.appxmanifest references.
#
# Usage:  powershell -ExecutionPolicy Bypass -File tools/generate-msix-assets.ps1

$ErrorActionPreference = 'Stop'

$assetDir = Join-Path $PSScriptRoot '..\src\BatteryIntelligence.App\Assets'
if (-not (Test-Path $assetDir)) { New-Item -ItemType Directory -Path $assetDir -Force | Out-Null }

# --- CRC-32 (int64 math, masked to 32 bits -- Windows PowerShell has no unsigned arithmetic) ---
$crcTable = New-Object 'int64[]' 256
for ($n = 0; $n -lt 256; $n++) {
    $c = [int64]$n
    for ($k = 0; $k -lt 8; $k++) {
        if ($c -band 1) { $c = (0xEDB88320L -bxor ($c -shr 1)) -band 0xFFFFFFFFL }
        else { $c = ($c -shr 1) -band 0xFFFFFFFFL }
    }
    $crcTable[$n] = $c
}
function Get-Crc32([byte[]]$bytes) {
    $c = [int64]0xFFFFFFFFL
    foreach ($b in $bytes) { $c = ($crcTable[[int](($c -bxor $b) -band 0xFF)] -bxor ($c -shr 8)) -band 0xFFFFFFFFL }
    return [uint32](($c -bxor 0xFFFFFFFFL) -band 0xFFFFFFFFL)
}

function Write-BE-UInt32([System.IO.BinaryWriter]$w, [uint32]$v) {
    $w.Write([byte](($v -shr 24) -band 0xFF)); $w.Write([byte](($v -shr 16) -band 0xFF))
    $w.Write([byte](($v -shr 8) -band 0xFF));  $w.Write([byte]($v -band 0xFF))
}

function Write-Chunk([System.IO.BinaryWriter]$w, [string]$type, [byte[]]$data) {
    Write-BE-UInt32 $w ([uint32]$data.Length)
    $typeBytes = [System.Text.Encoding]::ASCII.GetBytes($type)
    $w.Write($typeBytes)
    if ($data.Length) { $w.Write($data) }
    $crcInput = New-Object 'byte[]' ($typeBytes.Length + $data.Length)
    [Array]::Copy($typeBytes, 0, $crcInput, 0, $typeBytes.Length)
    if ($data.Length) { [Array]::Copy($data, 0, $crcInput, $typeBytes.Length, $data.Length) }
    Write-BE-UInt32 $w (Get-Crc32 $crcInput)
}

# Adler-32 over the raw (pre-compression) scanline bytes.
function Get-Adler32([byte[]]$bytes) {
    $a = [int64]1; $b = [int64]0
    foreach ($byte in $bytes) {
        $a = ($a + $byte) % 65521
        $b = ($b + $a) % 65521
    }
    return [uint32](((($b -shl 16) -bor $a)) -band 0xFFFFFFFFL)
}

function New-Png([int]$width, [int]$height, [string]$path) {
    # Palette (RGBA). Dark slate ground + the healthy-green battery, white shell.
    $bg = @([byte]26, [byte]27, [byte]30, [byte]255)
    $shell = @([byte]255, [byte]255, [byte]255, [byte]255)
    $fill = @([byte]108, [byte]203, [byte]95, [byte]255)

    # Raw scanlines: each row prefixed with a 0 filter byte, RGBA pixels.
    $raw = New-Object System.Collections.Generic.List[byte]
    # Battery rectangle occupies the centred 60% box.
    $bx0 = [int]($width * 0.20); $bx1 = [int]($width * 0.74)
    $by0 = [int]($height * 0.30); $by1 = [int]($height * 0.70)
    $nub0 = $bx1 + 1; $nub1 = [int]($width * 0.80)
    $border = [math]::Max(1, [int]($width * 0.03))
    $level = $bx0 + [int](($bx1 - $bx0) * 0.68)

    for ($y = 0; $y -lt $height; $y++) {
        $raw.Add([byte]0)
        for ($x = 0; $x -lt $width; $x++) {
            $p = $bg
            $inBody = ($x -ge $bx0 -and $x -le $bx1 -and $y -ge $by0 -and $y -le $by1)
            $inNub  = ($x -ge $nub0 -and $x -le $nub1 -and $y -ge ($by0 + ($by1 - $by0) / 4) -and $y -le ($by1 - ($by1 - $by0) / 4))
            if ($inNub) { $p = $shell }
            elseif ($inBody) {
                $onBorder = ($x -lt $bx0 + $border -or $x -gt $bx1 - $border -or $y -lt $by0 + $border -or $y -gt $by1 - $border)
                if ($onBorder) { $p = $shell }
                elseif ($x -le $level) { $p = $fill }
                else { $p = $bg }
            }
            $raw.Add($p[0]); $raw.Add($p[1]); $raw.Add($p[2]); $raw.Add($p[3])
        }
    }
    $rawArr = $raw.ToArray()

    # zlib stream: 2-byte header (0x78 0x01) + stored DEFLATE blocks + Adler-32.
    $zlib = New-Object System.Collections.Generic.List[byte]
    $zlib.Add([byte]0x78); $zlib.Add([byte]0x01)
    $offset = 0
    while ($offset -lt $rawArr.Length) {
        $blockLen = [math]::Min(65535, $rawArr.Length - $offset)
        $final = if (($offset + $blockLen) -ge $rawArr.Length) { 1 } else { 0 }
        $zlib.Add([byte]$final)                              # BFINAL, BTYPE=00
        $zlib.Add([byte]($blockLen -band 0xFF)); $zlib.Add([byte](($blockLen -shr 8) -band 0xFF))
        $nlen = (-bnot $blockLen) -band 0xFFFF
        $zlib.Add([byte]($nlen -band 0xFF)); $zlib.Add([byte](($nlen -shr 8) -band 0xFF))
        for ($i = 0; $i -lt $blockLen; $i++) { $zlib.Add($rawArr[$offset + $i]) }
        $offset += $blockLen
    }
    $adler = Get-Adler32 $rawArr
    $zlib.Add([byte](($adler -shr 24) -band 0xFF)); $zlib.Add([byte](($adler -shr 16) -band 0xFF))
    $zlib.Add([byte](($adler -shr 8) -band 0xFF));  $zlib.Add([byte]($adler -band 0xFF))

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([byte[]](137, 80, 78, 71, 13, 10, 26, 10))    # PNG signature

    $ihdr = New-Object System.IO.MemoryStream
    $iw = New-Object System.IO.BinaryWriter($ihdr)
    Write-BE-UInt32 $iw ([uint32]$width); Write-BE-UInt32 $iw ([uint32]$height)
    $iw.Write([byte]8); $iw.Write([byte]6); $iw.Write([byte]0); $iw.Write([byte]0); $iw.Write([byte]0)   # 8-bit RGBA
    $iw.Flush()
    Write-Chunk $bw 'IHDR' $ihdr.ToArray()
    Write-Chunk $bw 'IDAT' $zlib.ToArray()
    Write-Chunk $bw 'IEND' (New-Object 'byte[]' 0)
    $bw.Flush()

    [System.IO.File]::WriteAllBytes($path, $ms.ToArray())
    $bw.Dispose(); $ms.Dispose(); $iw.Dispose(); $ihdr.Dispose()
    Write-Output "  $((Split-Path $path -Leaf))  ${width}x${height}"
}

Write-Output 'Generating MSIX assets:'
New-Png 44   44   (Join-Path $assetDir 'Square44x44Logo.png')
New-Png 150  150  (Join-Path $assetDir 'Square150x150Logo.png')
New-Png 310  150  (Join-Path $assetDir 'Wide310x150Logo.png')
New-Png 50   50   (Join-Path $assetDir 'StoreLogo.png')
New-Png 620  300  (Join-Path $assetDir 'SplashScreen.png')
Write-Output 'Done.'
