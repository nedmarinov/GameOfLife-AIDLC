# Operations

## Running

```
dotnet run --project src/GameOfLife.Server -- --run
dotnet run --project src/GameOfLife.Client.Console
```

or open <http://localhost:5151> for the browser client, which the server serves
itself.

Server options are listed in the README. The client takes `--host` and
`--port`.

Nothing external is required: no database, no container, no configuration file.
The server is one process holding one universe in memory.

## Verified platforms

| Platform | Status |
|----------|--------|
| macOS (arm64, .NET 9.0.101) | Verified — full suite green, two-client run confirmed |
| Linux | Not yet run. No platform-specific code paths; expected to work |
| Windows | Not yet run. See the terminal note below |

**This is stated rather than claimed.** N5 asks that it run unchanged on all
three, and only one has actually been exercised.

Two places where the platforms genuinely differ, both handled:

- **UTF-8 output.** The half-block glyph is non-ASCII, so
  `Console.OutputEncoding` is set to UTF-8 before any output.
- **ANSI escape handling.** macOS and Linux terminals interpret escape
  sequences unconditionally; Windows does not, and .NET does not enable virtual
  terminal processing for you. Without it every sequence is printed literally
  and the screen fills with text like `[38;5;231m`. `Screen.Enter` enables it
  through `SetConsoleMode`, guarded by an OS check and tolerant of failure.
  Windows Terminal already enables it; the classic console host does not, which
  is exactly the case that would otherwise pass on one reviewer's machine and
  fail on another's.

Everything else — paths, sockets, threading — uses portable APIs. Paths are
built with `Path.Combine` and compared with `Path.DirectorySeparatorChar`, so
the pattern-directory containment check behaves the same on all three.

## What to look at when something is wrong

**The client shows `reconnecting...`.** The server is not running or not
reachable. The client retries with backoff up to five seconds and re-subscribes
its viewport automatically once the server returns; nothing needs restarting.

**The grid is full of text like `[38;5;231m`.** Virtual terminal processing is
off. Use Windows Terminal, or a terminal that supports ANSI.

**Cells look like blocks of the wrong shape.** The terminal font lacks U+2580
or is not being decoded as UTF-8.

**The window is cut off.** The client needs 100 columns and 53 rows. It prints
the required size on startup if the terminal is smaller.

**The browser page will not load.** The bridge serves `web/index.html`, found by
walking up from the binary. If the file is missing the bridge answers 404 rather
than failing to start, so the terminal client is unaffected. `--no-web` turns the
bridge off entirely.

**The browser connects then immediately disconnects.** Check the port in the
page's WebSocket URL matches `--web-port`; the page derives it from
`location.host`, so this only happens behind a proxy.

**The server logs `dropped N frames` on disconnect.** Working as intended: that
client could not keep up and its stale frames were discarded rather than queued.
A large number means a slow link, not a fault.

## Known limits

- **Population is bounded by host memory**, not by the universe. The sparse set
  has no fixed cap, but a pattern with unbounded growth — the shipped Gosper gun
  is one — will grow until the host runs out. There is no eviction.
- **A pan delta is a signed 64-bit value**, so a single message cannot express a
  move of more than 2^63. This is not reachable in practice: the universe wraps,
  so any destination is reachable by moving the shorter way round, which always
  fits. It is worth knowing before someone tries to compute an absolute
  difference and finds it overflows — use `subscribe` with an absolute origin
  for long jumps, which is what the client does.
- **Saving a pattern wider than `int.MaxValue`** on either axis cannot be
  expressed in an RLE header and is refused with a clear error rather than
  truncated.
- **No persistence across restart.** Deliberate; see the scope note in the
  README. State survives only through explicit save and load.

## Shutdown

Ctrl-C on either process. The server stops accepting, cancels the simulation
loop, and lets each connection flush what it has queued — up to two seconds —
before closing, so a client is never left guessing why it was hung up on.
