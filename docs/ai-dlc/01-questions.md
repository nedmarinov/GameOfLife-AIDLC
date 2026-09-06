# Inception — Clarifying Questions

Sent to the requester at the start of the engagement. The brief is small enough
that guessing wrong on any of these would mean rework, so each was raised before
construction rather than resolved by assumption.

**All four were answered on 2026-09-07. Decisions below are settled.**

| # | Question | Decision | Notes |
|---|----------|----------|-------|
| 1 | Is the 100x100 a **viewport** into the 2^64 universe, or a separate small-universe mode? | **Viewport**, with a pannable `ulong` origin | The only reading under which "support a large universe" and "the UI is 100x100" are the same feature rather than two unrelated ones. Already implemented in Bolt 1. |
| 2 | May clients edit **while the simulation runs**, or only before start? | **Only while paused**, but by *any* connected client | See below. |
| 3 | Should a saved file be **resumable**, or seed patterns only? | **Resumable**, via tagged `#C` comment lines | Keeps the file valid for Golly and other tools while round-tripping origin and generation. ADR 0004. |
| 4 | Persistence across restart, authentication, or horizontal scale? | **None** — single in-memory process | State survives only through explicit save/load, which the brief already requires. Listed as an explicit non-goal, so the boundary reads as a decision. |

## On question 2 — why edits are confined to the paused state

The brief uses one verb for the running phase, twice:

> "multiple clients should be able to connect to it and **observe** the
> generation updates"

> "a simple UI with the possibility to interactively configure the **initial
> state** of the universe (100 x 100), **start** the algorithm, and **observe**
> the changes"

That is a sequence: configure the initial state, start, then observe. Nothing
asks for edits mid-run, and "we hope to see a simple, minimalist solution"
argues against volunteering the extra scope.

So the server accepts toggles only while paused — including before the first
start — and rejects them with an explicit error while running. Any connected
client may edit during setup, and every client sees those edits live, so the
multi-client interaction the brief does ask for is fully present.

**This restriction is a policy check in one place, not an architectural
commitment.** Edits arrive from N concurrent connections whether or not the
simulation is ticking, so they are serialized through the command channel into
the single-writer simulation thread either way; ADR 0003 is unchanged under
every option considered. Lifting the restriction to allow live editing is a
one-line change, which is the point: the boundary was found and chosen, not
stumbled into.

## Requirements read directly from the brief, not assumed

- One server instance owns the simulation; clients observe. Clients never
  compute generations locally.
- Universe is 2^64 x 2^64 and wraps in both dimensions (torus).
- Universe is mostly empty; the working set is one or a few objects.
- The UI may be console-based.
- Store/load must exist, with a Gosper glider gun example file shipped.
- Protocol choice is explicitly left to the implementer.

## Deliberate non-goals

Recorded so that their absence reads as a decision and not an oversight:
authentication, TLS, horizontal scale, database persistence, an HTTP API,
containerization, and any guarantee for patterns whose population grows without
bound (the sparse set has no fixed cap, but host memory does).
