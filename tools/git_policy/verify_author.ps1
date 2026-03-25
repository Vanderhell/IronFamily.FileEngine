# Verify that the last 50 commits do not have "Claude" in Author or Committer
# Exit 0 if clean, exit 2 if violation found

$FoundViolation = $false
$Commits = git log --format="%an|%cn" -50 2>$null

foreach ($Line in $Commits) {
    $Author, $Committer = $Line -split '\|'

    if ($Author -like "*Claude*") {
        Write-Error "VIOLATION: Author contains 'Claude': $Author"
        $FoundViolation = $true
    }

    if ($Committer -like "*Claude*") {
        Write-Error "VIOLATION: Committer contains 'Claude': $Committer"
        $FoundViolation = $true
    }
}

if ($FoundViolation) {
    Write-Error "Git author policy check FAILED. Author/Committer must not contain 'Claude'."
    exit 2
} else {
    Write-Host "Git author policy check PASSED. All 50 commits have valid authors."
    exit 0
}
