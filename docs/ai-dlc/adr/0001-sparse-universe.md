# ADR 0001 — Sparse live-cell set as the universe representation

**Status:** Accepted · **Date:** 2026-09-07

## Context

The universe is 2^64 x 2^64 and wraps in both dimensions. It is stated to be
mostly empty, holding one or a few objects such as a Gosper glider gun.

## Decision

Represent the universe as `HashSet<Cell>` containing **only live cells**, where
`Cell` is a `readonly record struct (ulong X, ulong Y)`. Step by tallying
neighbour counts around live cells only.

Implement torus wrap as **unchecked `ulong` overflow**. Because each dimension
is exactly 2^64 and coordinates are `ulong`, `ulong.MaxValue + 1 == 0` is the
wrap. No modulo, no branch, no special case at the seam.

## Consequences

- Memory and per-generation cost are O(population), independent of universe
  size.
- Wrap correctness is inherited from the machine's integer arithmetic rather
  than implemented, so it cannot be subtly wrong in one of the four seam
  directions.
- No dimension constant exists anywhere in the code, so no code path can
  overflow the universe bounds.
- Iterating the universe in coordinate order is not possible. Nothing in the
  requirements needs it.
- Population is bounded by host memory, not by the universe. Documented as a
  non-goal.

## Alternatives rejected

**Dense grid (`bool[,]` or bitset).** Not slow — impossible. 2^128 cells cannot
be allocated or indexed. Rejected on feasibility, not performance.

**Chunked / tiled sparse grid** (hash of 64x64 tiles). The standard scaling
answer, and the right one at very high population: it gets cache locality and
amortises hashing. Rejected here because the brief specifies a mostly-empty
universe, where the tile bookkeeping is pure overhead and the extra indirection
costs the clarity that makes the wrap argument obvious. Revisit if population
reaches the 10^5-10^6 range.

**HashLife (quadtree + memoisation).** Asymptotically superior for patterns
with regular structure, and would run a glider gun for 2^64 generations in
negligible time. Rejected as disproportionate: it is a substantial
implementation, it obscures rather than demonstrates the torus reasoning, and
"simple, minimalist solution we can run" is an explicit requirement.

**`long` instead of `ulong` coordinates.** Rejected: the universe is specified
as 2^64 per dimension, and signed overflow reasoning is harder to defend than
unsigned wraparound, which is well-defined in C#.
