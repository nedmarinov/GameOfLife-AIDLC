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
#O 9223372036854775800 9223372036854775804     <- absolute ulong origin
#G 0                                            <- generation counter
x = 36, y = 9, rule = B3/S23
24bo$22bobo$12b2o6b2o12b2o$...!
```

## Consequences

- The shipped example is a real Life file: it opens in Golly and other existing
  tools, so the pattern can be verified against an independent implementation
  rather than only against our own reader.
- `#O` and `#G` are `#`-prefixed comments, which the RLE spec permits and other
  tools ignore, so the file round-trips our full state without ceasing to be
  valid for anyone else.
- Files stay small and diffable, and the glider gun is recognisable in the
  repository as text.
- A pattern straddling the 2^64 seam has an ambiguous bounding box on save.
  Not reachable in the demo; documented rather than solved.

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
