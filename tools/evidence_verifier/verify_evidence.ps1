#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Evidence Matrix Validator - Runs AST-based verification of all anchors.

.DESCRIPTION
    Calls the Roslyn-based EvidenceVerifier to validate:
    1. Code anchors exist as symbols in source code (via AST parsing)
    2. Test anchors match format requirements
    3. CI anchors are valid references

    Optionally validates numeric claim values against loaded assemblies (--reflect).

.EXIT CODES
    0 = All validations passed
    2 = Invalid format or missing file/symbol
#>

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $PSScriptRoot
$verifierProject = Join-Path $scriptDir "EvidenceVerifier"
$matrixPath = "docs/sct/EVIDENCE_MATRIX.md"

# Parse arguments - enforce strict mode with expected claim count and reflection checks
$args_array = @("$matrixPath", "--expect-claims", "47", "--reflect")

# Run AST verifier
Write-Host "=== Running AST-Based Evidence Verifier ===" -ForegroundColor Cyan
Write-Host "Project: $verifierProject"
Write-Host ""

$result = & dotnet run --project "$verifierProject" -c Release -- @args_array
$exitCode = $LASTEXITCODE

if ($exitCode -eq 0) {
    Write-Host ""
    Write-Host "✅ VERIFICATION PASSED" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "❌ VERIFICATION FAILED" -ForegroundColor Red
}

exit $exitCode
