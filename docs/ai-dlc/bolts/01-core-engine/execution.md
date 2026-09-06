# Bolt 01 — Core engine · execution

**Commits**

- `7168d6a feat(core): sparse toroidal universe with B3/S23 step`

**Diff:** 11 files, +927
**Tests after this bolt:** 28

## What was built

- 28 tests, all green. Wrap implemented as unchecked `ulong` overflow: no modulo, no bounds check, no dimension constant anywhere.
- Step is O(population x 8) with reused buffers, so a steady population allocates nothing in the steady state.
- Guards verified mechanically: 0 package references, no `%` on coordinates, `ulong` the only coordinate type.

## What changed from the plan

_None._

## What was learned

- **Audit entry 1.** The seam test failed and the *test* was wrong, not the engine: it asserted the glider's placement origin was alive, but the glider has no cell at offset (0,0). The engine had produced exactly the right answer.
- What caught it was that the same test also asserted the whole live set independently, so the two assertions contradicted each other rather than both accusing the engine. A single-assertion test would have sent me to 'fix' correct wrap arithmetic.
