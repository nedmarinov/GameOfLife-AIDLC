# Inception — User Stories

## Observer

**As** someone watching the simulation,
**I want** to connect to a running server and immediately see the current
generation, **so that** I do not have to wait for or coordinate a restart.
- Joining mid-run shows correct state within one tick.
- The generation counter and population are visible.
- Disconnecting does not disturb other clients or the simulation.

## Editor

**As** someone setting up a pattern,
**I want** to move a cursor over a 100x100 window and toggle cells,
**so that** I can compose an initial state without editing a file.
- Arrow keys move the cursor; space toggles.
- The toggle is applied by the server and appears on every connected client.
- Editing works while paused and while running.

## Navigator

**As** someone exploring a 2^64 universe,
**I want** to pan the 100x100 window,
**so that** I can look at regions far from where I started.
- Panning changes the viewport origin without disturbing the simulation.
- Panning across the 2^64 seam is seamless and requires no special input.

## Operator

**As** the person running the server,
**I want** to seed the universe from a pattern file and save it back,
**so that** interesting states survive a restart.
- `--pattern patterns/gosper-glider-gun.rle` seeds at startup.
- Saved files reopen in third-party Life tools.
- Saving captures the generation counter, so a run resumes where it stopped.

## Reviewer

**As** the person assessing this submission,
**I want** to clone and run it in under a minute,
**so that** I can evaluate behaviour rather than fight the build.
- `dotnet run` for server and client; no external services, no containers.
- README shows the three-client demo in a single short sequence.
