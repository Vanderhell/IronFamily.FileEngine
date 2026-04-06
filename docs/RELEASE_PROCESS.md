# Release Process

This repository uses a standard tagged release flow.

## 1) Preconditions

- CI green (`CI / CI Summary`)
- Changelog updated
- Performance notes attached for perf-sensitive changes

## 2) Versioning

- Annotated tags use the repository release version, for example `v2.6.0`.
- Replace the example version below with the actual release you are publishing.

## 3) Steps

```powershell
git checkout master
git pull
git tag -a v2.6.0 -m "Release v2.6.0"
git push origin master
git push origin v2.6.0
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
