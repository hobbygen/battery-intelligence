# Release gate: the simulation providers and test doubles must be physically
# absent from the Release binaries, not just disabled by a flag a corrupt
# settings file could flip (specification section 61; prd.md N9).
#
# Builds -c Release and reflects over the shipping assemblies, failing if any
# type name looks like a simulation / fake harness.
#
# Usage:  powershell -ExecutionPolicy Bypass -File tools/verify-no-simulation.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

Write-Output 'Building -c Release …'
& dotnet build (Join-Path $root 'BatteryIntelligence.slnx') -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

$binDir = Join-Path $root 'src\BatteryIntelligence.App\bin\Release\net10.0-windows10.0.19041.0\win-x64'
$targets = @('BatteryIntelligence.dll', 'BatteryIntelligence.Battery.dll', 'BatteryIntelligence.Core.dll')

# Known simulation / fake type names that must not survive into Release. Matched
# as raw UTF-8 strings against the assembly bytes — no assembly load, so the
# WinAppSDK dependency graph is irrelevant, and a match on the #Strings heap is
# proof the type is present.
$forbidden = @(
    'SimulatedBatteryProvider',
    'BatterySimulationScenario',
    'FakeBattery',
    'FakeSettings',
    'FakeSessions'
)

$hits = @()
foreach ($name in $targets) {
    $path = Join-Path $binDir $name
    if (-not (Test-Path $path)) { throw "Not found: $path (did the Release build run?)" }
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    foreach ($needle in $forbidden) {
        if ($text.Contains($needle)) { $hits += "$name :: $needle" }
    }
}

if ($hits.Count -gt 0) {
    Write-Output ''
    Write-Output 'FAIL -- simulation / fake type names found in the Release binaries:'
    $hits | ForEach-Object { Write-Output "  $_" }
    exit 1
}

Write-Output "PASS -- none of [$($forbidden -join ', ')] in $($targets -join ', ')."
