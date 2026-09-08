<#
.SYNOPSIS
    Builds the Battery Intelligence Setup.exe — a single self-contained,
    machine-wide installer that runs on any x64 Windows 10 (17763+) / 11 laptop
    with nothing pre-installed.

.DESCRIPTION
    1. Publishes the App self-contained (bundles .NET 10 + the Windows App SDK)
       to dist/self-contained.
    2. Compiles tools/installer/BatteryIntelligence.iss with Inno Setup 6.
    3. Writes dist/SHA256SUMS.txt.

    Inno Setup 6 is required (winget install --id JRSoftware.InnoSetup).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/build-installer.ps1
#>
[CmdletBinding()]
param(
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo

$publishDir = Join-Path $repo 'dist/self-contained'
$iss        = Join-Path $repo 'tools/installer/BatteryIntelligence.iss'

if (-not $SkipPublish) {
    Write-Host '==> Publishing self-contained...' -ForegroundColor Cyan
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    dotnet publish src/BatteryIntelligence.App/BatteryIntelligence.App.csproj `
        -c Release -r win-x64 --self-contained true `
        -p:WindowsAppSDKSelfContained=true -p:PublishReadyToRun=false `
        --output $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
}

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'ISCC.exe not found. Install Inno Setup 6.' }

Write-Host "==> Compiling installer with $iscc" -ForegroundColor Cyan
& $iscc $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }

Write-Host '==> Writing dist/SHA256SUMS.txt' -ForegroundColor Cyan
Push-Location (Join-Path $repo 'dist')
Get-ChildItem *.exe, *.zip |
    Get-FileHash -Algorithm SHA256 |
    ForEach-Object { $_.Hash + '  ' + (Split-Path $_.Path -Leaf) } |
    Tee-Object -FilePath 'SHA256SUMS.txt'
Pop-Location

Write-Host '==> Done.' -ForegroundColor Green
