# ADR 0004 — RLE with comment-carried universe metadata

**Status:** Accepted · **Date:** 2026-09-07

## Context

State must be storable and loadable, and a Gosper glider gun example file must
ship. Positions are `ulong` in a 2^64 universe, and a resumed run should keep
its generation counter.

## Decision

Use standard Life **RLE**, extending it only through legal comment lines:

```
#N Gosper glider gun
#C origin: 9223372036854775800 9223372036854775804
#C generation: 0
x = 36, y = 9, rule = B3/S23
24bo$22bobo$12b2o6b2o12b2o$...!
```

`#C` is free-text comment in every RLE implementation, so tagged `#C` lines
cannot collide with a defined tag. The obvious-looking `#O` and `#G` were
rejected: `#O` already means *author name and creation date*, and `#G` is not
defined at all. `#P` / `#R` are the real position tags but hold signed 32-bit
values, which cannot express a `ulong` origin.

## Consequences

- The shipped example is a real Life file: it opens in Golly and other existing
  tools, so the pattern can be verified against an independent implementation
  rather than only against our own reader.
- Origin and generation ride in `#C` comments, which every tool ignores, so the
  file round-trips our full state without ceasing to be valid for anyone else.
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
