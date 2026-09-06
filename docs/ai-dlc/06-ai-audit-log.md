# AI Audit Log

This project was built with AI agents. The interesting record is not where the
model helped — it is **where its output was wrong and what caught it.** Each
entry states what was asked, what came back, what was wrong, and what shipped.

Entries are added as they occur, in bolt order.

## Anticipated failure modes

Recorded before construction so they can be checked for rather than discovered.
Each is a defect this problem is known to invite, and each has a guard.

| # | Expected failure | Guard |
|---|------------------|-------|
| A1 | Dense grid or a bounded `width`/`height` field, silently capping the universe | `Universe` exposes no dimension constant; `TorusTests` at `ulong.MaxValue` |
| A2 | `long` or `int` coordinates instead of `ulong` | seam tests only pass with unsigned wraparound |
| A3 | Modulo arithmetic for wrap where unchecked overflow is free and exact | code review; `%` on a coordinate is a red flag |
| A4 | `ulong` serialized as a JSON number, losing precision above 2^53 | `ProtocolTests` round-trips `ulong.MaxValue - 1` |
| A5 | Framing that assumes one `Read` yields one whole message | `FramingTests` feeds the stream one byte at a time |
| A6 | Unbounded per-client queue — a stalled reader becomes a memory leak | bounded channel, capacity 2, `DropOldest` (ADR 0003) |
| A7 | `lock` sprinkled through `Universe` instead of single-writer ownership | `Universe` contains no synchronisation primitives at all |
| A8 | Neighbour counting that visits dead space, reintroducing O(universe) | step iterates live cells only; complexity argued in 05-logical-design |

## Log

### Bolt 0 — Inception

No code produced. The design decisions in ADRs 0001-0004 were reached by
stating the alternative first and requiring an argument to reject it; the
rejected options are recorded in each ADR rather than discarded, so a reviewer
can check the reasoning rather than the conclusion.

One correction already applied at plan stage: the initial transport
recommendation was WebSocket, chosen for implementation speed. It was replaced
by raw TCP after re-reading the brief — the traffic is server-initiated
broadcast, not request/response, and delegating the framing would have removed
the part of the problem most worth owning and testing. Recorded in ADR 0002
with WebSocket kept as a documented alternative rather than deleted.

### Bolt 1 — Core engine

**Entry 1 — a wrong test that looked like an engine bug.**

*Asked for:* a test proving a glider crosses the `ulong.MaxValue` seam intact.

*Produced:* the test ran the glider three diagonal steps from
`(MaxValue-1, MaxValue-1)`, compared the whole live set at each step, and then
added a final check that `(1, 1)` — the glider's wrapped placement origin — was
alive.

*What was wrong:* the glider's five offsets are `(1,0) (2,1) (0,2) (1,2) (2,2)`.
There is no cell at offset `(0,0)`, so the placement origin is never itself
alive. The engine was correct; the assertion was not. The observed live set,
`(2,1) (2,3) (3,3) (3,2) (1,3)`, is exactly the glider at origin `(1,1)` — the
set-equality assertions had already passed, which is what made the diagnosis
quick.

*Why it matters:* this is the dangerous class of AI defect. A wrong assertion in
a test named "proves the torus works" fails in a way that looks like an engine
bug, and the obvious response — adjusting the wrap arithmetic until the test
goes green — would have introduced a real defect into working code to satisfy a
broken oracle. The guard is that the test asserted the *whole* live set
independently, so the two assertions disagreed with each other rather than both
pointing at the engine.

*Shipped instead:* the final check now asserts every live cell has coordinates
below 10, which states the actual claim — cells this low are only reachable
from a start at `MaxValue - 1` by wrapping — rather than a coincidental
consequence of it.

*Guard added:* none needed; A2 already covered the engine behaviour. The lesson
is recorded rather than automated: when an AI-written test fails, establish
whether the oracle or the subject is wrong before changing either.
