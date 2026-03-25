param(
    [string]$MemoryFile = "./artifacts/bench/memory_profiles.json",
    [string]$ReportFile = "./artifacts/bench/MEMORY_PROFILE_REPORT.md"
)

# Load memory profiles
$profiles = Get-Content $MemoryFile | ConvertFrom-Json

# Create output directory
New-Item -ItemType Directory -Force -Path (Split-Path $ReportFile) | Out-Null

# Initialize report
$report = "# Memory Profile Report`n`n"
$report += "**Generated**: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')`n`n"

# Summary statistics
$report += "## Summary`n`n"
$report += "| Metric | Value |`n"
$report += "|--------|-------|`n"
$report += "| Total Profiles | {0} |`n" -f $profiles.Count
$report += "| Total Allocated Bytes | {0:N0} |`n" -f ($profiles | Measure-Object -Property allocated_bytes_delta -Sum).Sum
$report += "| Total Heap Delta | {0:N0} |`n" -f ($profiles | Measure-Object -Property heap_delta_bytes -Sum).Sum
$report += "| Total Working Set Delta | {0:N0} |`n" -f ($profiles | Measure-Object -Property working_set_delta_bytes -Sum).Sum
$report += "| Max GC Collections | {0} |`n" -f ($profiles | Measure-Object -Property gc_collection_count -Maximum).Maximum

# Separate CI and heavy scenarios
$ciScenarios = $profiles | Where-Object { $_.scenario -notmatch '(large|mega)' } | Group-Object -Property scenario
$heavyScenarios = $profiles | Where-Object { $_.scenario -match '(large|mega)' } | Group-Object -Property scenario

# CI scenarios
$report += "`n## CI Scenarios (small/medium datasets)`n`n"
$report += "| Scenario | Allocated (bytes) | Heap Delta (bytes) | Working Set Delta (bytes) | GC Count |`n"
$report += "|----------|------|-----|-----|----------|`n"

foreach ($group in $ciScenarios | Sort-Object { $_.Group[0].allocated_bytes_delta } -Descending) {
    $scenario = $group.Name
    $alloc = ($group.Group | Measure-Object -Property allocated_bytes_delta -Average).Average
    $heap = ($group.Group | Measure-Object -Property heap_delta_bytes -Average).Average
    $ws = ($group.Group | Measure-Object -Property working_set_delta_bytes -Average).Average
    $gc = ($group.Group | Measure-Object -Property gc_collection_count -Sum).Sum

    $report += "| {0} | {1:F0} | {2:F0} | {3:F0} | {4} |`n" -f `
        $scenario, $alloc, $heap, $ws, $gc
}

# Heavy scenarios
if ($heavyScenarios.Count -gt 0) {
    $report += "`n## Heavy Scenarios (large/mega datasets)`n`n"
    $report += "| Scenario | Allocated (bytes) | Heap Delta (bytes) | Working Set Delta (bytes) | GC Count |`n"
    $report += "|----------|------|-----|-----|----------|`n"

    foreach ($group in $heavyScenarios | Sort-Object { $_.Group[0].allocated_bytes_delta } -Descending) {
        $scenario = $group.Name
        $alloc = ($group.Group | Measure-Object -Property allocated_bytes_delta -Average).Average
        $heap = ($group.Group | Measure-Object -Property heap_delta_bytes -Average).Average
        $ws = ($group.Group | Measure-Object -Property working_set_delta_bytes -Average).Average
        $gc = ($group.Group | Measure-Object -Property gc_collection_count -Sum).Sum

        $report += "| {0} | {1:F0} | {2:F0} | {3:F0} | {4} |`n" -f `
            $scenario, $alloc, $heap, $ws, $gc
    }
}

# Top memory consumers
$report += "`n## Top Memory Consumers (by Allocated Bytes)`n`n"
$report += "| Rank | Scenario | Allocated (bytes) |`n"
$report += "|------|----------|-------------------|`n"

$topConsumers = $profiles |
    Group-Object -Property scenario |
    Select-Object -Property Name, @{Name='Average'; Expression={($_.Group | Measure-Object -Property allocated_bytes_delta -Average).Average}} |
    Sort-Object -Property Average -Descending |
    Select-Object -First 3

$rank = 1
foreach ($item in $topConsumers) {
    $report += "| {0} | {1} | {2:F0} |`n" -f $rank, $item.Name, $item.Average
    $rank++
}

# GC collection warnings
$excessiveGc = $profiles | Where-Object { $_.gc_collection_count -gt 0 }
if ($excessiveGc.Count -gt 0) {
    $report += "`n## GC Collection Warnings`n`n"
    $report += "The following scenarios triggered garbage collection:`n`n"
    foreach ($profile in $excessiveGc | Sort-Object -Property scenario -Unique) {
        $report += "- **{0}**: {1} GC collection(s)`n" -f $profile.scenario, $profile.gc_collection_count
    }
}

$report | Out-File -FilePath $ReportFile -Encoding UTF8

Write-Host $report
Write-Host "Memory profile report generated: $ReportFile" -ForegroundColor Green
exit 0
