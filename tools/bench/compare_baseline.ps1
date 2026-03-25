param(
    [string]$BaselineFile = "./bench/baselines/baseline.json",
    [string]$CurrentFile = "./artifacts/bench/profiles_bench.json",
    [string]$BudgetsFile = "./bench/budgets/budgets.json",
    [string]$ReportFile = "./artifacts/bench/PERF_BUDGET_REPORT.md"
)

# Load JSON files
$baseline = Get-Content $BaselineFile | ConvertFrom-Json
$current = Get-Content $CurrentFile | ConvertFrom-Json
$budgets = Get-Content $BudgetsFile | ConvertFrom-Json

# Create output directory
New-Item -ItemType Directory -Force -Path (Split-Path $ReportFile) | Out-Null

# Initialize report and tracking
$report = "# Performance Budget Gate Report`n`n"
$hasFail = $false
$hasWarn = $false
$regressions = @()
$rowsCompared = 0
$metricsCompared = 0
$baselineRowCount = $baseline.Count
$currentRowCount = $current.Count

# Create lookup for baseline
$baselineMap = @{}
foreach ($item in $baseline) {
    $key = "{0}_{1}_{2}" -f $item.engine, $item.profile, $item.dataset
    $baselineMap[$key] = $item
}

# Compare each metric
$metrics = @(
    "size_bytes",
    "encode_ms_median",
    "decode_ms_median",
    "validate_fast_ms_median",
    "validate_strict_ms_median",
    "managed_alloc_bytes_median",
    "working_set_bytes_median"
)

$report += "## Data Availability`n`n"
$report += "| Metric | Value |`n"
$report += "|--------|-------|`n"
$report += "| Rows in baseline | {0} |`n" -f $baselineRowCount
$report += "| Rows in current | {0} |`n" -f $currentRowCount

$report += "`n## Summary`n`n"
$report += "| Engine | Profile | Dataset | Metric | Baseline | Current | Delta | Status |`n"
$report += "|--------|---------|---------|--------|----------|---------|-------|--------|`n"

foreach ($item in $current) {
    $key = "{0}_{1}_{2}" -f $item.engine, $item.profile, $item.dataset
    $baseItem = $baselineMap[$key]

    if ($null -eq $baseItem) {
        continue
    }

    $rowsCompared++

    foreach ($metric in $metrics) {
        $baseVal = $baseItem.$metric
        $curVal = $item.$metric

        if ($baseVal -eq 0) {
            continue
        }

        $metricsCompared++
        $pctChange = (($curVal - $baseVal) / $baseVal) * 100
        $budgetInfo = $budgets.$metric

        $status = "OK"
        if ($null -ne $budgetInfo) {
            if ($pctChange -ge $budgetInfo.fail_pct) {
                $status = "FAIL"
                $hasFail = $true
            } elseif ($pctChange -ge $budgetInfo.warn_pct) {
                $status = "WARN"
                $hasWarn = $true
            }
        }

        # Output ALL rows to table (not just failures)
        $report += "| {0} | {1} | {2} | {3} | {4:F3} | {5:F3} | {6:+0.0;-#.0}% | {7} |`n" -f `
            $item.engine, $item.profile, $item.dataset, $metric, $baseVal, $curVal, $pctChange, $status

        if ($status -eq "FAIL") {
            $regressions += @{
                engine = $item.engine
                profile = $item.profile
                dataset = $item.dataset
                metric = $metric
                change = $pctChange
            }
        }
    }
}

# Add comparison metrics to report
$report += "`n## Comparison Metrics`n`n"
$report += "| Metric | Value |`n"
$report += "|--------|-------|`n"
$report += "| Rows compared | {0} |`n" -f $rowsCompared
$report += "| Metrics compared | {0} |`n" -f $metricsCompared

$report += "`n## Result`n`n"

if ($rowsCompared -eq 0 -or $metricsCompared -eq 0) {
    $report += "**FAIL**: No valid comparisons (rows={0}, metrics={1})`n" -f $rowsCompared, $metricsCompared
    $report | Out-File -FilePath $ReportFile -Encoding UTF8
    Write-Host $report
    Write-Host "FAIL: No valid performance data for comparison" -ForegroundColor Red
    exit 2
}

if ($regressions.Count -gt 0) {
    $report += "`n## Top Regressions`n`n"
    $report += "Found {0} regression(s):`n`n" -f $regressions.Count
    $regressions | Sort-Object { [Math]::Abs($_.change) } -Descending | ForEach-Object {
        $report += "- **{0}/{1}/{2}**: {3} = {4:+0.0;-#.0}%`n" -f `
            $_.engine, $_.profile, $_.dataset, $_.metric, $_.change
    }
} else {
    $report += "No regressions detected.`n"
}

$report | Out-File -FilePath $ReportFile -Encoding UTF8

Write-Host $report

if ($hasFail) {
    Write-Host "FAIL: Performance budgets exceeded" -ForegroundColor Red
    exit 1
}

if ($hasWarn) {
    Write-Host "WARN: Performance warnings detected" -ForegroundColor Yellow
}

Write-Host "PASS: Performance within budgets" -ForegroundColor Green
exit 0
