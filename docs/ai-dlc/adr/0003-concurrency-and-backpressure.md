# ADR 0003 — Single-writer simulation with bounded, lossy egress

**Status:** Accepted · **Date:** 2026-09-07

## Context

One `Universe` is mutated by the tick loop and by edits arriving from any of N
concurrent connections. Frames must reach every client each tick. Clients have
unequal link speeds, and one of them may stop reading entirely.

## Decision

- **One simulation thread is the sole owner of `Universe`.** No other thread
  touches it, and `Universe` contains no synchronisation.
- **Commands reach it through one bounded `Channel<Command>`** (many writers,
  one reader), drained between ticks.
- **Each connection has its own bounded egress channel**, capacity 2, with
  `BoundedChannelFullMode.DropOldest`.

## Consequences

- Mutual exclusion is structural rather than enforced: there is no lock to
  forget to take, and no lock ordering to get wrong.
- No contention on the hot path; the tick loop never blocks on a client.
- A slow client degrades to a lower frame rate and nothing else. It cannot
  stall the simulation, delay other clients, or grow server memory.
- Dropping stale frames is correct rather than merely tolerable, because each
  frame is a complete snapshot and the newest one supersedes the rest.
- Mid-run joins need no replay or catch-up path.
- Commands are applied at tick boundaries, so an edit may be observed up to one
  tick later. Acceptable and, at 100ms, imperceptible.
- The drain is a bounded batch per tick, so a flood of commands cannot starve
  the simulation.

## Alternatives rejected

**`lock` around `Universe`.** Simplest to write. Rejected because every client
read would contend with the tick, and the correctness argument becomes "every
access site remembered to take the lock" — a property that decays under edits.

**Unbounded egress queues.** Rejected outright: a client that stops reading
becomes an unbounded memory leak driven by a remote party. This is the exact
failure mode a device gateway must not have.

**Blocking writes to clients.** Rejected: the slowest client would dictate the
simulation rate for everyone.

**Immutable universe snapshots with lock-free publication.** Elegant, and would
let readers run concurrently. Rejected as unnecessary — there is exactly one
reader path (the renderer) and it already runs on the owning thread.

**Delta frames instead of snapshots.** Smaller on the wire, but they make
dropping a frame unsafe, which forfeits the entire backpressure design and
reintroduces replay-on-join. Rejected; see ADR 0002 on frame sizing.
