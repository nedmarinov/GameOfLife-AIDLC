# Multiplayer Game of Life — Implementation Plan

Status: **DRAFT — awaiting review**
Method: AI-DLC (Inception → Construction → Operations), human-gated at each bolt.

---

## 1. Intent

One server owns a single Game of Life simulation over a 2^64 x 2^64 toroidal
universe. Multiple clients connect concurrently, observe generation updates in
real time, and interactively edit the universe through a 100x100 viewport.
State can be stored to and loaded from disk; a Gosper glider gun ships as an
example file.

## 2. The three problems that actually matter

Everything else in the brief is table stakes. These three drive the design.

### 2.1 A 2^64 x 2^64 universe cannot be allocated

Sparse representation: the universe is the **set of live cells**, not a grid.
`HashSet<Cell>` where `Cell` is a `readonly record struct (ulong X, ulong Y)`.
Memory is O(live cells), independent of universe size.

Step is the standard sparse algorithm — tally neighbour counts only around live
cells:

```
counts = Dictionary<Cell,int>
for each live cell c:  for each of c's 8 neighbours n:  counts[n]++
next = { cell : count == 3 || (count == 2 && cell was live) }
```

O(live cells x 8) per generation. For a mostly-empty universe this is the right
complexity — a dense grid would be O(2^128) and is not merely slow, it is
impossible.

### 2.2 Torus wrap on 2^64 is free

The universe wraps in both dimensions. Because each dimension is exactly 2^64
and coordinates are `ulong`, **wrapping is unchecked integer overflow**:
`ulong.MaxValue + 1 == 0`. No modulo, no branch, no special case at the seam.

This is the single most important insight in the design and it must be proven,
not asserted. See test plan 8.1.

### 2.3 A 100x100 UI over a 2^64 universe is a viewport

The 100x100 is not a smaller universe — it is a window with a `ulong` origin
into the large one. Viewport containment is also wrap-free with unchecked
arithmetic:

```
dx = cell.X - viewport.OriginX      // unchecked; wraps
visible = dx < viewport.Width       // single unsigned compare
```

A viewport straddling the 2^64 seam needs no special handling.

## 3. Transport: raw TCP, length-prefixed frames

**Decision: TCP sockets with a custom framing protocol, built on
`System.IO.Pipelines`.** Not WebSocket, not SignalR, not gRPC.

Rationale:
- The brief explicitly leaves protocol choice open, and the problem — one
  long-lived server, many persistent client connections receiving pushed state
  — is a socket problem, not a request/response problem.
- Owning the framing means owning the parts that are actually hard and that
  higher-level libraries hide: message boundaries over a byte stream, partial
  reads, backpressure, and half-open connection detection.
- `System.IO.Pipelines` is the modern .NET answer for exactly this and avoids
  the classic hand-rolled buffer-management bugs.

Rejected alternatives are recorded in `docs/ai-dlc/adr/0002-transport.md`.

### 3.1 Frame format

```
+--------+--------+------------------+
| len:u32| type:u8|  payload (len-1) |    little-endian, len covers type+payload
+--------+--------+------------------+
```

| type | direction | payload |
|------|-----------|---------|
| 0x01 | both      | UTF-8 JSON control message |
| 0x02 | server->client | binary viewport bitmap |

Control messages stay JSON so the wire is human-readable and the protocol is
extensible. The per-generation frame is binary because it is the hot path.

### 3.2 The viewport frame is a fixed-size bitmap

A 100x100 viewport is 10,000 cells = **1,250 bytes**, one bit per cell, plus a
small header (generation, origin X/Y, width, height). Constant size regardless
of population, no per-cell allocation, no serializer.

The obvious alternative — a JSON array of live coordinates — is larger for any
population above ~150 cells, varies in size per frame, and allocates. For a
system whose reason to exist is pushing state at a fixed tick rate, the fixed
1,250-byte frame is the correct call.

### 3.3 Control messages

Client -> server:
```json
{"op":"subscribe","originX":"9223372036854775808","originY":"...","w":100,"h":100}
{"op":"toggle","x":"...","y":"..."}
{"op":"pan","dx":-10,"dy":0}
{"op":"control","action":"start|pause|step|clear|speed","value":100}
{"op":"load","file":"patterns/gosper-glider-gun.rle"}
{"op":"save","file":"snapshots/run1.rle"}
```

Server -> client:
```json
{"op":"hello","gen":"0","running":false,"tickMs":100,"population":36}
{"op":"status","gen":"1204","running":true,"tickMs":100,"population":86}
{"op":"error","message":"..."}
```

