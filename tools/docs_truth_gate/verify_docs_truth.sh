#!/bin/bash
#
# Docs Truth Gate Verification
# Purpose: Enforce 100% verifiability of normative claims in documentation
# Exit 0 if all normative claims have evidence, exit 1 otherwise
#

DOCS_DIR="${1:-.}"

echo "================================"
echo "Docs Truth Gate Verification"
echo "================================"
echo "Scanning: $DOCS_DIR for normative claims..."
echo

# Create temp file for unverified claims
UNVERIFIED=$(mktemp)
trap "rm -f $UNVERIFIED" EXIT

# Find all lines with normative keywords in markdown files
grep -rn " MUST \| SHOULD \| NEVER \| ALWAYS \| REQUIRED \| SHALL " "$DOCS_DIR"/*.md 2>/dev/null | while read -r match; do
    file=$(echo "$match" | cut -d: -f1)
    line_num=$(echo "$match" | cut -d: -f2)
    content=$(echo "$match" | cut -d: -f3-)

    # Check if line contains evidence reference
    if ! echo "$content" | grep -qE "(IlogTests|ILogTests|IronCfgTests|IupdTests|SpecLockTests|PhaseTests|RuntimeVerify|EdgeError|vectors|test_vectors|\`|Tests|\.md|\.cs)"; then
        echo "$file:$line_num:$content" >> "$UNVERIFIED"
        echo "âś— $file:$line_num - UNVERIFIED"
        echo "  $content"
    else
        echo "âś“ $file:$line_num"
    fi
done

echo
echo "================================"

# Check if there are unverified claims
if [ -s "$UNVERIFIED" ]; then
    echo "Status: FAIL - Found unverified normative claims"
    echo
    echo "All MUST/SHOULD/NEVER/ALWAYS/REQUIRED/SHALL statements must include"
    echo "evidence references (test names, test vectors, or code files)."
    echo "================================"
    exit 1
else
    echo "Status: PASS - All normative claims are verifiable"
    echo "================================"
    exit 0
fi
