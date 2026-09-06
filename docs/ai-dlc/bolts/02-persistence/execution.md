# Bolt 02 — Persistence · execution

**Commits**

- `3de244a feat(core): RLE persistence and the Gosper glider gun example`
- `b8e5e32 docs: document the RLE format with provenance, adopt #CXRLE`

**Diff:** 20 files, +1365 −91
**Tests after this bolt:** 57

## What was built

- 52 tests green after the first commit, 57 after the correction.
- Saving a seam-straddling pattern is **solved rather than documented**: the writer finds the largest empty run on each axis and takes its complement, correct whether or not the pattern wraps.
- An unsupported rule is a hard failure — loading HighLife and simulating it as Conway would be confidently wrong with no error anywhere.

## What changed from the plan

- The metadata carrier changed twice. First from `#O`/`#G` to tagged `#C` comments, then from those to Golly's `#CXRLE Pos=/Gen=`.
- `Pos` is signed and our coordinates unsigned, which costs nothing on a 2^64 torus: both are residues mod 2^64, so the same 64 bits name the same cell. An origin two cells before the wrap writes as `Pos=-2,-2`.

## What was learned

- **Audit entry 2.** `#O` already means *author and creation date*; `#G` is not a tag at all. Golly would have shown our coordinates as the pattern's author.
- **Audit entry 3.** The fix was itself wrong: `#CXRLE` already existed for exactly this. Correcting `#O` without re-reading the spec left the larger error standing — I verified the answer, not the question.
- A round-trip test could never have caught either. Our reader and writer agreed with each other perfectly *because they shared the misreading*. The guard is now an oracle outside the project: the writer must reproduce the published gun byte for byte.
