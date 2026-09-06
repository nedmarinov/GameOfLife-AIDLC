# Inception — Requirements

Every requirement is traceable to a test or to a named manual verification.
An untestable requirement is a requirement nobody can prove was met.

## Functional

| ID | Requirement | Verified by |
|----|-------------|-------------|
| F1 | Cells evolve by Conway's B3/S23 rules | `RulesTests` — block stable, blinker period 2 |
| F2 | The universe is 2^64 x 2^64 | `Cell` uses `ulong`; no dimension constant exists to be exceeded |
| F3 | The universe wraps in both dimensions | `TorusTests.Glider_Crossing_MaxValue_Seam_Arrives_Intact` |
| F4 | A mostly-empty universe costs memory proportional to population | `Universe` stores only live cells; `PopulationTests` |
| F5 | One server owns one simulation | single `Universe` instance owned by the sim loop |
| F6 | Multiple clients observe generation updates concurrently | manual: 3 concurrent clients, Bolt 5 |
| F7 | Any client may edit the universe through a 100x100 viewport **while paused** | manual + `ViewportTests` |
| F8 | Edits by one client are visible to all others | manual: 3-client run, Bolt 5 |
| F12 | Toggles sent while running are rejected with an explicit error, not silently dropped | `ServerPolicyTests`, Bolt 4 |
| F9 | State can be stored to disk | `RleTests` round-trip |
| F10 | State can be loaded from disk | `RleTests`; ships `patterns/gosper-glider-gun.rle` |
| F11 | Simulation can be started, paused, single-stepped, cleared, and re-speeded | manual, Bolt 5 |

## Non-functional

| ID | Requirement | Verified by |
|----|-------------|-------------|
| N1 | A slow or stalled client must not delay the simulation or other clients | bounded per-client channel, `DropOldest`; Bolt 4 review |
| N2 | A client joining mid-run becomes consistent without replay logic | frames are whole snapshots, not deltas |
| N3 | Coordinates survive the wire without precision loss | `ProtocolTests` — `ulong` above 2^53 round-trips |
| N4 | Message framing survives arbitrary TCP segmentation | `FramingTests` — stream fed one byte at a time |
| N5 | Runs on macOS, Linux and Windows with no code change | manual clean-clone run on all three, Bolt 6 |
| N6 | Runs from a clean clone with `dotnet run`, no external services | Bolt 6 |
| N7 | The domain core has zero third-party dependencies | `GameOfLife.Core.csproj` has no `PackageReference` |

## Constraint that shapes everything

A dense grid is not a slow implementation of F2 — it is an impossible one.
2^128 cells cannot be allocated, indexed, or iterated. This single fact forces
the sparse representation, and the sparse representation is what makes the
torus arithmetic (F3) collapse into unchecked integer overflow.
