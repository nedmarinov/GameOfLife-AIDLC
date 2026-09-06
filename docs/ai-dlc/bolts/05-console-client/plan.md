# Bolt 05 — Console client · plan

> **Reconstructed.** This bolt was executed before the per-bolt record existed.
> The plan below is recovered from `00-plan.md`, `04-units-of-work.md` and the
> commit itself; it is not a document that was written in advance and then
> followed. Bolt 9 onward are planned before execution. Distinguishing the two
> matters: a planning record backdated to match the outcome proves nothing.

**Unit(s):** U8
**Status:** complete

## Goal

Watch and edit a 100x100 window in a terminal, on any platform, with no TUI dependency.

## In scope

- Half-block rendering: 100 cell rows into 50 terminal rows.
- Cursor, editing, panning, speed, load and save prompts.
- Reconnection with backoff and viewport re-subscription.

## Out of scope

- A browser client — that is Bolt 7.

## Risks going in

- An off-by-one in row pairing produces output that still looks like Life and is wrong by one row everywhere.

## Done when

- Even cell rows render as foreground changes, odd rows as background.
- An edit in one client appears in another.
- Colour escapes are emitted only on change.
