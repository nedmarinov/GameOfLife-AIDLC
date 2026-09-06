# Construction — Logical Design

## Layering

```
GameOfLife.Core          pure domain. No sockets, no JSON, no third-party packages.
   Cell, Universe, Viewport, Rle
        |
GameOfLife.Protocol      framing + message codecs. Knows bytes, not sockets.
        |
GameOfLife.Server        TcpListener, simulation loop, client fan-out.
GameOfLife.Client.Console  ClientWebSocket-free; raw TcpClient + ANSI renderer.
```

The dependency arrow never points back into `Core`. The engine can be tested,
benchmarked and reasoned about with no network in the picture.

## Domain model

`Cell` — `readonly record struct (ulong X, ulong Y)`. Value type, no heap
allocation per cell reference, structural equality for free (needed for the
`HashSet`).

`Universe` — owns `HashSet<Cell> _live` and a reused `Dictionary<Cell,int>`
neighbour-count buffer. Not thread-safe by design: exactly one thread ever
touches it (see concurrency below), so internal locking would be pure cost.

`Viewport` — `readonly record struct (ulong OriginX, ulong OriginY, int Width,
int Height)`. Containment is a subtraction and one unsigned compare, which is
what makes seam-straddling windows free.

## The step algorithm

```
counts.Clear()
foreach live cell c:
    foreach of c's 8 neighbours n:
        counts[n]++
next.Clear()
foreach (cell, n) in counts:
    if n == 3 || (n == 2 && _live.Contains(cell)):
        next.Add(cell)
swap(_live, next)
Generation++
```

Cost is O(population x 8), independent of universe size. Only cells adjacent to
something alive can change state, so the empty remainder of a 2^64 x 2^64
universe is never visited. Buffers are reused across generations so a steady
population produces no steady-state allocation.

## Concurrency

Three roles, deliberately separated so that only one of them touches state:

```
  client conn 1 --\                                    /-- egress ch 1 --> conn 1
  client conn 2 ----> Channel<Command> --> SIM THREAD --+-- egress ch 2 --> conn 2
  client conn N --/     (bounded)         sole owner    \-- egress ch N --> conn N
                                          of Universe
```

1. **Simulation thread (single).** Sole owner of `Universe`. Drains the command
   channel between ticks, applies commands, steps, then renders one frame per
   distinct viewport and publishes. No locks on the hot path.
2. **Command ingress.** One bounded `Channel<Command>`, many writers, one
   reader. All mutation is serialized through a single writer, so there is no
   contention and no torn state — the property a lock would have been bought to
   provide, obtained structurally instead.
3. **Per-client egress.** One bounded channel per connection, capacity 2,
   `BoundedChannelFullMode.DropOldest`.

The egress policy is the load-bearing decision: **a slow client must never
stall the simulation or any other client.** Because a frame is a complete
snapshot rather than a delta, dropping a stale frame is not a compromise — it
is correct. A client on a congested link renders the newest state and silently
skips intermediate generations, which is exactly what an observer should do.

Two consequences fall out for free:
- **Mid-run joins need no replay.** A new client gets a whole snapshot on the
  next tick and is immediately consistent.
- **A dead client cannot leak memory.** The queue is bounded, so a connection
  that stops reading costs two frames, not unbounded growth.

## Tick loop

```
while running:
    drain command channel (bounded batch, so commands cannot starve the tick)
    if not paused: universe.Step()
    for each distinct viewport among connected clients:
        render bitmap once
    publish to each client's egress channel (non-blocking; drop-oldest)
    delay until next tick boundary
```

Rendering is per *distinct viewport* rather than per client, so N clients
watching the same window cost one render.

## Sequence — a client edits while the simulation runs

```
client A          server ingress      sim thread         clients A,B,C
   |  toggle(x,y)      |                  |                   |
   |------------------>|                  |                   |
   |                   |--- Command ----->|                   |
   |                   |                  | apply toggle      |
   |                   |                  | Step()            |
   |                   |                  | render viewport   |
   |                   |                  |--- frame -------->|
```

The edit is never applied locally by the client. The server is authoritative,
so all observers stay consistent by construction rather than by convention.
