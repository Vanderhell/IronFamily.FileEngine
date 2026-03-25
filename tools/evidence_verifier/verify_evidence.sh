#!/bin/bash

# Evidence Matrix Validator - Runs AST-based verification of all anchors.
# Calls the Roslyn-based EvidenceVerifier to validate code/test/CI anchors.
# Optionally validates numeric claim values against loaded assemblies (--reflect).
# Exit codes: 0 = pass, 2 = fail

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VERIFIER_PROJECT="$SCRIPT_DIR/EvidenceVerifier"
MATRIX_PATH="docs/sct/EVIDENCE_MATRIX.md"

# Build args array - enforce strict mode with expected claim count and reflection checks
args=("$MATRIX_PATH" "--expect-claims" "47" "--reflect")

# Run AST verifier
echo "=== Running AST-Based Evidence Verifier ==="
echo "Project: $VERIFIER_PROJECT"
echo ""

if dotnet run --project "$VERIFIER_PROJECT" -c Release -- "${args[@]}"; then
    echo ""
    echo "✅ VERIFICATION PASSED"
    exit 0
else
    echo ""
    echo "❌ VERIFICATION FAILED"
    exit 2
fi
