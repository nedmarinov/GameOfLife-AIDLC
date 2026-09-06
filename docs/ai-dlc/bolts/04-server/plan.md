# Bolt 04 — Server · plan

> **Reconstructed.** This bolt was executed before the per-bolt record existed.
> The plan below is recovered from `00-plan.md`, `04-units-of-work.md` and the
> commit itself; it is not a document that was written in advance and then
> followed. Bolt 9 onward are planned before execution. Distinguishing the two
> matters: a planning record backdated to match the outcome proves nothing.

**Unit(s):** U7
**Status:** complete

## Goal

One thread owns the universe; many clients watch it; no client can hurt another.

## In scope

- `TcpListener`, per-connection read and write loops.
- Single-writer simulation thread with a bounded command channel.
- Per-client egress bounded at two frames, dropping oldest.
- The paused-only edit policy, refused explicitly.
- Path-safe pattern load and save.

## Out of scope

- Any UI.

## Risks going in

- A6: unbounded per-client queues letting a stalled reader consume server memory.
- A7: locks sprinkled through `Universe` instead of single-writer ownership.
- A10: a client-supplied file name reaching the filesystem unchecked.

## Done when

- Three concurrent clients observe one simulation.
- An edit by one client reaches the others.
- A toggle while running is refused with a reason, not dropped.
- Path traversal is refused on both read and write.
