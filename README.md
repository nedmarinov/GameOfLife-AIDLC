# Multiplayer Conway's Game of Life

A single server simulates a **2^64 x 2^64** toroidal universe. Any number of
clients connect over TCP and watch the same generations tick.

```
dotnet run --project src/GameOfLife.Server -- --run
```

then either open **<http://localhost:5151>** in a browser, or run the terminal
client in one or more other terminals:

```
dotnet run --project src/GameOfLife.Client.Console
```

You should see Gosper's glider gun firing. Open more of either kind and they all
show the same thing, in step — a browser tab and a terminal are the same client
to the server. Nothing else to install: .NET 9, and a terminal or a browser.

## The console client

```
arrows        move the cursor (the window follows at the edge)
shift+arrows  pan the window by half a screen
space         toggle a cell (while paused)
s             start / pause
n             single step (while paused)
+ / -         faster / slower
z / x         zoom out / in
Z             zoom all the way out — the whole universe in one window
c             clear
o / w         load / save a pattern file
g             jump back to the centre of the universe
q             quit
```

The window is 100x100 cells drawn in 100x50 characters: the half-block glyph
`▀` carries two cells, its foreground colour painting the upper one and its
background the lower.

## The browser client

Served by the server itself at <http://localhost:5151> — no build step, no
bundler, one static file. Click to toggle cells while paused, drag to paint,
arrow keys to pan, scroll wheel to zoom.

It speaks **the same protocol as the terminal client**, byte for byte: the
length-prefixed frames simply travel inside WebSocket messages instead of
directly over TCP. One specification, two transports. Coordinates use `BigInt`,
because a JavaScript number loses precision above 2^53 and the universe runs to
2^64 — the same reason the JSON messages carry them as strings.

Run the server with `--no-web` to disable it.

## Try this

Start the server paused, draw something, then run it:

```
dotnet run --project src/GameOfLife.Server -- --empty
dotnet run --project src/GameOfLife.Client.Console     # in two terminals
```

Draw cells in one client with the arrow keys and space. **They appear in the
other one**, because the server owns the universe and every client is watching
it rather than running its own copy. Press `s` in either to start.

Mix the two: draw in a browser tab and watch it appear in a terminal. The server
does not distinguish them.

To watch the torus actually wrap, load the glider that ships positioned two
cells before the edge of the universe — press `o`, then type `glider.rle`. It
crosses `ulong.MaxValue` and arrives at the far side within four generations.

## Running the tests

```
dotnet test
```

189 tests. The ones worth reading are in
[TorusTests.cs](tests/GameOfLife.Core.Tests/TorusTests.cs) — a glider crossing
the `ulong.MaxValue` seam in both dimensions, and one wrapping in a single
dimension, which is where a hand-written modulo implementation typically breaks.
Neither can pass on a dense grid or with signed coordinates.

## What patterns work

All of them. The engine implements the B3/S23 rule rather than a catalogue of
shapes, so every Conway pattern behaves as it does anywhere else — and
[PatternCatalogueTests.cs](tests/GameOfLife.Core.Tests/PatternCatalogueTests.cs)
checks that against published figures rather than taking it on trust: still
lifes, the pulsar at period 3, the pentadecathlon at period 15, the *WSS
spaceships at c/2, the R-pentomino settling at generation 1,103 with exactly 116
cells, the acorn at 5,206 with 633, and the diehard vanishing on generation 130.
Each of those is then re-run straddling the 2^64 seam and required to produce an
identical shape.

Not supported, deliberately: rules other than Conway's. Loading a HighLife
(`B36/S23`) file is refused rather than quietly simulated under the wrong rule.

## How it works

**The universe is the set of live cells**, not a grid. A 2^64 x 2^64 grid is not
slow to allocate, it is impossible — 2^128 cells. Memory and per-generation cost
are proportional to population, so the empty remainder of the universe costs
nothing.

