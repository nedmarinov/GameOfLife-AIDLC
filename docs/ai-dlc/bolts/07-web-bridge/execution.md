# Bolt 07 — Web bridge · execution

**Commits**

- `3abc5d3 feat(web): WebSocket bridge and browser client`

**Diff:** 12 files, +973 −29
**Tests after this bolt:** 171

## What was built

- 171 tests. The browser receives the *identical* frames a native client receives, inside WebSocket messages — four redundant bytes per message in exchange for one specification.
- After the `Stream` generalisation the server cannot tell the transports apart, so every existing policy applies to browsers for free.
- The page uses `BigInt` throughout: a JavaScript number loses precision above 2^53, the same defect as A9 in another language.

## What changed from the plan

_None._

## What was learned

- **No defects.** Recorded because it went in cleanly for a reason: it reused decisions already made rather than making new ones, including the stream-ownership question that produced entry 4.
- The handshake test uses RFC 6455's own numbers, and `curl` confirmed the same accept value from a running server. Integration tests drive it with `ClientWebSocket`, so an independent RFC implementation validates ours.
