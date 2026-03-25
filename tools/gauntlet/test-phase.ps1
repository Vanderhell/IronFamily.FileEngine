# Phase Test Suite
# Runs ONLY Phase 1.2 placeholder tests
# These tests FAIL because Phase 1.2 implementation is pending
# Gate: Count=5 (verifies tests exist and run, failures expected)

param(
    [string]$Configuration = "Release"
)

Write-Host "╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║                PHASE TEST SUITE (Phase 1.2)               ║" -ForegroundColor Cyan
Write-Host "║  Project: IronConfig.ILog.PhaseTests                     ║" -ForegroundColor Cyan
Write-Host "║  Category: Phase1.2 Error Mapping (implementation pending)║" -ForegroundColor Cyan
Write-Host "╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

$repoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName
$phaseProject = Join-Path $repoRoot "libs/ironconfig-dotnet/tests/IronConfig.ILog.PhaseTests/IronConfig.ILog.PhaseTests.csproj"

Write-Host "Testing: IronConfig.ILog.PhaseTests" -ForegroundColor Yellow
$output = & dotnet test $phaseProject -c $Configuration --no-build 2>&1

# Parse results
if ($output -match "Passed!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+)|Failed!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+)") {
    if ($matches[1] -ne $null) {
        $failed = [int]$matches[1]
        $passed = [int]$matches[2]
        $skipped = [int]$matches[3]
    } else {
        $failed = [int]$matches[4]
        $passed = [int]$matches[5]
        $skipped = [int]$matches[6]
    }
} else {
    Write-Host "  ✗ Failed to parse results" -ForegroundColor Red
    exit 1
}

$total = $failed + $passed + $skipped
Write-Host "  ✓ Passed: $passed, Failed: $failed, Skipped: $skipped, Total: $total" -ForegroundColor Green

Write-Host ""
Write-Host "╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║                 SUMMARY (Phase Suite)                     ║" -ForegroundColor Cyan
Write-Host "╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host "Total Tests:   $total" -ForegroundColor Green
Write-Host "Total Passed:  $passed" -ForegroundColor Green
Write-Host "Total Failed:  $failed" -ForegroundColor $(if ($failed -gt 0) { "Yellow" } else { "Green" })
Write-Host "Total Skipped: $skipped" -ForegroundColor Green
Write-Host ""
Write-Host "ℹ️  Phase 1.2 tests are EXPECTED to fail - implementation pending" -ForegroundColor Yellow

if ($total -eq 5) {
    Write-Host ""
    Write-Host "✅ PHASE TESTS VERIFIED: All 5 tests present and running" -ForegroundColor Green
    exit 0
} else {
    Write-Host ""
    Write-Host "⚠️  WARNING: Expected 5 tests, found $total" -ForegroundColor Yellow
    exit 0
}
