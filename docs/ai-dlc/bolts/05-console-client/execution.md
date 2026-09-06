# Bolt 05 — Console client · execution

**Commits**

- `951f290 feat(client): console client with half-block rendering`

**Diff:** 12 files, +1000 −3
**Tests after this bolt:** 138

## What was built

- 138 tests. `Compose` is separated from `Draw` so the packing is asserted without a terminal.
- **Verified end to end**: two clients run concurrently against one seeded server reported identical generations 0..206 and population climbing from 36 as the gun emitted gliders, over 290 frames each.
- `Console.KeyAvailable` throws on redirected stdin; that is now a supported pure-observer mode, which is what made the two-client verification possible.

## What changed from the plan

- Added `--run` to the server so the demo is a single command.

## What was learned

- **Audit entry 5 — a wrong oracle again, same cause as entry 1.** The seam test parked the cursor at (0,0) as 'outside the window', but that window starts at `MaxValue-9` and wraps, so (0,0) is *inside* it at local (10,10) — exactly the cell asserted.
- On a torus, 'far away' and 'outside' stop meaning what they look like. Both wrong oracles came from reasoning about wrapped coordinates in my head instead of on the page.
