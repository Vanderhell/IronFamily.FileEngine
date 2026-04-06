# Contributing

## Branching and commits

- Use short, focused branches.
- Keep commits atomic and reviewable.
- Prefer conventional commit style (`feat:`, `fix:`, `chore:`, `perf:`).

## Code changes

- Keep behavior changes covered by tests.
- Do not mix unrelated refactors with functional fixes.
- Preserve existing architecture and naming unless migration is planned.

## Validation before PR

```powershell
dotnet build -c Release libs/ironconfig-dotnet/IronConfig.sln
dotnet test  -c Release libs/ironconfig-dotnet/IronConfig.sln
```

If native components are affected:

```powershell
cmake -S native -B native/build
cmake --build native/build --config Release
ctest --test-dir native/build -C Release --output-on-failure
```

## Performance-sensitive areas

- Validate with `tools/megabench` when touching hot paths.
- Include before/after numbers in PR description for perf-related changes.

## Documentation

- Keep public documentation free of temporary notes, short-lived baselines, and non-public workflow details.
- Document externally useful behavior, constraints, and supported workflows only.
