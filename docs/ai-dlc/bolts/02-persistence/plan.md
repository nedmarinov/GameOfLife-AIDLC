# Bolt 02 — Persistence · plan

> **Reconstructed.** This bolt was executed before the per-bolt record existed.
> The plan below is recovered from `00-plan.md`, `04-units-of-work.md` and the
> commit itself; it is not a document that was written in advance and then
> followed. Bolt 9 onward are planned before execution. Distinguishing the two
> matters: a planning record backdated to match the outcome proves nothing.

**Unit(s):** U4
**Status:** complete

## Goal

Store and load state in a format other tools can read, and ship a Gosper glider gun as a real Life file.

## In scope

- RLE reader and writer.
- Absolute `ulong` origin and generation counter carried in the file.
- `patterns/gosper-glider-gun.rle` plus a seam-crossing glider.

## Out of scope

- Multi-state rules, non-Conway rule sets.

## Risks going in

- A format extension that only this project understands, defeating the reason RLE was chosen.

## Done when

- The gun parses to exactly 36 cells and grows without bound.
- A saved file round-trips including generation.
- The example opens in a third-party tool.
