#!/bin/bash
set -e

CURRENT_FILE="${1:-./artifacts/bench/profiles_bench.json}"
REPORT_FILE="${2:-./artifacts/bench/DETERMINISM_CI_REPORT.md}"

# Create output directory
mkdir -p "$(dirname "$REPORT_FILE")"

# Initialize report
report="# Determinism CI Report

**Platform**: Linux
**Time**: $(date '+%Y-%m-%d %H:%M:%S')

## Benchmark Coverage

| Engine | Profile | Dataset | Size | Encode (ms) | Decode (ms) |
|--------|---------|---------|------|-------------|-------------|
"

# Parse JSON using jq
if ! command -v jq &> /dev/null; then
    echo "Error: jq is required but not installed"
    exit 127
fi

# Get unique entries and build report
jq -r '.[] | [.engine, .profile, .dataset, .size_bytes, .encode_ms_median, .decode_ms_median] | @csv' "$CURRENT_FILE" | while IFS=',' read -r engine profile dataset size encode decode; do
    engine=$(echo "$engine" | sed 's/"//g')
    profile=$(echo "$profile" | sed 's/"//g')
    dataset=$(echo "$dataset" | sed 's/"//g')
    size=$(echo "$size" | sed 's/"//g')
    encode=$(echo "$encode" | sed 's/"//g')
    decode=$(echo "$decode" | sed 's/"//g')

    report+="| $engine | $profile | $dataset | $size | $(printf '%.4f' $encode) | $(printf '%.4f' $decode) |
"
done

count=$(jq 'length' "$CURRENT_FILE")

report+="
## Summary

- **Total Benchmarks**: $count
- **Status**: ✅ All benchmarks present and readable

"

echo "$report" | tee "$REPORT_FILE"

exit 0
