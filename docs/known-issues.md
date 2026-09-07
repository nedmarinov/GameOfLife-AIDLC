# Known issues

Found by a deliberate review pass before publication, then triaged. Nine issues;
two were fixed, seven were judged not worth fixing and are recorded here rather
than left for someone else to discover.

**None of the seven is reachable from the demo path, and none is remotely
exploitable.** Each needs an unusual trigger, described below so the judgement
can be checked rather than taken on trust.

Worth stating plainly: **the 192 tests found none of these.** Six live in error
paths, shutdown paths, or interactions between features — the same shape as both
real defects already recorded in the audit log. That is the honest limit of the
suite.

---

## Fixed

### 1. Symlink escape from the pattern directory — *fixed*

`PatternStore.Resolve` combined the request with the root and normalised it with
`Path.GetFullPath`, then checked containment. `GetFullPath` is **purely
textual** and does not follow symbolic links, so a link inside `patterns/`
pointing elsewhere produced a path that passed the check while reading or
writing a file outside the root. Demonstrated experimentally before fixing.

The comment claimed the check "rejects traversal, absolute paths, and symlink
escapes together". The first two were true. The third was not, and the audit log
repeated the claim.

Now checked twice: textually as before, then physically, resolving every
component through its links and requiring the result to stay under the resolved
root. Regression tests cover both an escaping link and a legitimate link that
stays inside — containment is the rule, not a ban on links.

Not remotely exploitable either way: creating a symlink needs local filesystem
access, which the protocol does not offer.

### 2. Loading a pattern while zoomed out put it in the corner — *fixed*

The window was recentred by subtracting half its width in *displayed* cells,
ignoring zoom. At zoom 8 a loaded pattern landed about twelve thousand cells
from where it belonged, appearing jammed against the top-left corner. Visible,
which is why no test caught it — it was wrong, not absent.

Now scaled by `CoverageWidth`, which is the same quantity already multiplied by
the zoom.

---

## Not fixed

### 3. An empty WebSocket message closes the connection

`WebSocketStream.ReadAsync` returns `result.Count`, so a zero-length binary
message returns `0` — which `PipeReader` reads as end-of-stream, dropping the
client.

*Why not fixed:* no client in this repository sends empty binary messages, and
browsers do not generate them spontaneously. Correct fix is to loop until a
non-empty message or a genuine close arrives, which adds a loop to a path that
is currently three lines.

### 4. The dropped-frame counter over-reports

`Publish` checks `Reader.Count >= EgressCapacity` *after* writing. A queue
holding one frame that grows to two evicted nothing, yet counts a drop.

*Why not fixed:* the counter is diagnostic, appearing only in a disconnect log
line. Counting accurately means having the channel report evictions, which it
does not expose — the alternative is tracking depth manually and duplicating
what the channel already knows.

### 5. Background tasks can die silently

The console client starts its connection loop and key reader as `_ = Task(...)`.
Their catch filters name `SocketException`, `IOException` and
`ObjectDisposedException`. Anything else terminates the task unobserved, and the
UI keeps reporting "reconnecting…" forever.

*Why not fixed:* no such exception has been observed, and the honest fix is a
catch-all plus a visible error state — a small feature rather than a patch. This
is the one most worth revisiting if the client is ever used in earnest.

### 6. Shutdown can strand a connection task

`GameServer.ServeAsync` posts `ClientDisconnected` with
`CancellationToken.None`. During shutdown the simulation loop has stopped
reading, so if the 4096-deep command channel were full, that write would wait
forever.

*Why not fixed:* it needs a full channel at the instant of shutdown. The task is
fire-and-forget, so it cannot block process exit — it would leak a task in a
process that is ending anyway.

### 7. Default ports can collide

`--port 5151` collides with the default `--web-port` of 5151, and the second
listener fails to bind.

*Why not fixed:* the failure is immediate and loud. A check comparing the two
would be clearer, but nothing is silently wrong.

### 8. Bytes after an HTTP request head are discarded

`WebBridge.ReadRequestAsync` returns once it sees the blank line and drops
anything already buffered past it. A client that pipelined data immediately
after its upgrade request would lose it.

*Why not fixed:* browsers wait for the 101 response before sending frames. Only
a hand-written client that pipelined aggressively would notice.

### 9. Path comparison and filesystem case

Containment compares ordinally on Linux and case-insensitively elsewhere,
matching each platform's filesystem. A case-insensitive Linux mount, or a
case-sensitive volume on macOS, would disagree with that assumption.

*Why not fixed:* the mismatch is fail-closed — it rejects a legitimate path
rather than accepting a hostile one. Detecting the actual behaviour of the
mounted filesystem is more machinery than the risk warrants.
