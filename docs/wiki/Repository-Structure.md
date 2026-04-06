# Repository Structure

Top-level layout:

- `libs/` core libraries
- `native/` native code and build assets
- `tools/` developer tooling and benchmark runners
- `vectors/` canonical test vectors
- `docs/` documentation and wiki source
- `artifacts/` generated outputs (ignored in git unless explicitly tracked)

Policy:

- Keep root clean (no ad-hoc `*.txt` / `*.cs` files).
- Keep large/noisy local assets out of git.
