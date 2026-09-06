# Bolt 01 — Core engine · plan

> **Reconstructed.** This bolt was executed before the per-bolt record existed.
> The plan below is recovered from `00-plan.md`, `04-units-of-work.md` and the
> commit itself; it is not a document that was written in advance and then
> followed. Bolt 9 onward are planned before execution. Distinguishing the two
> matters: a planning record backdated to match the outcome proves nothing.

**Unit(s):** U1, U2, U3
**Status:** complete

## Goal

A 2^64 x 2^64 toroidal universe that costs memory proportional to population, with wrap that cannot be subtly wrong.

## In scope

- `Cell` as two `ulong`s, with neighbour enumeration.
- `Universe` as a sparse live-cell set with a B3/S23 step.
- `Viewport` with wrap-free containment and bitmap rendering.
- Tests that prove the torus rather than assert it.

## Out of scope

- Sockets, serialisation, any transport concept in `Core`.

## Risks going in

- A1: a dense grid or a stored dimension silently capping the universe.
- A2: signed coordinates.
- A3: modulo where unchecked overflow is exact and free.
- A8: neighbour counting that visits dead space.

## Done when

- A glider crosses the `ulong.MaxValue` seam in both dimensions and arrives intact.
- A glider wrapping in one dimension only also works — asymmetric wrap is where hand-written modulo breaks.
- `Core` has zero `PackageReference` and no `%` on any coordinate.
