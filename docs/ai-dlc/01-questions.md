# Inception — Clarifying Questions

Sent to the requester at the start of the engagement. The brief is small enough
that guessing wrong on any of these would mean rework, so each carries the
assumption held while awaiting an answer, and the cost of that assumption being
wrong.

| # | Question | Assumption held | Cost if wrong |
|---|----------|-----------------|---------------|
| 1 | Is the 100x100 a **viewport** into the 2^64 universe, or a separate small-universe mode? | Viewport, with a pannable `ulong` origin | Low. The engine is unaffected; only the client's origin handling changes. |
| 2 | May clients edit **while the simulation runs**, or only before start? | Yes — live edits, applied server-side between ticks | **High.** Editing during a run is what makes the command-ingress channel and the single-writer model necessary. Edit-before-start-only would permit a much simpler design. |
| 3 | Should a saved file be **resumable** (generation counter + full state), or seed patterns only? | Resumable, via RLE comment extensions | Medium. Changes the file format contract. |
| 4 | Any expectation of persistence across restart, authentication, or horizontal scale? | None — single in-memory process | Medium. Would introduce a datastore and change the deployment story. |

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
