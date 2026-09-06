# The RLE file format — reference and provenance

Why this document exists: an earlier revision of ADR 0004 specified `#O` for the
pattern origin and `#G` for the generation counter, asserting that other tools
ignore both. That was wrong, and it was wrong because the format was documented
from recall rather than from a source. This file records what the format
actually says and **where each claim came from**, so the next person does not
have to re-derive it — and so that any claim here can be checked.

Verified 2026-09-07.

---

## 1. Sources

| # | Source | URL | Status |
|---|--------|-----|--------|
| S1 | Golly Help: File Formats — the reference implementation's own documentation | https://golly.sourceforge.io/Help/formats.html | Fetched and read |
| S2 | `ca-formats` (Rust) RLE parser documentation | https://docs.rs/ca-formats/latest/ca_formats/rle/index.html | Fetched and read |
| S3 | NDCell documentation, RLE / Extended RLE section | https://ndcell.readthedocs.io/en/latest/formats.html | Fetched and read |
| S4 | LifeWiki, *Run Length Encoded* | https://conwaylife.com/wiki/Run_Length_Encoded | **HTTP 403 — could not be fetched.** Content below attributed to S4 comes from a search-engine summary of it and of mirrored copies of the same text, not from reading the page directly. |

**Confidence is labelled per claim.** "Verified" means it was read in a source
listed above. "Reported" means it comes only from the S4 summary and has not
been confirmed against a primary document.

---

## 2. Comment and metadata lines

A comment line is "either a blank line or a line beginning with `#`" (S1,
verified).

| Tag | Meaning | Confidence | Source |
|-----|---------|-----------|--------|
| `#C`, `#c` | Free-text comment. The only genuinely common use of `#` lines. | Reported | S4 |
| `#N` | Name of the pattern. | Reported | S4 |
| `#O` | **When and by whom the file was created.** Written by XLife. **Not an origin or position.** | Reported, and independently corroborated by the absence of any positional meaning in S1–S3 | S4 |
| `#P` | Coordinates of the top-left corner. Written by Life32. Described in the source text as coordinates that "probably won't be of use to anyone" and "best ignored". | Reported | S4 |
| `#R` | Coordinates of the top-left corner; "essentially the same as" `#P`, written by XLife. | Reported | S4 |
| `#r` | A rule definition. Golly ignores it "if a rule is already defined". | Verified | S1 |
| `#CXRLE` | Golly's **Extended RLE** metadata line. See §3. | Verified | S1, S2, S3 |

**Handling of unknown tags.** "An RLE-reader should ignore any line marked with
a letter it does not know about" (S4, reported). Golly treats unrecognised
comment lines at the top or bottom of a file as "information lines" and displays
them on request; comment lines interleaved with pattern data are ignored (S1,
verified).

**Anything after the final `!` is ignored**, and trailing comments there are
common (S4, reported).

### Why this project uses `#CXRLE` and not `#O`, `#P` or `#R`

- `#O` means authorship. Writing coordinates there would have made Golly display
  `9223372036854775790 9223372036854775804` as the pattern's author.
- `#P` and `#R` are genuine position tags, but they carry the signed 32-bit
  coordinates of legacy tools. They cannot express a position in a 2^64
  universe, and S4 explicitly advises ignoring `#P`.
- `#G` is not a defined tag at all.
- `#CXRLE` is the established mechanism for exactly this metadata, and Golly
  *restores* it rather than merely tolerating it.

---

## 3. `#CXRLE` — Extended RLE metadata

Verified against S1, S2 and S3.

```
#CXRLE Pos=0,-1377 Gen=3480106827776
```

Space-separated `Keyword=Value` pairs. Two keywords are defined:

| Keyword | Meaning | Notes |
|---------|---------|-------|
| `Pos` | Absolute position of the **upper-left cell of the enclosing rectangle**, which may itself be an off cell. | Comma-separated integers. **May be negative** — the S1 example is `Pos=0,-1377`. S3 notes a reader may accept more or fewer components for higher dimensions, missing values defaulting to zero. |
| `Gen` | Generation count of the stored pattern. | Written by Golly only when greater than zero (S1). Values are large in practice — the S1 example is 3,480,106,827,776. |

