# Release Process

This repository uses an annotated tag release flow.

## 1. Preconditions

- CI green (`CI / CI Summary`)
- Changelog updated
- Documentation updated when public behavior changed

## 2. Versioning

Inspect existing tags before choosing a new version:

```powershell
git tag --list --sort=version:refname
```

- Existing tags already present in this repository include `v0.1.0-internal` and `v0.1.1`.
- Use annotated tags for published releases.

## 3. Steps

```powershell
git checkout master
git pull
git tag -a vX.Y.Z -m "Release vX.Y.Z"
git push origin master
git push origin vX.Y.Z
```

## 4. Release notes

- Keep release notes in `releases/`
- Include:
  - scope summary
  - breaking changes
  - migration notes when needed

## 5. References

- Code and tests in the repository
- Changelog entries in `CHANGELOG.md`
