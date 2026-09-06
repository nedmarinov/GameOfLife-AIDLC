# ADR 0002 — Raw TCP with length-prefixed framing

**Status:** Accepted · **Date:** 2026-09-07

## Context

One long-lived server pushes generation updates to many persistent clients.
The brief leaves protocol choice explicitly open. The traffic shape is
server-initiated broadcast at a fixed tick rate, not request/response.

## Decision

Raw TCP via `TcpListener`, with a custom length-prefixed frame format read
using `System.IO.Pipelines`.

```
+---------+--------+----------------------+
| len:u32 | type:u8|  payload (len-1)     |   little-endian; len covers type+payload
+---------+--------+----------------------+

type 0x01  UTF-8 JSON control message   (both directions)
type 0x02  binary viewport bitmap        (server -> client)
```

## Consequences

- Message boundaries, partial reads, backpressure and half-open detection are
  owned explicitly rather than delegated. These are the parts that are actually
  hard, and they are where the tests are pointed (N4).
- `System.IO.Pipelines` supplies buffer management, so the classic hand-rolled
  ring-buffer and reassembly defects are avoided without giving up control of
  the wire format.
- A browser cannot connect directly. Accepted: the sanctioned UI is console
  based. A WebSocket bridge is scoped as stretch work (Bolt 7).
- The wire is inspectable — control messages are readable JSON, so a reviewer
  can follow a session with `tcpdump` without a decoder.

## Alternatives rejected

**WebSocket.** The pragmatic default and genuinely simpler; ASP.NET Core would
supply framing free. Rejected because it hides exactly the layer this design
wants to own, and it drags in an HTTP server and its handshake to carry traffic
that is never request/response. Its one real advantage — browser reach — is
recoverable later through a bridge, and applies to a UI the brief already made
optional.

**SignalR.** Adds hub abstractions, a negotiation step and transport
fallbacks over a problem that has one transport and one message shape.
Rejected as weight without benefit.

**gRPC server streaming.** A strong fit on paper: streaming is native and
codegen removes hand-written codecs. Rejected because `.proto` tooling and
generated sources work against "simple, minimalist solution we can run", and
the generated stack would hide the framing behaviour the tests exist to prove.

**UDP / multicast.** Attractive for fan-out broadcast, and frames are
idempotent snapshots so loss is tolerable. Rejected: reliable delivery of
control messages and edits would have to be rebuilt on top, trading a solved
problem for an unsolved one.
