# Bolt 08 — Zoom · plan

> **Reconstructed.** This bolt was executed before the per-bolt record existed.
> The plan below is recovered from `00-plan.md`, `04-units-of-work.md` and the
> commit itself; it is not a document that was written in advance and then
> followed. Bolt 9 onward are planned before execution. Distinguishing the two
> matters: a planning record backdated to match the outcome proves nothing.

**Unit(s):** U11
**Status:** complete

## Goal

Make the size of the universe visible rather than merely claimed.

## In scope

- Power-of-two zoom on `Viewport`, centred rather than origin-anchored.
- Zoom in the frame header and the subscribe message.
- Keys in the terminal, buttons and scroll wheel in the browser.

## Out of scope

- Magnification below 1:1. A cell is the smallest thing there is, so it would show no more information and only shrink the visible area.

## Risks going in

- `Width << Zoom` overflowing, making the containment test silently accept cells outside the window.

## Done when

- Two cells a quarter of the universe apart appear in the same frame at maximum zoom.
- Zoom is per client, like the viewport.
- The overflow boundary is refused, not discovered.
