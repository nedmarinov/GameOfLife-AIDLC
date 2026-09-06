# Bolt 09 — Cross-platform verification · plan

> **Written before execution.** There is no `execution.md` in this directory
> because this bolt has not been run. That absence is the point: it is what the
> per-bolt record is for.

**Unit(s):** U9 (the outstanding half)
**Status:** not started — requires machines not available to the agent

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

- CI. A one-off manual verification answers the question; wiring up runners
  does not, and would be scope the brief never asked for.
- Any platform beyond the three named in N5.

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

## Done when

- `dotnet test` is green on all three platforms, from a clean clone, with no
  code changes.
- A screenshot or a written confirmation exists for the terminal client on
  Linux and on Windows.
- `docs/ai-dlc/07-operations.md` and requirement N5 are updated to say
  *verified* — or to record precisely what failed and on what.

## Note on how this ends

If something fails, the honest outcome is a defect and a fix, not a caveat
quietly added to the README. If nothing fails, the outcome is two table cells
changing from "not yet run" to "verified", which is a smaller change than it
looks: it converts an argument into evidence, and that is the whole reason this
bolt exists.
