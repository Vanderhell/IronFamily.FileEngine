#!/usr/bin/env python3
"""
Determinism test for ICFX:
Verify that JSON â†’ packx â†’ tojson â†’ packx produces identical bytes
"""

import subprocess
import os
import sys
import hashlib

def run_cmd(cmd):
    """Run a command and return output"""
    result = subprocess.run(cmd, shell=True, capture_output=True, text=True)
    if result.returncode != 0:
        print(f"Command failed: {cmd}")
        print(f"Error: {result.stderr}")
        sys.exit(1)
    return result.stdout

def file_hash(filepath):
    """Compute SHA256 hash of a file"""
    sha256 = hashlib.sha256()
    with open(filepath, 'rb') as f:
        sha256.update(f.read())
    return sha256.hexdigest()

def test_determinism():
    """Test determinism: JSON â†’ packx â†’ tojson â†’ packx should produce identical bytes"""

    basedir = "C:\\Users\\vande\\Desktop\\Bjv\\bjv"
    cli = os.path.join(basedir, "tools\\ironconfigtool\\bin\\Debug\\net8.0\\ironconfigtool.exe")

    golden_json = os.path.join(basedir, "vectors\\icfx\\golden_config.json")
    temp_dir = os.path.join(basedir, "_icfx_impl")

    # Step 1: Encode golden JSON to ICFX (first time)
    print("Step 1: Encoding golden_config.json to ICFX (first pass)...")
    icfx1 = os.path.join(temp_dir, "roundtrip_pass1.icfx")
    run_cmd(f'"{cli}" packx "{golden_json}" "{icfx1}"')
    hash1 = file_hash(icfx1)
    print(f"  First encoding hash: {hash1[:16]}...")

    # Step 2: Decode to JSON
    print("Step 2: Decoding ICFX back to JSON...")
    json2 = os.path.join(temp_dir, "roundtrip_pass2.json")
    with open(json2, 'w', encoding='utf-8') as f:
        output = run_cmd(f'"{cli}" tojson "{icfx1}"')
        # Extract JSON from output (skip "IronConfig Engine..." line)
        lines = output.split('\n')
        # Find the first line that starts with { and capture all JSON
        json_start = None
        for i, line in enumerate(lines):
            if line.startswith('{'):
                json_start = i
                break

        if json_start is None:
            print("Error: Could not find JSON in output")
            print(f"Output: {output[:500]}")
            sys.exit(1)

        # Join all remaining lines from json_start onwards
        json_content = '\n'.join(lines[json_start:]).strip()
        f.write(json_content)

    # Step 3: Encode the decoded JSON back to ICFX (second time)
    print("Step 3: Re-encoding decoded JSON to ICFX (second pass)...")
    icfx2 = os.path.join(temp_dir, "roundtrip_pass2.icfx")
    run_cmd(f'"{cli}" packx "{json2}" "{icfx2}"')
    hash2 = file_hash(icfx2)
    print(f"  Second encoding hash: {hash2[:16]}...")

    # Step 4: Compare
    print("\nStep 4: Comparing binary files...")
    if hash1 == hash2:
        print("[OK] DETERMINISM TEST PASSED")
        print(f"  Both encodings match: {hash1[:32]}...")

        # Verify sizes match
        size1 = os.path.getsize(icfx1)
        size2 = os.path.getsize(icfx2)
        print(f"  File sizes: {size1} == {size2}")

        # Do byte-by-byte comparison as final check
        with open(icfx1, 'rb') as f:
            bytes1 = f.read()
        with open(icfx2, 'rb') as f:
            bytes2 = f.read()

        if bytes1 == bytes2:
            print("[OK] BYTE-FOR-BYTE COMPARISON PASSED")
        else:
            print("[FAIL] BYTE MISMATCH (even though hashes match?)")
            sys.exit(1)

        return True
    else:
        print("[FAIL] DETERMINISM TEST FAILED")
        print(f"  First pass:  {hash1}")
        print(f"  Second pass: {hash2}")

        # Show where they differ
        with open(icfx1, 'rb') as f:
            bytes1 = f.read()
        with open(icfx2, 'rb') as f:
            bytes2 = f.read()

        min_len = min(len(bytes1), len(bytes2))
        for i in range(min_len):
            if bytes1[i] != bytes2[i]:
                print(f"  First difference at byte {i}: {bytes1[i]:02x} vs {bytes2[i]:02x}")
                break

        if len(bytes1) != len(bytes2):
            print(f"  Size mismatch: {len(bytes1)} vs {len(bytes2)}")

        return False

if __name__ == '__main__':
    success = test_determinism()
    sys.exit(0 if success else 1)
