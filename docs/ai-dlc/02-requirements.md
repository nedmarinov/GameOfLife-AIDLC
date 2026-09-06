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
| F5 | One server owns one simulation | `ServerIntegrationTests.The_Simulation_Advances_For_Every_Client_At_Once` |
| F6 | Multiple clients observe generation updates concurrently | `The_Simulation_Advances_For_Every_Client_At_Once` (3 concurrent clients) |
| F7 | Any client may edit the universe through a 100x100 viewport **while paused** | manual + `ViewportTests` |
| F8 | Edits by one client are visible to all others | `An_Edit_By_One_Client_Reaches_The_Others` |
| F12 | Toggles sent while running are rejected with an explicit error, not silently dropped | `Editing_While_Running_Is_Refused_With_A_Reason` |
| F9 | State can be stored to disk | `RleTests` round-trip |
| F10 | State can be loaded from disk | `RleTests`; ships `patterns/gosper-glider-gun.rle` |
| F11 | Simulation can be started, paused, single-stepped, cleared, and re-speeded | manual, Bolt 5 |

## Non-functional

| ID | Requirement | Verified by |
|----|-------------|-------------|
| N1 | A slow or stalled client must not delay the simulation or other clients | bounded `DropOldest` channel; `One_Client_Disconnecting_Does_Not_Disturb_The_Others` |
| N2 | A client joining mid-run becomes consistent without replay logic | `A_Client_Joining_Mid_Run_Sees_Current_State_Without_Replay` |
| N3 | Coordinates survive the wire without precision loss | `MessageTests.Coordinates_Above_Two_To_The_Fifty_Three_Survive_A_Round_Trip` |
| N4 | Message framing survives arbitrary TCP segmentation | `FramingTests.Reader_Survives_A_Stream_Delivered_One_Byte_At_A_Time` |
| N5 | Runs on macOS, Linux and Windows with no code change | manual clean-clone run on all three, Bolt 6 |
| N6 | Runs from a clean clone with `dotnet run`, no external services | Bolt 6 |
| N7 | The domain core has zero third-party dependencies | `GameOfLife.Core.csproj` has no `PackageReference` |

| N8 | A hostile peer cannot make the server buffer without bound | `FramingTests.Rejects_An_Absurd_Declared_Length_Without_Buffering_It` |
| N9 | A client cannot read or write files outside the pattern directory | `PatternStoreTests`; `Loading_Outside_The_Pattern_Directory_Is_Refused` |

## Constraint that shapes everything

A dense grid is not a slow implementation of F2 — it is an impossible one.
2^128 cells cannot be allocated, indexed, or iterated. This single fact forces
the sparse representation, and the sparse representation is what makes the
torus arithmetic (F3) collapse into unchecked integer overflow.