**Wrapping is not implemented, it is inherited.** Each dimension is exactly 2^64
and coordinates are `ulong`, so stepping off an edge *is* unchecked integer
overflow: `ulong.MaxValue + 1 == 0`. There is no modulo, no bounds check, and no
seam special case anywhere in the code.

**The 100x100 UI is a viewport**, with a `ulong` origin you can pan anywhere.
Deciding whether a cell is visible is one wrapping subtraction and one unsigned
comparison, which is why a window straddling the seam needs no special handling.

**Zoom is a power of two**, so mapping a cell to its displayed block is a shift
rather than a division — exact, with no rounding that could put a cell in the
wrong block. At maximum zoom each displayed cell stands for 2^57 universe cells
and the entire 2^64 × 2^64 universe fits in the window, which is the only direct
way to see that it really is that large. Rendering needs no special case for it:
the renderer walks live cells rather than the window, so several cells landing on
one bit simply set it twice.

**One thread owns the universe.** Connections post commands to a single channel
and that thread drains them between ticks, so mutual exclusion is structural —
there is no lock to forget to take. Each client has a two-frame outbound queue
that drops the oldest when full, so a slow client degrades to a lower frame rate
and cannot stall the simulation or anyone else. Frames are whole snapshots
rather than deltas, which is what makes dropping one correct, and what lets a
client joining mid-run become consistent with no replay logic.

**Transport is raw TCP** with a length-prefixed binary framing protocol on
`System.IO.Pipelines`. Control messages are JSON, so the wire is readable with
`tcpdump` and no decoder; the per-generation frame is a fixed 1,280 bytes for a
100x100 window whatever the population. Browsers get the identical frames
inside WebSocket messages: the RFC 6455 handshake is done by hand, the framing
is left to the runtime, and a connection is the same object to the server
either way.

## Layout

```
src/GameOfLife.Core             universe, torus arithmetic, viewport, RLE. No dependencies.
src/GameOfLife.Protocol         framing and message codecs. Knows bytes, not sockets.
src/GameOfLife.Server           TCP listener, simulation loop, client fan-out
src/GameOfLife.Client.Console   terminal observer and editor
web/index.html                  browser client, served by the server
patterns/                       Gosper glider gun and friends, in standard RLE
docs/                           design decisions, and how this was built
```

## Files

Patterns are standard Life **RLE**, so `patterns/gosper-glider-gun.rle` opens in
Golly and other existing tools rather than being a format only this project
understands. Absolute position and generation ride in Golly's Extended RLE
`#CXRLE` line, which those tools restore rather than discard.
[docs/rle-format.md](docs/rle-format.md) documents every tag and where each
meaning came from.

Load and save are confined to the `patterns/` directory. The file name comes
from a network peer, so it is resolved and checked to be inside that root
before anything touches the filesystem.

## Server options

```
--port <n>        listen port (default 5150)
--tick <ms>       generation interval (default 100)
--patterns <dir>  directory for load/save (default ./patterns)
--pattern <file>  seed pattern (default gosper-glider-gun.rle)
--empty           start with an empty universe
--run             begin ticking immediately
--web-port <n>    browser client port (default 5151)
--no-web          do not serve the browser client
```

## Design notes

This was built with AI agents, and the process is part of the deliverable.
[docs/ai-dlc/](docs/ai-dlc/) holds the requirements, the decision records with
their rejected alternatives, a
[plan and execution record for every bolt](docs/ai-dlc/bolts/), and
[an audit log](docs/ai-dlc/06-ai-audit-log.md) recording where AI output was
**wrong and was rejected** — a file format tag that meant something else, a
socket closed out from under its own writer, and two test oracles that were
wrong about wrapped coordinates while the code was right.

## Scope

Deliberately not included: authentication, TLS, persistence across restart,
horizontal scale, and an HTTP API. The server is one in-memory process; state
survives only through explicit save and load.
