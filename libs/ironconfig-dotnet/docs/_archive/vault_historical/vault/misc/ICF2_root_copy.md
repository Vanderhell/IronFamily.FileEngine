> **ARCHIVED DOCUMENT**
> Not verified by truth audit. Historical reference only.

# ICF2 (IRONCFG Columnar Format v2) — Normatívna špecifikácia

**Status:** DRAFT v1 (normatívne)  
**Účel:** Unity/DOTS/IL2CPP asset & tabuľkové dáta (read + write), extrémne rýchle čítanie, stabilný random access, malé dáta.  
**Kľúčové vlastnosti:** columnar layout, zero-copy view, deterministické bytes, adaptívne encodingy per-column (deterministicky), povinná certifikácia cez `ironcert`.

> **Normatívnosť:** Tento dokument je *jediná autorita* pre formát. Všetky implementácie (.NET/C) musia byť kompatibilné s týmito pravidlami.

---

## 0) Terminológia

- **Row**: jeden záznam (riadok) tabuľky.
- **Column**: jedno pole (stĺpec), uložené ako samostatný dátový blok.
- **ColMeta**: metadáta jedného stĺpca (typ, encoding, offsety, indexy).
- **Encoding**: spôsob uloženia hodnôt v stĺpci (Fixed, Bitpacked, Varint…).
- **Checkpoint index**: tabuľka offsetov do dátového bloku pre stabilný random access pri varlen encodingoch.
- **Core**: ICF2 bez kompresie; kompresia je iba wrapper mimo Core.

---

## 1) Súborové vrstvy

### 1.1 Core ICF2 (povinné)
Súbor s magic `ICF2` predstavuje **Core** formát.

### 1.2 Wrapper vrstvy (voliteľné, mimo Core)
Rodina môže definovať kontajnery nad Core, napr:
- **ICF2Z**: kompresný wrapper (LZ4 / Zstd)
- **ICF2S**: signed wrapper (Ed25519) + BLAKE3

Wrapper **nesmie** meniť význam Core bajtov. Core musí byť vždy validovateľné samostatne (po dekompresii / overení podpisu).

---

## 2) Základné pravidlá (endianness, alignment)

- **Endianness:** Little-endian (povinné)
- **Základné alignment:** 4B (povinné)
- Voliteľne: 8B alebo 16B alignment pre data bloky (flag + hodnota v headeri)
- Všetky offsety sú od začiatku súboru.

---

## 3) Verzionovanie a kompatibilita

- `version_major`:
  - zmena binárneho layoutu alebo významu polí = **increment major**
- `version_minor`:
  - doplnenie kompatibilných prvkov (nové encoding enumy, nové voliteľné bloky) = **increment minor**
- Implementácia:
  - **strict** režim: unknown `version_major` = fail
  - `version_minor` môže byť akceptovaný, ak implementácia pozná všetky použité feature flags/encodingy; inak fail.

---

## 4) File layout (vysoká úroveň)

Súbor je sekvenčný, ale adresovaný cez offsety:

1. Header (fixná časť)
2. (Voliteľné) Header extensions (TLV)
3. Schema block
4. Column metadata block (ColMeta array)
5. Data block region (column data blocks)
6. Index block region (checkpointy, PK/SK indexy)
7. String pool region (string tables + blob)
8. Trailer (CRC32, voliteľne BLAKE3)

---

## 5) Header (normatívny layout)

### 5.1 Fixná hlavička (min 64 B)
Všetky polia little-endian.

| Offset | Veľkosť | Názov | Typ | Popis |
|---:|---:|---|---|---|
| 0  | 4  | magic | u8[4] | ASCII `ICF2` |
| 4  | 1  | version_major | u8 | major |
| 5  | 1  | version_minor | u8 | minor |
| 6  | 2  | flags | u16 | Feature flags (nižšie) |
| 8  | 2  | header_size | u16 | Celková veľkosť headeru (min 64) |
| 10 | 1  | endian | u8 | musí byť `1` (little) |
| 11 | 1  | alignment | u8 | 4/8/16 (default 4) |
| 12 | 4  | rows | u32 | počet riadkov |
| 16 | 2  | cols | u16 | počet stĺpcov |
| 18 | 2  | reserved0 | u16 | 0 |
| 20 | 8  | schema_off | u64 | offset schema bloku |
| 28 | 8  | colmeta_off | u64 | offset colmeta bloku |
| 36 | 8  | data_off | u64 | offset začiatku data regiónu |
| 44 | 8  | index_off | u64 | offset index regiónu |
| 52 | 8  | string_off | u64 | offset string regiónu |
| 60 | 4  | trailer_off | u32 | offset traileru (CRC) |
| 64 | ... | header_ext | TLV | voliteľné, len ak header_size > 64 |

