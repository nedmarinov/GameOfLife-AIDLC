# Bolt 09 — Cross-platform verification · plan

> **Written before execution.** There is no `execution.md` in this directory
> because this bolt has not been run. That absence is the point: it is what the
> per-bolt record is for.

**Unit(s):** U9 (the outstanding half)
**Status:** in progress — CI added, awaiting its first run

> **Plan revised mid-bolt.** The original plan put CI out of scope, on the
> grounds that a one-off manual run answers the question and wiring up runners
> does not. That reasoning assumed a Windows machine was reachable. It is not —
> the author has no Windows hardware — so CI stops being scope creep and becomes
> the only route to evidence. The revision is recorded rather than silently
> applied, because a plan quietly edited to match what became convenient is
> worth nothing.

## Goal

Turn requirement N5 from *reasoned* into *observed*. The clients have been run
on macOS only. Every other platform claim in this repository rests on the
absence of platform-specific code, which is an argument, not evidence — and
Bolt 06 already found a Windows defect by inspection that no test could catch.

## In scope

- Clean clone and `dotnet test` on Linux.
- Clean clone and `dotnet test` on Windows.
- Run the server and terminal client on each, and confirm by eye:
  - the grid renders as blocks, not as literal escape text;
  - half-blocks display (the terminal font has U+2580 and decodes UTF-8);
  - arrow keys, space, and the zoom keys register.
- Open the browser client on each and confirm it connects and renders.
- Confirm a terminal client on one machine and a browser on another see the
  same universe, if two machines are available.

## Out of scope

- Any platform beyond the three named in N5.
- Visual confirmation on Windows. See the limits section below — this is the
  part CI provably cannot close.

## Risks going in

- **Windows console host rather than Windows Terminal.** The `SetConsoleMode`
  call in `Screen.Enter` is the untested mitigation. If it fails, the symptom is
  a screen full of `[38;5;231m` rather than a grid.
- **Terminal font lacking U+2580.** Symptom is boxes or the wrong glyph shape;
  not a code defect, but it needs saying in the operations guide if it happens.
- **Terminal smaller than 100x53.** The client already warns; confirm the
  warning is legible rather than clipped.
- **Linux terminals with no 256-colour support.** Colour escapes would be
  ignored or printed. Not seen on any modern terminal, but unverified.

## What CI can and cannot prove

Being precise about this matters, because a green badge invites the reader to
assume more than it earns.

**It can prove**, on all three platforms:

- the solution builds in Release with no platform-specific compilation problem;
- all tests pass — sockets, threading, framing, file paths, torus arithmetic;
- the server binds, seeds from `patterns/`, and ticks;
- a client connects, decodes frames, and advances the generation counter;
- **the half-block glyph survives the platform's console encoding** — if
  Windows mangled UTF-8 output the smoke test's `grep` for `▀` would fail;
- the browser bridge serves its page and completes an RFC 6455 handshake,
  checked against the specification's published accept value.

**It cannot prove** that a real terminal *displays* the escapes correctly. CI
captures stdout to a file, so the ANSI sequences are recorded rather than
interpreted, and no human sees the result. The `SetConsoleMode` call in
`Screen.Enter` — the mitigation for the Bolt 06 defect — therefore remains
**untested against an interactive Windows console**. A pipe cannot tell you
whether a grid looked like a grid.

That residual gap needs one person, one Windows Terminal, thirty seconds.

## Done when

- The `build` workflow is green on ubuntu, windows and macos runners.
- The smoke job passes on all three, so a server and client demonstrably run.
- `docs/ai-dlc/07-operations.md` and requirement N5 are updated to say
  *verified by CI*, with the visual gap named — not upgraded to a bare
  *verified* that the evidence does not support.
- The visual check on an interactive Windows console remains open until
  somebody with the hardware does it.

## Note on how this ends

If something fails, the honest outcome is a defect and a fix, not a caveat
quietly added to the README. If nothing fails, the outcome is two table cells
changing from "not yet run" to "verified", which is a smaller change than it
looks: it converts an argument into evidence, and that is the whole reason this
bolt exists.
