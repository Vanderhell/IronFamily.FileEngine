#!/usr/bin/env pwsh
<#
.SYNOPSIS
ICFX CRC32 Parity Test: Compare C and .NET implementations on golden ICFX files

.DESCRIPTION
This script validates that C and .NET produce identical CRC values for ICFX files.
Tests both standard and indexed ICFX files.

.PARAMETER TestVectorsDir
Directory containing golden test vectors (default: vectors/small/icfx)
#>

param(
    [string]$TestVectorsDir = "vectors/small/icfx",
    [string]$IronconfigtoolExe = "tools/ironconfigtool/bin/Debug/net8.0/ironconfigtool.dll",
    [string]$CrcDiagnosticExe = "libs/ironcfg-c/build/Release/crc_diagnostic.exe"
)

$ErrorActionPreference = "Stop"

Write-Host "ICFX CRC32 Parity Test: C vs .NET" -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan

# Files to test
$TestFiles = @(
    "golden_icfx_crc.icfx",
    "golden_icfx_crc_index.icfx"
)

$FailCount = 0
$PassCount = 0

foreach ($file in $TestFiles) {
    $filePath = Join-Path $TestVectorsDir $file

    if (-not (Test-Path $filePath)) {
        Write-Host "SKIP: File not found: $filePath" -ForegroundColor Yellow
        continue
    }

    Write-Host "`nTesting: $file" -ForegroundColor Cyan
    Write-Host "---" -ForegroundColor Cyan

    # Get .NET CRC output
    Write-Host "Running .NET (dotnet) CRC check..." -NoNewline
    $dotnetOutput = & dotnet run --project $IronconfigtoolExe -- printcrc $filePath 2>$null

    # Extract CRC values from .NET output
    $dotnetCrc = $null
    $dotnetStored = $null

    foreach ($line in $dotnetOutput) {
        if ($line -match "Computed CRC: (0x[0-9A-F]+)") {
            $dotnetCrc = $matches[1]
        }
        if ($line -match "Stored CRC: (0x[0-9A-F]+)") {
            $dotnetStored = $matches[1]
        }
    }

    Write-Host " CRC=0x$($dotnetCrc -replace '0x','')" -ForegroundColor Green

    # Get C CRC output
    Write-Host "Running C (crc_diagnostic) CRC check..." -NoNewline
    $cOutput = & $CrcDiagnosticExe $filePath

    # Extract CRC values from C output
    $cCrc = $null
    $cStored = $null

    foreach ($line in $cOutput) {
        if ($line -match "Computed CRC: (0x[0-9A-F]+)") {
            $cCrc = $matches[1]
        }
        if ($line -match "Stored CRC: (0x[0-9A-F]+)") {
            $cStored = $matches[1]
        }
    }

    Write-Host " CRC=0x$($cCrc -replace '0x','')" -ForegroundColor Green

    # Compare results
    Write-Host "`nComparison:"
    Write-Host "  .NET Computed: $dotnetCrc"
    Write-Host "  C Computed:    $cCrc"
    Write-Host "  Stored:        $dotnetStored"

    if ($dotnetCrc -eq $cCrc -and $dotnetCrc -eq $dotnetStored) {
        Write-Host "  Status: PASS âś“ (CRC parity achieved)" -ForegroundColor Green
        $PassCount++
    } else {
        Write-Host "  Status: FAIL âś— (CRC mismatch)" -ForegroundColor Red
        if ($dotnetCrc -ne $cCrc) {
            Write-Host "    -> .NET and C differ: $dotnetCrc vs $cCrc" -ForegroundColor Red
        }
        if ($dotnetCrc -ne $dotnetStored) {
            Write-Host "    -> Computed != Stored: $dotnetCrc vs $dotnetStored" -ForegroundColor Red
        }
        $FailCount++
    }
}

Write-Host "`n=====================================================" -ForegroundColor Cyan
Write-Host "Results: $PassCount passed, $FailCount failed" -ForegroundColor $(if ($FailCount -eq 0) { "Green" } else { "Red" })
Write-Host "=====================================================" -ForegroundColor Cyan

exit $FailCount
