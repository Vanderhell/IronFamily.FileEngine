# CI/CD

Main pipeline:

- `.github/workflows/ci.yml`
  - .NET restore/build/test
  - Native configure/build/test
  - Documentation truth gate

Related workflows exist for determinism, evidence, incremental gates, and performance gates.

Branch protection recommendation:

- Require `CI / CI Summary`
- Disallow direct pushes to protected branches