**Poznámky:**
- `header_size` umožní kompatibilné rozšírenia (TLV).
- Ak je implementácia neznalá TLV typu, **strict validate** musí zlyhať, ak je príznak `F_STRICT_EXT` nastavený.

### 5.2 Header flags (u16)
Bitové príznaky:

- `F_HAS_CRC32` (bit 0): Core obsahuje CRC32 v traileri (povinné pre family baseline)
- `F_HAS_BLAKE3` (bit 1): Core obsahuje BLAKE3(32B) v traileri
- `F_ALIGN_8` (bit 2): alignment=8 (len pre kontrolu konzistencie)
- `F_ALIGN_16` (bit 3): alignment=16
- `F_STRICT_EXT` (bit 4): unknown TLV v header_ext => fail
- `F_SCHEMA_REQUIRED` (bit 5): schema block musí existovať a byť validný
- `F_STRINGS_REQUIRED` (bit 6): string blok musí existovať (ak sú použité string typy, toto MUSÍ byť 1)
- `F_RESERVED` (ostatné): musia byť 0 v v1; strict validate: unknown flag => fail

---

## 6) Schema block (povinné pre Unity engine)

ICF2 je tabuľkový formát; schéma je súčasťou súboru kvôli verziám a reprodukovateľnosti.

### 6.1 Schema header
| Pole | Typ | Popis |
|---|---|---|
| schema_magic | u8[4] | ASCII `SCHM` |
| schema_version | u32 | verzia schémy (nie formátu) |
| schema_id | u64 | deterministický hash logical schémy (napr. SHA-256 truncated) |
| table_name_sid | u32 | stringId názvu tabuľky |
| column_count | u16 | musí sa rovnať header.cols |
| reserved | u16 | 0 |

### 6.2 Schema columns (pole dĺžky `column_count`)
Každá položka:

| Pole | Typ | Popis |
|---|---|---|
| col_name_sid | u32 | stringId názvu stĺpca |
| col_type | u16 | ColumnType enum |
| nullable | u8 | 0/1 |
| semantics | u8 | Semantics enum (hint) |
| default_kind | u8 | 0=none, 1=scalar, 2=stringSid |
| reserved | u8 | 0 |
| default_u64 | u64 | podľa default_kind / typu |
| constraints | u32 | bitmask (napr. unique, range-check enabled) |

**Poznámka:** Semantics sú hint pre pipeline, nie význam formátu. Nepoužívajú sa na dekódovanie.

---

## 7) Column metadata block (ColMeta)

### 7.1 ColMeta header
| Pole | Typ | Popis |
|---|---|---|
| meta_magic | u8[4] | ASCII `META` |
| meta_version | u16 | 1 |
| col_count | u16 | musí sa rovnať header.cols |
| reserved | u32 | 0 |

### 7.2 ColMeta entry (opakované `col_count` krát)
| Pole | Typ | Popis |
|---|---|---|
| col_id | u16 | 0..cols-1 |
| col_type | u16 | ColumnType |
| encoding | u16 | Encoding enum |
| enc_param | u16 | parameter (napr. bitsPerValue) |
| data_off | u64 | offset do data regiónu |
| data_len | u32 | dĺžka data bloku |
| aux_off | u64 | offset aux (offsets/checkpoints/…) |
| aux_len | u32 | dĺžka aux |
| nullmap_off | u64 | offset nullmap bitset (0 ak nepoužité) |
| nullmap_len | u32 | dĺžka nullmap |
| stats_off | u64 | offset stats (0 ak nepoužité) |
| stats_len | u32 | dĺžka stats |

**Normatívne:**
- `data_off + data_len` musí ležať v súbore.
- `aux` je povinné, ak encoding vyžaduje checkpointy / offsets.
- `nullmap` je povinná, ak `nullable=1` v schéme (alebo ak schema neexistuje, nullable sa považuje za false).

---

## 8) ColumnType enum (u16)

