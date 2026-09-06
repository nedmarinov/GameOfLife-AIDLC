# Bolt 06 — Documentation and portability · execution

**Commits**

- `1eec5dc docs: README, operations guide, and Windows terminal support`
- `1a81a65 test: catalogue of known patterns checked against published figures`

**Diff:** 9 files, +684 −12
**Tests after this bolt:** 163

## What was built

- 163 tests after the pattern catalogue was added.
- Windows virtual terminal processing enabled via `SetConsoleMode`, guarded and failure-tolerant. **No test could have caught this**: the whole suite runs on macOS.
- Added the test the README's own instructions depend on — pressing `o` to load the seam glider only works because loading moves the requesting client's window to the pattern.

## What changed from the plan

- `DllImport` rather than `LibraryImport`: the source-generated form requires `AllowUnsafeBlocks` assembly-wide, which three interop declarations do not justify.

## What was learned

- **Audit entry 6.** Windows does not interpret ANSI escapes unless VT processing is enabled, and .NET does not enable it for you. Windows Terminal does, so this is precisely the defect that works on one reviewer's machine and fails on another's — and gets reported as 'your client is broken'.
- **The pattern catalogue made a standing rule explicit.** Of eighteen entries, four failed, and every failure was hand-authored test data: the Clock had a row shifted, the LWSS travels (-2,0) not (+2,0), the MWSS and HWSS had right cell counts and wrong shapes. Everything sourced externally passed first time — R-pentomino settling at generation 1,103 with 116 cells, acorn at 5,206 with 633, diehard vanishing at 130.
- Across entries 1, 5 and this one: **every wrong oracle has been hand-authored; every externally sourced one has been right.** Where a published figure exists, the test uses it.
