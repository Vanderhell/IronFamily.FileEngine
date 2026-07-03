# ironconfig-dotnet

`libs/ironconfig-dotnet` contains the managed reference implementation, tests, and host tooling for the active repository scope.

## Build

```powershell
dotnet restore libs/ironconfig-dotnet/IronConfig.sln
dotnet build libs/ironconfig-dotnet/IronConfig.sln -c Release --no-restore
dotnet test libs/ironconfig-dotnet/IronConfig.sln -c Release --no-build
```

## Scope notes

- The managed codebase is still part of the maintained product surface.
- It is used for reference behavior, tooling, vectors, and parity support.
- Its existence does not imply native parity unless a specific gate proves it.
