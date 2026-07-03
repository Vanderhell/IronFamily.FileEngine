# IronCfg.Core

Managed package surface for the repository's active `ICFG` implementation.

## Scope

- Targets `.NET 8.0`
- Ships the managed `ICFG` implementation from `libs/ironconfig-dotnet/src/IronConfig/`
- Lives in the same monorepo as `ILOG` and `IUPD`, but the package readme here is limited to the `ICFG` package surface

## Build

```powershell
dotnet build libs/ironconfig-dotnet/src/IronConfig/IronConfig.csproj -c Release
```

## Documentation

- Repository overview: `README.md`
- `ICFG` format reference: `docs/engines/icfg/SPEC.md`
- Repository compatibility policy: `docs/COMPATIBILITY_AND_VERSIONING.md`

## Notes

- This readme does not claim production readiness.
- Native parity is not implied by the package readme.
