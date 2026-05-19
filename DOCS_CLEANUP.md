# Docs cleanup / inventúra (.md)

Pointa: nájsť všetky súbory dokumentácie a upratať ich tak, aby dokumentácia bola v `docs/` (okrem “repo meta” súborov, ktoré GitHub očakáva v roote / `.github/`).

## Pravidlá

- Repo meta (ponechať mimo `docs/`):
  - `README.md`, `CHANGELOG.md`, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`
  - `.github/pull_request_template.md`
- Dokumentácia (patrí do `docs/`):
  - špecifikácie, návody, metodiky, release poznámky, indexy
- Generované artefakty:
  - nepresúvať “naslepo”; skôr ignorovať cez `.gitignore` (`**/artifacts/`, `**/bin/`, `**/obj/`)

## Stav (po uprataní)

Zavedená štruktúra:

- engine docs: `docs/engines/<engine>/...`
- libs docs: `docs/libs/...`
- tools docs: `docs/tools/...`
- native docs: `docs/native/...`
- releases: `docs/releases/...`
- attribution: `docs/attribution/...`
- vector manifesty: `docs/vectors/...`

Na pôvodných miestach sú namiesto `.md` krátke `.txt` “pointer” súbory.

