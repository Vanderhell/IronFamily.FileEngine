# CI/CD

Main pipeline:

- `.github/workflows/ci.yml`
  - .NET restore/build/test
  - Native configure/build/test
  - Documentation checks

Related workflows may cover additional validation and performance checks.

Branch protection recommendation:

- Require `CI / CI Summary`
- Disallow direct pushes to protected branches
