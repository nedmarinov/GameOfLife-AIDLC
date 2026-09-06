# Bolt 00 — Inception · plan

> **Reconstructed.** This bolt was executed before the per-bolt record existed.
> The plan below is recovered from `00-plan.md`, `04-units-of-work.md` and the
> commit itself; it is not a document that was written in advance and then
> followed. Bolt 9 onward are planned before execution. Distinguishing the two
> matters: a planning record backdated to match the outcome proves nothing.

**Unit(s):** —
**Status:** complete

## Goal

Turn a one-page brief into requirements that can be tested, decisions that can be reviewed, and a decomposition that can be sequenced.

## In scope

- Restate the intent and separate what the brief says from what is being assumed.
- Raise the clarifying questions whose answers would change the design.
- Write requirements, each traceable to a test or a named manual check.
- Record the load-bearing decisions as ADRs, each carrying its rejected alternatives.
- Decompose into units with an explicit dependency order.

## Out of scope

- Any code. Nothing is built in this bolt.

## Risks going in

- Ceremony outgrowing the deliverable — mitigated by keeping the root README short and the process artifacts under `docs/`.
- Deciding the transport before understanding the traffic shape.

## Done when

- Every requirement names the test or check that will prove it.
- Every ADR states what was rejected and why.
- The unit dependency order is explicit.
