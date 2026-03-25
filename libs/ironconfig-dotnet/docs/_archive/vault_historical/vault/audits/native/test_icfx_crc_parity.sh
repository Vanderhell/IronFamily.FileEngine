#!/bin/bash

# ICFX CRC32 Parity Test: Compare C and .NET implementations on golden ICFX files

TEST_VECTORS_DIR="${1:-vectors/small/icfx}"
IRONCONFIGTOOL_EXE="${2:-tools/ironconfigtool/bin/Debug/net8.0/ironconfigtool.dll}"
CRC_DIAGNOSTIC_EXE="${3:-libs/ironcfg-c/build/Release/crc_diagnostic.exe}"

echo "ICFX CRC32 Parity Test: C vs .NET"
echo "====================================================="

PASS_COUNT=0
FAIL_COUNT=0

# Files to test
for file in "golden_icfx_crc.icfx" "golden_icfx_crc_index.icfx"; do
    filepath="${TEST_VECTORS_DIR}/${file}"

    if [ ! -f "$filepath" ]; then
        echo "SKIP: File not found: $filepath"
        continue
    fi

    echo ""
    echo "Testing: $file"
    echo "---"

    # Get .NET CRC output
    echo -n "Running .NET (dotnet) CRC check..."
    dotnet_output=$(dotnet run --project "$IRONCONFIGTOOL_EXE" -- printcrc "$filepath" 2>/dev/null)

    dotnet_crc=$(echo "$dotnet_output" | grep "Computed CRC:" | grep -o "0x[0-9A-F]*")
    dotnet_stored=$(echo "$dotnet_output" | grep "Stored CRC:" | grep -o "0x[0-9A-F]*")

    echo " CRC=${dotnet_crc}"

    # Get C CRC output
    echo -n "Running C (crc_diagnostic) CRC check..."
    c_output=$("$CRC_DIAGNOSTIC_EXE" "$filepath")

    c_crc=$(echo "$c_output" | grep "Computed CRC:" | grep -o "0x[0-9A-F]*")
    c_stored=$(echo "$c_output" | grep "Stored CRC:" | grep -o "0x[0-9A-F]*")

    echo " CRC=${c_crc}"

    # Compare results
    echo ""
    echo "Comparison:"
    echo "  .NET Computed: $dotnet_crc"
    echo "  C Computed:    $c_crc"
    echo "  Stored:        $dotnet_stored"

    if [ "$dotnet_crc" = "$c_crc" ] && [ "$dotnet_crc" = "$dotnet_stored" ]; then
        echo "  Status: PASS âś“ (CRC parity achieved)"
        PASS_COUNT=$((PASS_COUNT + 1))
    else
        echo "  Status: FAIL âś— (CRC mismatch)"
        if [ "$dotnet_crc" != "$c_crc" ]; then
            echo "    -> .NET and C differ: $dotnet_crc vs $c_crc"
        fi
        if [ "$dotnet_crc" != "$dotnet_stored" ]; then
            echo "    -> Computed != Stored: $dotnet_crc vs $dotnet_stored"
        fi
        FAIL_COUNT=$((FAIL_COUNT + 1))
    fi
done

echo ""
echo "====================================================="
echo "Results: $PASS_COUNT passed, $FAIL_COUNT failed"
echo "====================================================="

exit $FAIL_COUNT