From S1: "If the File menu's *Save Extended RLE* option is ticked then comment
lines with a specific format will be added at the start of the file… The
pattern's position is recorded, along with the current generation count if it's
greater than zero. This allows Golly to restore the pattern's position and
generation count when the file is loaded at a later date."

### Signed `Pos` in an unsigned 2^64 universe

`Pos` is signed; this project's coordinates are `ulong`. These are the same
thing on our universe, and that is not a coincidence to be worked around but a
property to be used.

The universe is a torus of exactly 2^64 cells per axis, so coordinates are
residues modulo 2^64. Signed and unsigned interpretations of the same 64 bits
name the **same residue** and therefore the same cell. Reinterpreting the bits
costs nothing and loses nothing:

```
ulong  18446744073709551614   ==   long  -2        (same 64 bits, same cell)
```

So the writer emits `Pos` as the signed reinterpretation of the `ulong` origin,
and the reader converts straight back. Values round-trip exactly, and they land
in the range Golly expects rather than overflowing it. The shipped
`glider.rle`, placed two cells before the wrap point, is written `Pos=-2,-2`.

---

## 4. Header line

Verified against S1.

```
x = <width>, y = <height>, rule = <rule>
```

- "Whitespace can be inserted at any point in this line except at the beginning
  or where it would split a token."
- `rule` is **optional**.
- S3 notes additional dimension parameters (`z`, `w`, …) are used by some
  multi-dimensional tools and that Golly ignores parameters it does not know,
  which is what keeps such files backwards compatible.

Rule notation: this project accepts `B3/S23` and the older survival/birth form
`23/3`, and **rejects everything else**. Loading a HighLife file (`B36/S23`) and
simulating it under Conway's rules would produce confidently wrong output with
no error anywhere, so an unsupported rule is a hard failure.

---

## 5. Body encoding

Verified against S1.

| Token | Meaning |
|-------|---------|
| `b` | One dead cell (two-state rules) |
| `o` | One live cell (two-state rules) |
| `.` | State 0 (multi-state rules) |
| `A`–`X` | States 1–24; `pA`–`pX` states 25–48, continuing to state 255 |
| `$` | End of row |
| `!` | End of pattern |
| `<n>` prefix | Repeat count for the token that follows, e.g. `3o`, `5$` |

From S1: for two-state rules "a `b` represents an off cell, and a `o` represents
an on cell." Row and data terminators are `$` and an optional `!`.

**This project implements the two-state subset only** — `b`, `o`, `$`, `!`, run
counts, and `.` accepted as a synonym for `b`. Multi-state tokens are out of
scope for a B3/S23 engine; encountering one raises a `FormatException` rather
than being silently skipped.

### Line length

Conventionally wrapped at **70 characters**. Golly's own output wraps there, and
the canonical published Gosper glider gun breaks mid-token as
`…bob2o4b` / `obo…`. This project wraps at the same column, which is why its
output for that pattern is byte-identical to the published form — asserted by
`Writer_Reproduces_The_Canonical_Published_Gun_Body`.

Readers must therefore treat the body as a character stream and not assume a
token, or even a run count and its tag, sits wholly on one line.

---

## 6. What this project reads and writes

**Writes:**

```
#N Gosper glider gun
#C <free-text comments>
#CXRLE Pos=-18,-4 Gen=0
x = 36, y = 9, rule = B3/S23
24bo$22bobo$...!
```

**Reads:** `#N` as the name, `#CXRLE` `Pos`/`Gen` as origin and generation, `#C`
as free text. Every other `#` tag — `#O`, `#P`, `#R`, `#r` and anything
unrecognised — is preserved verbatim as a comment and **never reinterpreted**,
per the "ignore unknown tags" rule. `Preserves_Standard_Tags_Without_Reinterpreting_Them`
pins the `#O` case specifically, since that is the one this project previously
got wrong.

---

## 7. Open items

- **S4 could not be fetched** (HTTP 403). The meanings of `#C`, `#N`, `#O`, `#P`
  and `#R` are marked *Reported* for that reason. They are consistent across the
  mirrored copies seen and nothing in S1–S3 contradicts them, but they have not
  been read from a primary document. Anyone able to reach LifeWiki should
  confirm and promote them.
- Golly's `Pos` for a multi-state or higher-dimensional pattern accepts more
  components (S3). Irrelevant to a 2D two-state engine, and not implemented.
