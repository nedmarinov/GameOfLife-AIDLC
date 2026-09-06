# Bolt 03 — Protocol · plan

> **Reconstructed.** This bolt was executed before the per-bolt record existed.
> The plan below is recovered from `00-plan.md`, `04-units-of-work.md` and the
> commit itself; it is not a document that was written in advance and then
> followed. Bolt 9 onward are planned before execution. Distinguishing the two
> matters: a planning record backdated to match the outcome proves nothing.

**Unit(s):** U5, U6
**Status:** complete

## Goal

Turn a TCP byte stream back into messages, and carry 2^64 coordinates without losing them.

## In scope

- Length-prefixed framing on `System.IO.Pipelines`.
- JSON control messages; binary viewport frames.
- A fixed-size bitmap frame for a 100x100 window.

## Out of scope

- Sockets. This bolt knows bytes, not connections.

## Risks going in

- A4: `ulong` as a JSON number, silently rounding above 2^53.
- A5: framing that assumes one read yields one whole message.

## Done when

- A stream delivered one byte at a time reassembles correctly and completes only on the final byte.
- Multi-segment sequences work, since Pipelines produces them when a frame spans buffers.
- `ulong.MaxValue` survives a JSON round trip.