Minimálne typy v1:

- `0x0001` BOOL
- `0x0002` I32
- `0x0003` U32
- `0x0004` I64
- `0x0005` U64
- `0x0006` F32
- `0x0007` F64
- `0x0008` STRING_SID   (u32 stringId)
- `0x0009` BYTES_BLOB   (varlen bytes, aux=offsets)
- `0x000A` VEC3_F32     (3×f32, fixed)
- `0x000B` VEC4_F32     (4×f32, fixed)
- `0x000C` ENUM_U32     (u32 but semantically enum)
- `0x000D` ID_U64       (u64)

Unknown type => strict fail.

---

## 9) Encoding enum (u16)

**Povinné encodingy v1:**

- `0x0001` FIXED32      (O(1), 4B per value)
- `0x0002` FIXED64      (O(1), 8B per value)
- `0x0003` BITPACKED    (enc_param = bitsPerValue)
- `0x0004` VARINT_ZZ    (ZigZag pre signed, raw varint pre unsigned; vyžaduje checkpointy)
- `0x0005` DELTA_VARINT (first + delta varint; vyžaduje checkpointy)
- `0x0006` RLE          (run-length encoding; vyžaduje run index pre random access)
- `0x0007` STRING_POOL  (u32 stringId; FIXED32 alebo VARINT podľa colmeta)

Unknown encoding => strict fail.

---

## 10) Dátové bloky (per encoding)

### 10.1 FIXED32 / FIXED64
- `rows` hodnôt uložených tesne za sebou.
- `data_len` musí byť `rows * 4` alebo `rows * 8` (ak nullable, stále len hodnoty; nullmap rieši null).

### 10.2 BITPACKED
- `enc_param` = bitsPerValue (1..31)
- data blok: bitstream uložení `rows` hodnôt.
- `data_len` musí byť `ceil(rows * bitsPerValue / 8)` zaokrúhlené na alignment.

### 10.3 VARINT_ZZ
- data blok: varint stream (LEB128 štýl) pre `rows` hodnôt.
- **aux**: checkpoint index (povinné).
- ZigZag: i32/i64 sa zigzaguje; u32/u64 bez zigzag.

### 10.4 DELTA_VARINT
- data blok: first (varint) + deltas (varint) pre `rows-1`.
- **aux**: checkpoint index (povinné).

### 10.5 RLE
- data blok: opakované páry `(runLen varint, value)`.
- **aux**: run index (povinné): prefix-sum checkpointy.

### 10.6 BYTES_BLOB (varlen)
- data blok: concatenated bytes
- **aux**: offsets table (u32 offsets + u32 lengths) alebo varint offsets (definované v aux headeri)

---

## 11) Aux bloky (checkpointy, offsets)

### 11.1 Checkpoint index (pre VARINT/DELTA)
Aux header:

| Pole | Typ | Popis |
|---|---|---|
| aux_magic | u8[4] | ASCII `CPI1` |
| stride | u16 | K (napr. 256) |
| entry_count | u32 | ceil(rows / K) |
| reserved | u16 | 0 |
| off_unit | u16 | 4 alebo 8 (offset size) |

Následne `entry_count` offsetov do data bloku (relatívne k `data_off`).

**Random access garant:** max preskočených hodnôt je `stride-1`.

### 11.2 RLE run index
Aux magic `RIX1`, podobná štruktúra:
- stride v počte runov alebo row-range
- entry = (rowStart, dataOffset) checkpoint

### 11.3 Varlen offsets (BYTES_BLOB / STRING varlen)
Aux magic `OFF1`:
- offsets_count = rows
- u32 offsets + u32 lengths (alebo varint variant `VOF1`)

---

## 12) String pool region

### 12.1 String table header
| Pole | Typ | Popis |
|---|---|---|
| str_magic | u8[4] | ASCII `STRS` |
| count | u32 | počet stringov |
| blob_len | u32 | dĺžka blobu |
| reserved | u32 | 0 |

### 12.2 Offsets + lengths
- `count` položiek u32 offset + u32 length (offset do blobu)
- blob: UTF-8 bytes

**Normatívne:**
- bytes sú presne UTF-8; žiadna normalizácia.
- ak `F_STRINGS_REQUIRED=1`, string region musí existovať.

---

## 13) Trailer (integrita)

