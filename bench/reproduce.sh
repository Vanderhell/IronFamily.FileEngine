#!/usr/bin/env bash
# MegaBench V5 Reproduction Script - Bash
# Usage: bash reproduce.sh [--engine all|icfg|ilog|iupd] [--skip-tests]

set -e

ENGINE="all"
SKIP_TESTS=""
MODE="ci-mode"

# Parse arguments
while [[ $# -gt 0 ]]; do
  case $1 in
    --engine)
      ENGINE="$2"
      shift 2
      ;;
    --skip-tests)
      SKIP_TESTS="true"
      shift
      ;;
    *)
      echo "Unknown option: $1"
      echo "Usage: bash reproduce.sh [--engine all|icfg|ilog|iupd] [--skip-tests]"
      exit 1
      ;;
  esac
done

echo "=== MegaBench V5 Reproduction Script ==="
echo "Engine: $ENGINE"
echo "Mode: $MODE"
echo ""

# Step 1: Build
echo "[1/4] Building MegaBench..."
dotnet build -c Release
if [ $? -ne 0 ]; then
  echo "Build failed!"
  exit 1
fi
echo "✓ Build successful"
echo ""

# Step 2: Unit tests (unless skipped)
if [ -z "$SKIP_TESTS" ]; then
  echo "[2/4] Running unit tests..."
  dotnet test libs/ironconfig-dotnet/tests/IronConfig.Evidence.Tests/IronConfig.Evidence.Tests.csproj -c Release || exit 1
  dotnet test libs/ironconfig-dotnet/tests/IronConfig.IronCfgTests/IronConfig.IronCfgTests.csproj -c Release || exit 1
  dotnet test libs/ironconfig-dotnet/tests/IronConfig.ILog.Tests/IronConfig.ILog.Tests.csproj -c Release || exit 1
  dotnet test libs/ironconfig-dotnet/tests/IronConfig.Iupd.Tests/IronConfig.Iupd.Tests.csproj -c Release || exit 1
  echo "✓ All tests passed"
else
  echo "[2/4] Skipping unit tests"
fi
echo ""

# Step 3: Benchmark
echo "[3/4] Running realworld benchmark..."
echo "Command: bench-competitors-v5 --engine $ENGINE --realworld --$MODE"
echo ""

export IRONFAMILY_DETERMINISTIC=1
export DOTNET_TieredPGO=0
export COMPlus_ReadyToRun=0

dotnet run --project tools/megabench/MegaBench.csproj -c Release --no-build -- \
  bench-competitors-v5 --engine "$ENGINE" --realworld --$MODE

if [ $? -ne 0 ]; then
  echo "Benchmark failed!"
  exit 1
fi
echo "✓ Benchmark completed"
echo ""

# Step 4: Summary
echo "[4/4] Reproduction complete"
echo ""
echo "Results saved to current directory"
echo "Check README.md for interpretation guidelines"
echo ""
echo "Verify reproducibility: Compare REPRO_HASH.txt with baseline"

exit 0
