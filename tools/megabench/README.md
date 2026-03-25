# MegaBench

`megabench` je interný benchmark harness pre IronFamily formáty a súvisiace porovnania.

## ICFG Layers Bench

Príkaz `bench-icfg-layers` porovnáva:

- `full` pipeline
- `open`
- `fast`
- `strict`
- `read`
- odvodené `sum(strict) = open + strict + read`
- odvodené `sum(fast) = fast + read`

Použitie:

```powershell
dotnet run -c Release --project tools/megabench/MegaBench.csproj -- bench-icfg-layers --dataset 10KB
dotnet run -c Release --project tools/megabench/MegaBench.csproj -- bench-icfg-layers --dataset 10KB --read-mode payload
dotnet run -c Release --project tools/megabench/MegaBench.csproj -- bench-icfg-layers --dataset 10KB --cold
dotnet run -c Release --project tools/megabench/MegaBench.csproj -- bench-icfg-layers --dataset 1MB
```

Podporované `--read-mode`:

- `string`
- `scalar`
- `nested`
- `payload`
- `full`

Ak nie je zadané `--output`, výsledky sa zapisujú do:

`artifacts/bench/megabench_metrics/icfg_full_vs_layers/`

Runner zapisuje:

- per-run JSON
- `icfg_full_vs_layers_summary.csv`
- `icfg_full_vs_layers_summary.md`
- `icfg_full_vs_layers_verdict.md`

## Overview + Ranking

Prikaz `bench-overview` vytvori jednotny report pre:

- `encode`
- `strict`
- `decode`
- `verify`
- `apply_after_strict`
- `encoded bytes`

Pouzitie:

```powershell
dotnet run -c Release --project tools/megabench/MegaBench.csproj -- bench-overview --datasets 100KB,1MB --label current
dotnet run -c Release --project tools/megabench/MegaBench.csproj -- bench-overview --datasets 100KB,1MB --label current --cleanup-old
```

Source of truth pre overview artefakty je:

`artifacts/bench/megabench_metrics/overview/`

Runner zapisuje:

- `overview_<label>.json|md|csv`
- `overview_<label>_ranking.md|csv`
- canonical symlink-like kopie: `overview_latest.*` a `overview_latest_ranking.*`

## Poznámky

- `--cold` resetuje strict metadata cache aj reader cache pred každým meraním.
- Datasety `1KB`, `10KB`, `100KB` a `1MB` sú generované do root `artifacts/bench/megabench_datasets/`.
- Po oprave dataset generatora už benchmark škáluje s reálnou veľkosťou payloadu, nie s malým fixným objektom.
