# Bolt 06 — Documentation and portability · plan

> **Reconstructed.** This bolt was executed before the per-bolt record existed.
> The plan below is recovered from `00-plan.md`, `04-units-of-work.md` and the
> commit itself; it is not a document that was written in advance and then
> followed. Bolt 9 onward are planned before execution. Distinguishing the two
> matters: a planning record backdated to match the outcome proves nothing.

**Unit(s):** U9
**Status:** complete

## Goal

Make the repository runnable by someone who has never seen it, and stop claiming portability that has not been exercised.

## In scope

- README kept to a clone-and-run at the top.
- Operations guide: what to check when something looks wrong, and the known limits.
- Cross-platform review.

## Out of scope

- Running on Linux or Windows — no such machine is available here.

## Risks going in

- Documentation making claims no test covers.

## Done when

- A reviewer can clone and run in under a minute.
- Unverified platforms are stated as unverified.
