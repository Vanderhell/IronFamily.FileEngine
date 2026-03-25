# Performance Test Suite
# Runs ONLY performance/GC characteristic tests
# Gate: Fail=0 (skips allowed for performance variance)

param(
    [string]$Configuration = "Release"
)

Write-Host "╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║              PERFORMANCE TEST SUITE                       ║" -ForegroundColor Cyan
Write-Host "║  Project: IronConfig.PerfTests                           ║" -ForegroundColor Cyan
Write-Host "║  Category: Perf (GC allocation, hot path measurements)   ║" -ForegroundColor Cyan
Write-Host "╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

$repoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName
$perfProject = Join-Path $repoRoot "libs/ironconfig-dotnet/tests/IronConfig.PerfTests/IronConfig.PerfTests.csproj"

Write-Host "Testing: IronConfig.PerfTests" -ForegroundColor Yellow
$output = & dotnet test $perfProject -c $Configuration --no-build 2>&1

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

Write-Host "  ✓ Passed: $passed, Failed: $failed, Skipped: $skipped" -ForegroundColor Green

Write-Host ""
Write-Host "╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║                  SUMMARY (Perf Suite)                     ║" -ForegroundColor Cyan
Write-Host "╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host "Total Passed:  $passed" -ForegroundColor Green
Write-Host "Total Failed:  $failed" -ForegroundColor $(if ($failed -gt 0) { "Red" } else { "Green" })
Write-Host "Total Skipped: $skipped" -ForegroundColor $(if ($skipped -gt 0) { "Yellow" } else { "Green" })

if ($failed -eq 0) {
    Write-Host ""
    Write-Host "✅ GATE PASSED: Fail=0" -ForegroundColor Green
    exit 0
} else {
    Write-Host ""
    Write-Host "❌ GATE FAILED: Performance regression detected" -ForegroundColor Red
    exit 1
}