**`ulong` values are serialized as JSON strings.** JSON numbers are IEEE 754
doubles; anything above 2^53 loses precision. A 2^64 universe makes this a
correctness bug, not a style preference — and it is the kind of defect that
survives code review unless someone is looking for it.

## 4. Concurrency model

Three roles, deliberately separated:

1. **Simulation thread (single).** Sole owner of the `Universe`. Nothing else
   touches it. No locks on the hot path.
2. **Command ingress.** Client connections post commands to one bounded
   `Channel<Command>`. The simulation thread drains the channel between ticks
   and applies commands. All mutation is serialized through one writer, so
   there is no lock contention and no torn state.
3. **Per-client egress.** Each connection has its own bounded channel
   (capacity 2, `BoundedChannelFullMode.DropOldest`).

The egress policy is the important part: **a slow client must never stall the
simulation or other clients.** Because each frame is a complete snapshot rather
than a delta, dropping a stale frame is not just acceptable — it is correct.
A client on a congested link renders the newest state and skips intermediate
generations, which is exactly the desired behaviour for an observer.

This also makes mid-game joins trivial: a new client receives a full snapshot
on the next tick and is immediately consistent. No replay, no catch-up logic.

Server is authoritative for all mutations, so all clients stay in sync by
construction.

## 5. Console client

Single .NET console app, `dotnet run`, no TUI dependency.

- **Rendering:** Unicode half-blocks pack two cell rows into one terminal row —
  `▀` `▄` `█` ` ` — so 100x100 cells fit in 100x50 characters, a normal
  terminal. Frame is composed into a `StringBuilder` and written in one call
  after `ESC[H`; alternate screen buffer on enter/exit.
- **Input:** dedicated reader task on `Console.ReadKey(intercept:true)`.
  Arrows move cursor / pan, space toggles a cell, `s` start/pause, `n` step,
  `+`/`-` speed, `w`/`o` save/load, `q` quit.
- **Modes:** edit and observe. Both are live — edits during a run are pushed to
  the server and appear on every other connected client, which is the
  multiplayer demo.
- **Reconnect:** exponential backoff on connection loss, viewport re-subscribed
  on reattach.

Cross-platform notes: set `Console.OutputEncoding = UTF8` on Windows for
half-blocks; VT sequences are enabled by default on Windows 10+ and Windows
Terminal. Verify on all three before submission.

## 6. Persistence: RLE

Standard Life RLE format, so the example file opens in Golly and other existing
tools rather than being a bespoke blob.

```
#N Gosper glider gun
#CXRLE Pos=9223372036854775790,9223372036854775804
x = 36, y = 9, rule = B3/S23
24bo$22bobo$12b2o6b2o12b2o$11bo3bo4b2o12b2o$2o8bo5bo3b2o$2o8bo3bob2o4b
obo$10bo5bo7bo$11bo3bo$12b2o!
```

Origin and generation ride in **Golly's Extended RLE `#CXRLE` line**, the
established mechanism for this metadata, so other tools restore the position
rather than discarding it. `Pos` is signed and our coordinates unsigned, which
is free on a 2^64 torus: both name the same residue. See `docs/rle-format.md`
for the full tag reference and ADR 0004 for why `#O`/`#G`/`#P`/`#R` were
rejected.

A pattern straddling the 2^64 seam has an ambiguous bounding box under naive
min/max. Solved by taking the complement of the largest empty run on each axis,
which is correct in both cases.

## 7. Repository layout

```
README.md                      # 30-second clone-and-run. Kept short.
GameOfLife.sln
src/
  GameOfLife.Core/             # Universe, Cell, Viewport, RLE. Zero dependencies.
  GameOfLife.Server/           # TCP listener, framing, sim loop, fan-out
  GameOfLife.Client.Console/   # observer + editor
tests/
  GameOfLife.Core.Tests/
  GameOfLife.Protocol.Tests/
patterns/
  gosper-glider-gun.rle
  glider.rle
  blinker.rle
docs/
  ai-dlc/                      # inception, design, ADRs, AI audit log
```

`GameOfLife.Core` takes no dependencies and knows nothing about sockets — the
engine is testable without a server and the domain is not contaminated by
transport concerns.

## 8. Test plan

The tests that carry weight are the ones that prove the claims a reviewer would
otherwise have to take on faith.

### 8.1 Torus / large-universe (the load-bearing tests)
- Cell at `(0,0)` has `(ulong.MaxValue, ulong.MaxValue)` among its neighbours.
- **A glider placed at the `ulong.MaxValue` corner traverses the seam and
  arrives intact** — this is the test that proves 2^64 wrap actually works,
  and it is unreachable by any dense-grid implementation.
