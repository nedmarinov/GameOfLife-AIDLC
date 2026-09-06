# Bolt 08 — Zoom · execution

**Commits**

- `a32142b feat: zoom out to the whole 2^64 universe`

**Diff:** 16 files, +625 −41
**Tests after this bolt:** 189

## What was built

- 189 tests. At maximum zoom each displayed cell stands for 2^57 universe cells and the whole universe fits in one 100x100 window.
- **Rendering needed no change at all** — the renderer walks live cells rather than the window, so several cells landing on one bit simply set it twice.
- `MaxZoomFor` derives the limit from the width's bit length; the constructor refuses to exceed it.

## What changed from the plan

- The viewport frame header grew from 28 to 30 bytes, so a 100x100 frame is 1,280 rather than 1,278. Protocol version bumped to 2.

## What was learned

- **Audit entry 7, and the second interaction defect.** The tick loop broadcast only when a dirty flag was set — sound for an idle universe. Egress queues drop the oldest frame when full — sound, because a newer one is always coming.
- Together they produced a state where **the newer frame never came**: a client falling behind while paused would hold a stale frame forever. Fixed with a one-second broadcast floor, now requirement N10.
- It surfaced as a test *timeout*, which reads like a slow test rather than a defect.
- Both real bugs in this project (entries 4 and 7) lived in an interaction between two individually correct decisions, not in any single function.