Ak `F_HAS_CRC32=1`:
- trailer začína u `trailer_off`
- obsahuje u32 CRC32 IEEE nad všetkým od offset 0 po `trailer_off` (t.j. bez CRC poľa).

Ak `F_HAS_BLAKE3=1`:
- za CRC32 nasleduje 32B BLAKE3 hash nad rovnakým rozsahom (bez traileru).

---

## 14) Determinismus 2.0 (povinné)

### 14.1 Float canonicalizácia
- `-0.0` musí byť uložené ako `+0.0`
- NaN: všetky NaN musia byť uložené ako jeden canonical bitpattern:
  - f32: `0x7FC00000`
  - f64: `0x7FF8000000000000`

### 14.2 Stabilné rozhodovanie encodera (auto-tuning)
Auto-tuning je povolený len ak:
- používa normatívny decision tree (sekcia 15)
- generuje rovnaké bytes pri rovnakom vstupe a rovnakých pravidlách.

### 14.3 Sorting/ordering
Ak existujú interné zoradenia (napr. string pool), musia byť:
- deterministické
- stabilné
- definované byte-wise (UTF-8 bytes), nie locale.

---

## 15) Auto-tuning decision tree (normatívne)

Encoder musí vypočítať štatistiky pre každý stĺpec:
- `min`, `max`
- `nullRatio`
- `cardinality` (unikátne hodnoty)
- `monotonic` (non-decreasing)
- `rleRatio` (odhad úspory pri RLE)
- `avgVarintLen` (odhad)

Potom zvoliť encoding podľa týchto pravidiel (v poradí):

1) Ak type == BOOL → `BITPACKED(bits=1)`
2) Ak type je ENUM_U32 alebo small int a `max < 2^b` a `b<=16` → `BITPACKED(bits=b)`
3) Ak monotonic == true a type je int/id/timestamp → `DELTA_VARINT(stride=256)`
4) Ak `rleRatio >= 0.30` → `RLE(stride=256)`
5) Inak:
   - ak `avgVarintLen < 3.5` → `VARINT_ZZ(stride=256)`
   - inak → `FIXED32` / `FIXED64` podľa typu

**Výber stride deterministicky podľa rows:**
- rows < 50k → 256
- 50k..500k → 512
- >500k → 1024

---

## 16) Validácia

### 16.1 validate_fast (povinné)
Musí overiť:
- magic, endian, header_size >= 64
- všetky offsety sú v rozsahu súboru a monotónne dávajú zmysel
- `colmeta_off` a `schema_off` nesmú ukazovať mimo súboru
- ak `F_HAS_CRC32=1`, overiť CRC

### 16.2 validate_strict (povinné)
Okrem fast musí overiť:
- schema_magic `SCHM`, column_count == cols (ak `F_SCHEMA_REQUIRED=1`)
- ColMeta block `META`, col_count == cols
- pre každý stĺpec:
  - encoding kompatibilný s typom
  - data_len sedí (pre fixed/bitpacked)
  - aux existuje a je validný (pre varint/delta/rle/varlen)
  - nullmap validná, ak nullable
- string pool validný, ak sú použité stringId
- DoS budget limity (sekcia 17)

Unknown flag/type/encoding => fail.

---

## 17) Limity a DoS politika (povinné)

Minimálne (rodina môže sprísniť):
- max rows: 100,000,000
- max cols: 4,096
- max string count: 10,000,000
- max aux size per column: 25% z data size (ochrana pred index bomb)
- max work: validate_strict max `O(file_size + cols * entry_count)`

---

## 18) Error model (rodinné mapovanie)

Implementácie musia mapovať chyby minimálne do týchto kategórií:
- INVALID_MAGIC
- UNSUPPORTED_VERSION
- UNKNOWN_FLAG
- OUT_OF_RANGE_OFFSET
- INVALID_SCHEMA
- INVALID_COLMETA
- INVALID_ENCODING
- INVALID_AUX
- INVALID_STRING_POOL
- CRC_MISMATCH
- DOS_LIMIT_EXCEEDED

---

## 19) Golden vectors & certifikácia (normatívne pre rodinu)

- Goldeny sa smú generovať len cez: `ironcert generate icf2`
- Testy a bench len cez: `ironcert icf2`
- Povinné datasety: `small/medium/large/mega`
- Povinné režimy: validate_fast + validate_strict + parity (.NET vs C)
