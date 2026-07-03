#!/bin/bash

set -euo pipefail

REPO_ROOT="${1:-.}"

python3 - "$REPO_ROOT" <<'PY'
import pathlib
import re
import subprocess
import sys

repo = pathlib.Path(sys.argv[1]).resolve()

tracked = subprocess.run(
    ["git", "ls-files"],
    cwd=repo,
    check=True,
    capture_output=True,
    text=True,
).stdout.splitlines()

doc_files = []
for rel in tracked:
    if not rel.endswith(".md"):
        continue
    path = repo / rel
    if path.exists():
        doc_files.append(path)

session_patterns = [
    r"\bexecuted now\b",
    r"\bin this session\b",
    r"\bin this task\b",
    r"\bin this workspace\b",
    r"\bfreshly executed\b",
    r"\bnot confirmed now\b",
]
absolute_path_patterns = [
    r"[A-Za-z]:\\",
    r"/Users/",
    r"/home/",
]
stale_count_pattern = re.compile(r"\b\d+\s*/\s*\d+\b|\b\d+\s+passed\b", re.IGNORECASE)
removed_reference_fragments = [
    "ENGINE_TRUTH_SUMMARY.md",
    "docs/wiki/",
    "BENCHMARK_REPORT_2026",
    "PERFORMANCE_REPORT_2026",
    "IMPLEMENTATION_TODO",
    "v0.1.0-internal.md",
]
unsupported_active_patterns = [
    re.compile(r"\b(active|supported|primary|current)\b.{0,40}\b(BJX|ICF2|ICFX|ICXS)\b", re.IGNORECASE),
    re.compile(r"\b(BJX|ICF2|ICFX|ICXS)\b.{0,40}\b(active|supported|primary|current)\b", re.IGNORECASE),
]

path_token = re.compile(r"`([^`\n]+)`")

errors = []

def record(path: pathlib.Path, line_no: int, message: str) -> None:
    rel = path.relative_to(repo).as_posix()
    errors.append(f"{rel}:{line_no}: {message}")

for path in doc_files:
    text = path.read_text(encoding="utf-8", errors="replace")
    lines = text.splitlines()

    for idx, line in enumerate(lines, start=1):
        for pattern in session_patterns:
            if re.search(pattern, line, re.IGNORECASE):
                record(path, idx, f"forbidden session-specific phrase: {line.strip()}")

        for pattern in absolute_path_patterns:
            if re.search(pattern, line):
                record(path, idx, f"absolute local path: {line.strip()}")

        if stale_count_pattern.search(line):
            record(path, idx, f"stale or session-bound test-count claim: {line.strip()}")

        for fragment in removed_reference_fragments:
            if fragment in line:
                record(path, idx, f"reference to removed or forbidden document: {line.strip()}")

        for pattern in unsupported_active_patterns:
            if pattern.search(line):
                record(path, idx, f"unsupported engine presented as active: {line.strip()}")

        for token in path_token.findall(line):
            cleaned = token.strip()
            if " " in cleaned:
                continue
            if not ("/" in cleaned or "\\" in cleaned or cleaned.startswith("./") or cleaned.startswith("../") or cleaned.startswith("/")):
                continue
            if cleaned.startswith(("http://", "https://", "#")):
                continue
            candidate = repo / cleaned.lstrip("/") if cleaned.startswith("/") else repo / cleaned
            if candidate.exists() or (path.parent / cleaned).exists():
                continue
            record(path, idx, f"referenced path does not exist: {cleaned}")

if errors:
    print("Docs Truth Gate FAILED")
    for item in errors:
        print(item)
    sys.exit(1)

print("Docs Truth Gate PASSED")
PY
