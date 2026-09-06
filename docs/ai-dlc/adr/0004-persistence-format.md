# ADR 0004 — RLE with comment-carried universe metadata

**Status:** Accepted · **Date:** 2026-09-07

## Context

State must be storable and loadable, and a Gosper glider gun example file must
ship. Positions are `ulong` in a 2^64 universe, and a resumed run should keep
its generation counter.

## Decision

Use standard Life **RLE**, carrying position and generation in Golly's
**Extended RLE `#CXRLE` line**, which is the established mechanism for exactly
this metadata:

```
#N Gosper glider gun
#CXRLE Pos=9223372036854775790,9223372036854775804
x = 36, y = 9, rule = B3/S23
24bo$22bobo$12b2o6b2o12b2o$...!
```

`Pos` is the absolute position of the enclosing rectangle's upper-left cell and
`Gen` the generation count; Golly *restores* both on load rather than merely
tolerating them. Full tag reference and provenance in `docs/rle-format.md`.

`Pos` is signed while this universe is unsigned, and that costs nothing: the
universe is a torus of exactly 2^64 cells per axis, so both are residues modulo
2^64 and the signed and unsigned readings of the same 64 bits name the same
cell. The writer emits the signed reinterpretation, which keeps values inside
the range other tools expect — an origin two cells before the wrap point is
written `Pos=-2,-2`.

Three alternatives were rejected. `#O` already means *author name and creation
date*, so coordinates there would display as the pattern's author. `#G` is not a
defined tag at all. `#P` / `#R` are genuine position tags but carry legacy
signed 32-bit values that cannot express a 2^64 origin, and the format's own
documentation advises ignoring `#P`.

## Consequences

- The shipped example is a real Life file: it opens in Golly and other existing
  tools, so the pattern can be verified against an independent implementation
  rather than only against our own reader.
- Origin and generation ride in `#CXRLE`, so the file round-trips our full state
  *and* another tool actually restores it, rather than discarding it as a
  comment. Using the established line instead of a private convention means
  there is one fewer thing about this format that only we understand.
- Files stay small and diffable, and the glider gun is recognisable in the
  repository as text.
- A pattern straddling the 2^64 seam would have an ambiguous bounding box under
  naive min/max. Solved rather than documented: the bounding span is found by
  locating the *largest empty run* on each axis and taking the complement, which
  is correct whether or not the pattern crosses the seam.
- A pattern genuinely wider than `int.MaxValue` cannot be expressed in an RLE
  header. Rejected with a clear error rather than truncated.

## Alternatives rejected

**Bespoke JSON state file.** Trivial to write and would carry `ulong` origins
and the generation counter directly. Rejected because the brief asks for an
example Gosper glider gun file, and a file only our own code can read is a
weaker artifact than one a reviewer can open in an existing tool.

**Plaintext `.cells`.** Human-readable and simple, but has no standard place
for an origin or generation, and is bulky for sparse patterns.

**Binary snapshot of the live set.** Compact and exact for very large
populations. Rejected: opaque, undiffable, and unreadable by any other tool,
for a demo whose populations are small.
