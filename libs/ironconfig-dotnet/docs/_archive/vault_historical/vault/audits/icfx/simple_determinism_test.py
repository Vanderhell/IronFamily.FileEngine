#!/usr/bin/env python3
"""
Simple determinism test: Encoding the same JSON multiple times should produce identical output
"""

import subprocess
import os
import sys
import hashlib

def file_hash(filepath):
    """Compute SHA256 hash of a file"""
    sha256 = hashlib.sha256()
    with open(filepath, 'rb') as f:
        sha256.update(f.read())
    return sha256.hexdigest()

def run_cmd(cmd):
    """Run a command and return output"""
    result = subprocess.run(cmd, shell=True, capture_output=True, text=True)
    if result.returncode != 0:
        print(f"Command failed: {cmd}")
        print(f"Error: {result.stderr}")
        sys.exit(1)
    return result.stdout

basedir = "C:\\Users\\vande\\Desktop\\Bjv\\bjv"
cli = os.path.join(basedir, "tools\\ironconfigtool\\bin\\Debug\\net8.0\\ironconfigtool.exe")
golden_json = os.path.join(basedir, "vectors\\icfx\\golden_config.json")
temp_dir = os.path.join(basedir, "_icfx_impl")

# Encode the same JSON three times
hashes = []
for i in range(1, 4):
    output_file = os.path.join(temp_dir, f"determinism_pass{i}.icfx")
    print(f"Pass {i}: Encoding golden_config.json...")
    run_cmd(f'"{cli}" packx "{golden_json}" "{output_file}"')
    h = file_hash(output_file)
    hashes.append(h)
    print(f"  Hash: {h[:16]}...")

# Check if all hashes are identical
if len(set(hashes)) == 1:
    print("\n[OK] DETERMINISM TEST PASSED - All 3 encodings are identical")
    print(f"  Hash: {hashes[0]}")
    sys.exit(0)
else:
    print("\n[FAIL] DETERMINISM TEST FAILED - Encodings differ")
    for i, h in enumerate(hashes, 1):
        print(f"  Pass {i}: {h}")
    sys.exit(1)
