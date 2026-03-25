# Canonical Unit Test Suite - Enterprise Grade
# Runs ONLY core unit tests - deterministic, fast, no skips
# Gate: Fail=0, Skip=0

param(
    [string]$Configuration = "Release"
)

Write-Host "╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║         CANONICAL UNIT TEST SUITE (Enterprise)           ║" -ForegroundColor Cyan
Write-Host "║  Projects: IronCfg, Iupd, ILog (core engines only)       ║" -ForegroundColor Cyan
Write-Host "║  Explicit project list - NO filters, NO accidental runs ║" -ForegroundColor Cyan
Write-Host "╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

$repoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName
$solutionPath = Join-Path $repoRoot "libs/ironconfig-dotnet/IronConfig.sln"

# Explicit unit test projects (EXACT list, NO filters)
$unitProjects = @(
    "libs/ironconfig-dotnet/tests/IronConfig.IronCfgTests/IronConfig.IronCfgTests.csproj",
    "libs/ironconfig-dotnet/tests/IronConfig.Iupd.Tests/IronConfig.Iupd.Tests.csproj",
    "libs/ironconfig-dotnet/tests/IronConfig.ILog.Tests/IronConfig.ILog.Tests.csproj"
)

Write-Host "Running unit tests from explicit project list:" -ForegroundColor Green
$unitProjects | ForEach-Object { Write-Host "  ✓ $_" }
Write-Host ""

$totalPassed = 0
$totalFailed = 0
$totalSkipped = 0
$allPassed = $true

foreach ($project in $unitProjects) {
    $projectPath = Join-Path $repoRoot $project
    Write-Host "Testing: $(Split-Path $project -Leaf)" -ForegroundColor Yellow

    $output = & dotnet test $projectPath -c $Configuration --no-build 2>&1

    # Parse results
    if ($output -match "Passed!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+)") {
        $failed = [int]$matches[1]
        $passed = [int]$matches[2]
        $skipped = [int]$matches[3]

        $totalPassed += $passed
        $totalFailed += $failed
        $totalSkipped += $skipped

        Write-Host "  ✓ Passed: $passed, Failed: $failed, Skipped: $skipped" -ForegroundColor Green

        if ($failed -gt 0 -or $skipped -gt 0) {
            $allPassed = $false
        }
    } else {
        Write-Host "  ✗ Failed to parse results" -ForegroundColor Red
        $allPassed = $false
    }
}

Write-Host ""
Write-Host "╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║                    SUMMARY (Unit Suite)                   ║" -ForegroundColor Cyan
Write-Host "╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host "Total Passed:  $totalPassed" -ForegroundColor Green
Write-Host "Total Failed:  $totalFailed" -ForegroundColor $(if ($totalFailed -gt 0) { "Red" } else { "Green" })
Write-Host "Total Skipped: $totalSkipped" -ForegroundColor $(if ($totalSkipped -gt 0) { "Yellow" } else { "Green" })

if ($allPassed -and $totalFailed -eq 0 -and $totalSkipped -eq 0) {
    Write-Host ""
    Write-Host "✅ GATE PASSED: Fail=0, Skip=0 (Deterministic)" -ForegroundColor Green
    exit 0
} else {
    Write-Host ""
    Write-Host "❌ GATE FAILED" -ForegroundColor Red
    exit 1
}
