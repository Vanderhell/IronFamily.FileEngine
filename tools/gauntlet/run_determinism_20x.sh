#!/bin/bash
#
# Determinism Multi-Process Proof
# Runs 20 independent processes and verifies byte-for-byte identical output
#

set -u

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
FIXTURE="${REPO_ROOT}/testing/fixtures/determinism_min/input.bin"
GAUNTLET_DIR="${REPO_ROOT}/artifacts/_gauntlet/2026-02-21_determinism_multiprocess"
HARNESS_DIR="/tmp/determinism_harness_$$"

mkdir -p "$GAUNTLET_DIR" "$HARNESS_DIR"

echo "========================================="
echo "Determinism Multi-Process Proof (20x)"
echo "========================================="
echo "Repo:     $REPO_ROOT"
echo "Fixture:  $FIXTURE"
echo "Harness:  $HARNESS_DIR"
echo "Output:   $GAUNTLET_DIR"
echo

if [ ! -f "$FIXTURE" ]; then
    echo "ERROR: Fixture not found at $FIXTURE"
    exit 1
fi

# Use existing DeterminismProver executable
PROVER_EXE="$REPO_ROOT/tools/gauntlet/bin/DeterminismProver.exe"

if [ ! -f "$PROVER_EXE" ]; then
    echo "[BUILD] Compiling DeterminismProver..."
    cd "$REPO_ROOT"
    dotnet build tools/gauntlet/DeterminismProver.csproj -o "$REPO_ROOT/tools/gauntlet/bin" 2>&1 | tail -5
fi

if [ ! -f "$PROVER_EXE" ]; then
    echo "ERROR: DeterminismProver executable not found"
    exit 1
fi

echo "[READY] DeterminismProver: $PROVER_EXE"
echo

# Run 20 times
declare -a HASHES
declare -a OUTPUTS
declare -a EXITCODES
declare -a TIMESTAMPS

echo "[RUN] Starting 20 determinism runs..."
echo

for i in {1..20}; do
    RUN_DIR="$HARNESS_DIR/run_$i"
    OUTPUT_DIR="$RUN_DIR/output"

    mkdir -p "$OUTPUT_DIR"

    echo -n "Run $i... "

    # Capture timestamp
    START=$(date +%s%N)

    # Run DeterminismProver
    cd "$OUTPUT_DIR"
    "$PROVER_EXE" "$FIXTURE" "$OUTPUT_DIR" > "$RUN_DIR/stdout.txt" 2> "$RUN_DIR/stderr.txt"
    EXITCODE=$?

    END=$(date +%s%N)
    DURATION_MS=$(( (END - START) / 1000000 ))

    EXITCODES[$i]=$EXITCODE
    TIMESTAMPS[$i]=$DURATION_MS

    if [ $EXITCODE -ne 0 ]; then
        echo "FAILED (exit $EXITCODE)"
        cat "$RUN_DIR/stderr.txt"
        continue
    fi

    # Compute SHA256 for each output file
    ILOG_HASH=$(sha256sum "$OUTPUT_DIR/determinism.ilog" 2>/dev/null | awk '{print $1}')
    IRONCFG_HASH=$(sha256sum "$OUTPUT_DIR/determinism.ironcfg" 2>/dev/null | awk '{print $1}')
    IUPD_HASH=$(sha256sum "$OUTPUT_DIR/determinism.iupd" 2>/dev/null | awk '{print $1}')

    HASHES[$i]="$ILOG_HASH|$IRONCFG_HASH|$IUPD_HASH"
    OUTPUTS[$i]="$OUTPUT_DIR"

    echo "OK (${DURATION_MS}ms)"
done

echo
echo "========================================="
echo "Hash Comparison"
echo "========================================="

# Save hashes
{
    for i in {1..20}; do
        echo "Run $i: ${HASHES[$i]}"
    done
} | tee "$GAUNTLET_DIR/hashes.txt"

echo
echo "========================================="
echo "Determinism Analysis"
echo "========================================="

# Check if all hashes are identical
FIRST_HASH="${HASHES[1]}"
HASHES_MATCH=1
FIRST_DIFF=""

for i in {2..20}; do
    if [ "${HASHES[$i]}" != "$FIRST_HASH" ]; then
        HASHES_MATCH=0
        FIRST_DIFF="$i"
        break
    fi
done

{
    echo "# Determinism Proof Summary"
    echo
    echo "## Test Configuration"
    echo "- Fixture: $FIXTURE"
    echo "- Fixture size: $(stat -f%z "$FIXTURE" 2>/dev/null || stat -c%s "$FIXTURE") bytes"
    echo "- Runs: 20"
    echo "- Engines: ILOG, IRONCFG, IUPD"
    echo

    if [ $HASHES_MATCH -eq 1 ]; then
        echo "## Result: ✓ PASS"
        echo
        echo "All 20 runs produced **byte-for-byte identical** output:"
        echo
        echo "- ILOG hash (all 20):     ${HASHES[1]%|*}"
        echo "- IRONCFG hash (all 20):  $(echo "${HASHES[1]}" | cut -d'|' -f2)"
        echo "- IUPD hash (all 20):     $(echo "${HASHES[1]}" | cut -d'|' -f3)"
        echo
        echo "## Conclusion"
        echo "**Determinism verified.** All three engines produce identical output across independent process executions."
    else
        echo "## Result: ✗ FAIL"
        echo
        echo "Hash mismatch detected!"
        echo "- Expected: $FIRST_HASH"
        echo "- Got (run $FIRST_DIFF): ${HASHES[$FIRST_DIFF]}"
        echo
        echo "## Root Cause Investigation Required"
        echo "See PHASE3_root_cause.md for binary diff analysis."
    fi

    echo
    echo "## Timing"
    for i in {1..5}; do
        echo "- Run $i: ${TIMESTAMPS[$i]}ms"
    done
    echo "- ..."
    echo
    echo "## Log Files"
    echo "See \`artifacts/_gauntlet/2026-02-21_determinism_multiprocess/\` for:"
    echo "- \`hashes.txt\`: All SHA256 hashes"
    echo "- \`run_*/stdout.txt\`: Process output"
    echo "- \`run_*/stderr.txt\`: Error messages"

} | tee "$GAUNTLET_DIR/PHASE2_summary.md"

# Cleanup
echo
echo "========================================="
echo "Cleanup"
echo "========================================="
echo "Harness directory: $HARNESS_DIR"
echo "Keeping for debugging. To cleanup: rm -rf $HARNESS_DIR"
echo

if [ $HASHES_MATCH -eq 0 ]; then
    echo "STATUS: FAIL - Determinism proof failed"
    exit 1
else
    echo "STATUS: PASS - Determinism proof successful"
    exit 0
fi
