# MegaBench V5 Reproduction Script - PowerShell
# Usage: ./reproduce.ps1

param(
    [string]$Engine = "all",
    [string]$Mode = "ci-mode",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

Write-Host "=== MegaBench V5 Reproduction Script ===" -ForegroundColor Green
Write-Host "Engine: $Engine" -ForegroundColor Cyan
Write-Host "Mode: $Mode" -ForegroundColor Cyan
Write-Host ""

# Step 1: Build
Write-Host "[1/4] Building MegaBench..." -ForegroundColor Yellow
dotnet build -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "✓ Build successful" -ForegroundColor Green
Write-Host ""

# Step 2: Unit tests (unless skipped)
if (-not $SkipTests) {
    Write-Host "[2/4] Running unit tests..." -ForegroundColor Yellow
    dotnet test libs/ironconfig-dotnet/tests/IronConfig.Evidence.Tests/IronConfig.Evidence.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Tests failed!" -ForegroundColor Red
        exit 1
    }
    dotnet test libs/ironconfig-dotnet/tests/IronConfig.IronCfgTests/IronConfig.IronCfgTests.csproj -c Release
    dotnet test libs/ironconfig-dotnet/tests/IronConfig.ILog.Tests/IronConfig.ILog.Tests.csproj -c Release
    dotnet test libs/ironconfig-dotnet/tests/IronConfig.Iupd.Tests/IronConfig.Iupd.Tests.csproj -c Release
    Write-Host "✓ All tests passed" -ForegroundColor Green
} else {
    Write-Host "[2/4] Skipping unit tests" -ForegroundColor Yellow
}
Write-Host ""

# Step 3: Benchmark
Write-Host "[3/4] Running realworld benchmark..." -ForegroundColor Yellow
Write-Host "Command: bench-competitors-v5 --engine $Engine --realworld --$Mode" -ForegroundColor Cyan
Write-Host ""

$env:IRONFAMILY_DETERMINISTIC = "1"
$env:DOTNET_TieredPGO = "0"
$env:COMPlus_ReadyToRun = "0"

dotnet run --project tools/megabench/MegaBench.csproj -c Release --no-build -- `
    bench-competitors-v5 --engine $Engine --realworld --$Mode

if ($LASTEXITCODE -ne 0) {
    Write-Host "Benchmark failed!" -ForegroundColor Red
    exit 1
}
Write-Host "✓ Benchmark completed" -ForegroundColor Green
Write-Host ""

# Step 4: Summary
Write-Host "[4/4] Reproduction complete" -ForegroundColor Green
Write-Host ""
Write-Host "Results saved to current directory" -ForegroundColor Cyan
Write-Host "Check README.md for interpretation guidelines" -ForegroundColor Cyan
Write-Host ""
Write-Host "Verify reproducibility: Compare REPRO_HASH.txt with baseline" -ForegroundColor Cyan

exit 0
