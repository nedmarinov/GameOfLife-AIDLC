# Inception — Units of Work

Decomposition into independently reviewable units. Each unit has one owner
concept, an explicit done condition, and a human gate. Nothing advances on AI
output alone.

| Unit | Owns | Done when | Gate |
|------|------|-----------|------|
| U1 Cell & torus arithmetic | `Cell`, neighbour enumeration | seam tests green | torus test read line by line |
| U2 Universe & step | `Universe`, B3/S23 sparse step | rules + population tests green | complexity argument checked against the code |
| U3 Viewport | `Viewport`, bitmap rendering | containment across the seam correct | wrap-free containment verified |
| U4 RLE | reader, writer, pattern files | round-trip green; file opens in Golly | example file opened in a third-party tool |
| U5 Framing | length-prefix codec on Pipelines | byte-at-a-time test green | partial-read test reviewed |
| U6 Messages | JSON control codec | `ulong` > 2^53 round-trips | precision regression guard present |
| U7 Server | listener, sim loop, channels, fan-out | 3 clients observe one sim | concurrency model walked through aloud |
| U8 Console client | render, input, reconnect | edit on one client appears on others | manual 3-client run |
| U9 Docs | README, ADRs, audit log | clean clone runs on 3 OSes | full clean-clone verification |

## Dependency order

```
U1 -> U2 -> U3 ---------\
                         >-- U7 -> U8 -> U9
U4 -------\             /
U5 -> U6 -/
```

U1-U4 are pure domain and carry no transport concepts, so they are testable
without a socket. U5-U6 are pure codec and testable without a server. Only U7
combines them, which keeps the concurrent surface — the part hardest to test —
as small as possible.

## Mapping to bolts

Bolt 1 = U1+U2+U3 · Bolt 2 = U4 · Bolt 3 = U5+U6 · Bolt 4 = U7 ·
Bolt 5 = U8 · Bolt 6 = U9 · Bolt 7 = stretch WebSocket bridge + HTML client.
