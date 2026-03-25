# Release Process

This repository uses an internal-first release flow.

## 1) Preconditions

- CI green (`CI / CI Summary`)
- Changelog updated
- Benchmark evidence attached for perf-sensitive changes

## 2) Versioning

- Internal milestones: `vX.Y.Z-internal`
- Stable/public-ready releases: `vX.Y.Z`

## 3) Steps

```powershell
git checkout master
git pull
git tag -a vX.Y.Z-internal -m "Internal milestone"
git push origin master
git push origin vX.Y.Z-internal
```

## 4) Release notes

- Keep release notes in `releases/`
- Include:
  - Scope summary
  - Breaking changes
  - Migration notes
  - Performance deltas

## 5) Source of truth

- Code + tests in repository
- Benchmark artifacts under `artifacts/bench/megabench_metrics/overview/`
- Changelog entries in `CHANGELOG.md`

