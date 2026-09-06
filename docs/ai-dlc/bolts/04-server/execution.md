# Bolt 04 — Server · execution

**Commits**

- `267b663 feat(server): TCP server, single-writer simulation, bounded egress`

**Diff:** 14 files, +1494 −6
**Tests after this bolt:** 126

## What was built

- 126 tests total. Connect and disconnect are commands too, so the client list needs no synchronisation either.
- Rendering and framing happen once per *distinct* viewport, so ten clients on one window cost one render.
- A10 guarded proactively: paths are resolved and required to stay under the root, which rejects traversal, absolute paths and symlink escapes together. The boundary compares against root-plus-separator, so `/data-evil` is refused against `/data`.

## What changed from the plan

_None._

## What was learned

- **Audit entry 4, found in two layers.** A test passed alone and failed in the full suite. The server queued the `ErrorMessage` explaining a protocol violation, then cancelled the write loop before it could be sent.
- Reordering shutdown did not fix it. The root cause was one layer below: `PipeReader.Create(Stream)` defaults to `leaveOpen: false`, so the read loop's own `CompleteAsync` disposed the socket out from under the writer.
- Every piece was individually defensible — completing your own reader is hygiene, `WhenAny` on two loops is normal, a bounded queue is right. **The defect lived in their interaction**, and only appeared with two concurrent connections. A green run now means five green runs.
