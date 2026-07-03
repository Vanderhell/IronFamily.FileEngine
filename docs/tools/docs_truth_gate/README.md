# Docs Truth Gate

Purpose: reject repository documentation that contains invalid local paths, removed references, forbidden session-specific language, absolute local paths, stale test-count claims, or misleading active-scope wording.

## Usage

```bash
bash tools/docs_truth_gate/verify_docs_truth.sh [repo_root]
```

The CI workflow runs it from the repository root.

## CI Integration

The gate is wired into `.github/workflows/ci.yml`.