- Viewport containment across the seam returns the correct cell set.
- Population is preserved when a still life sits on the origin corner.

### 8.2 Rules
- Block is stable; blinker has period 2; glider translates (1,1) per 4 gens.
- Gosper gun: population grows without bound, first glider released on schedule.

### 8.3 Protocol
- Framing survives a byte stream split at every possible boundary (partial
  reads) — driven by a test that feeds one byte at a time.
- Bitmap encode/decode round-trip for random populations.
- `ulong` values above 2^53 survive JSON round-trip (regression guard for 3.3).

### 8.4 Persistence
- Gosper gun RLE parses to exactly 36 live cells.
- Round-trip: parse -> serialize -> parse yields an identical cell set.

## 9. AI-DLC execution — bolts

Each bolt is time-boxed, produces a reviewable artifact, and ends at a human
gate. Nothing advances on AI output alone.

| # | Bolt | Output | Gate |
|---|------|--------|------|
| 0 | Inception | intent, clarifying questions, requirements, stories, units of work | requirements signed off |
| 1 | Core engine | `GameOfLife.Core` + tests 8.1/8.2 green | torus test reviewed line by line |
| 2 | Persistence | RLE reader/writer, pattern files, tests 8.4 | example file opens in a third-party tool |
| 3 | Protocol | framing + codecs + tests 8.3 | partial-read test reviewed |
| 4 | Server | listener, sim loop, channels, fan-out | concurrency model walked through |
| 5 | Console client | render, input, reconnect | manual 3-client run |
| 6 | Docs / polish | README, ADRs, AI audit log | full clean-clone run on macOS + Linux + Windows |
| 7 | Stretch | WebSocket bridge + single-file HTML client | only if bolts 0-6 are done |

### 9.1 Artifacts committed under `docs/ai-dlc/`

```
00-plan.md              this document
01-questions.md         clarifying questions sent + assumptions taken pending answers
02-requirements.md      functional / non-functional, each traced to a test
03-stories.md           user stories
04-units-of-work.md     decomposition and sequencing
05-logical-design.md    domain model, concurrency model, sequence diagrams
adr/0001-sparse-universe.md
adr/0002-transport.md
adr/0003-concurrency-and-backpressure.md
adr/0004-persistence-format.md
06-ai-audit-log.md      see below
07-operations.md        run, configure, observe, known limits
```

### 9.2 The AI audit log

For a role defined as *gatekeeper of AI-assisted code*, the artifact with the
most signal is not "AI wrote this" — it is **the record of where AI output was
wrong and was rejected**. Each entry:

- what was asked for,
- what the model produced,
- what was wrong with it (with the failing test or the reasoning),
- what shipped instead.

Candidates already anticipated: dense-grid or `long`-based coordinates instead
of `ulong`; modulo arithmetic where unchecked overflow is correct and free;
`ulong` emitted as a JSON number (silent precision loss above 2^53); framing
code that assumes one `Read` returns one whole message; an unbounded per-client
queue that lets a slow observer consume server memory without limit.

## 10. Estimate

~12-14 focused hours, delivered in **4 working days** calendar.

| Bolt | Estimate |
|------|----------|
| 0 Inception | 1.5 h |
| 1 Core engine + tests | 2.5 h |
| 2 Persistence | 1.5 h |
| 3 Protocol | 2 h |
| 4 Server | 2 h |
| 5 Console client | 3 h |
| 6 Docs / cross-platform verification | 1.5 h |
| 7 Stretch (HTML) | not committed |

## 11. Open questions (sent to the requester; assumptions held meanwhile)

| # | Question | Assumption if unanswered |
|---|----------|--------------------------|
| 1 | Is 100x100 a viewport into the large universe, or a separate small mode? | Viewport with pannable `ulong` origin |
| 2 | May clients edit while the simulation is running, or only before start? | Yes, live edits — it makes the multiplayer aspect real |
| 3 | Should a saved file be resumable (generation + full state), or seed patterns only? | Resumable, via RLE comment extensions |
| 4 | Any expectation of persistence across restart, auth, or horizontal scale? | No — single in-memory process, documented as a deliberate boundary |

## 12. Explicit non-goals

Called out so the boundary reads as a decision rather than an omission:
authentication, TLS, horizontal scale, database persistence, HTTP API,
containerization, and unbounded-universe patterns that grow without limit
(the sparse set has no cap, but memory does).
