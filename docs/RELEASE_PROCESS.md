# Release Process

This repository uses a standard tagged release flow.

## 1) Preconditions

- CI green (`CI / CI Summary`)
- Changelog updated
- Performance notes attached for perf-sensitive changes

## 2) Versioning

- Tags: `vX.Y.Z`

## 3) Steps

```powershell
git checkout master
git pull
git tag -a vX.Y.Z -m "Release vX.Y.Z"
git push origin master
git push origin vX.Y.Z
```

## 4) Release notes

- Keep release notes in `releases/`
- Include:
  - Scope summary
  - Breaking changes
  - Migration notes
  - Performance deltas

## 5) References

- Code + tests in repository
- Benchmark artifacts under `artifacts/bench/megabench_metrics/overview/`
- Changelog entries in `CHANGELOG.md`
