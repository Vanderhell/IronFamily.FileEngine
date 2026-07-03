# Tooling Notes

Tool-specific prose is kept minimal in the public documentation tree.

- `tools/docs_truth_gate/verify_docs_truth.sh` checks repository documentation for invalid paths, forbidden session-specific language, absolute local paths, stale test-count claims, and removed references.
- Benchmark and ad hoc development tools remain in source control only when their code is required by builds, vectors, or tests.
