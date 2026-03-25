# Benchmarking

Primary benchmark tooling lives in `tools/megabench`.

Common command:

```powershell
dotnet run -c Release --project tools/megabench/MegaBench.csproj -- bench-overview --datasets 100KB,1MB --label current
```

Source-of-truth outputs:

- `artifacts/bench/megabench_metrics/overview/overview_<label>.json`
- `artifacts/bench/megabench_metrics/overview/overview_<label>_ranking.csv`

