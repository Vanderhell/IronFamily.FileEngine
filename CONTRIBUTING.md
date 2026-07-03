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
dotnet restore libs/ironconfig-dotnet/IronConfig.sln
dotnet build -c Release libs/ironconfig-dotnet/IronConfig.sln
dotnet test -c Release libs/ironconfig-dotnet/IronConfig.sln
```

If native components are affected:

```powershell
cmake -S native -B native/build
cmake --build native/build --config Release
ctest --test-dir native/build -C Release --output-on-failure
```

Use the opt-in hardening builds when touching parser, bounds, or security-sensitive native code:

```powershell
cmake -S native -B native/build-strict -DIRONFAMILY_STRICT_WARNINGS=ON
cmake --build native/build-strict --config Release

cmake -S native -B native/build-asan -DIRONFAMILY_ENABLE_SANITIZERS=ON
cmake --build native/build-asan --config Release
ctest --test-dir native/build-asan -C Release --output-on-failure
```

If documentation is affected:

```powershell
bash tools/docs_truth_gate/verify_docs_truth.sh .
git diff --check
```

## Performance-sensitive areas

- Validate with `tools/megabench` when touching hot paths.
- Include before/after numbers in PR description for perf-related changes when you actually ran the benchmarks.

## Documentation

- Keep public documentation free of temporary notes, short-lived baselines, and non-public workflow details.
- Document externally useful behavior, constraints, and supported workflows only.
