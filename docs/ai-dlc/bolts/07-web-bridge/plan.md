# Bolt 07 — Web bridge · plan

> **Reconstructed.** This bolt was executed before the per-bolt record existed.
> The plan below is recovered from `00-plan.md`, `04-units-of-work.md` and the
> commit itself; it is not a document that was written in advance and then
> followed. Bolt 9 onward are planned before execution. Distinguishing the two
> matters: a planning record backdated to match the outcome proves nothing.

**Unit(s):** U10
**Status:** complete

## Goal

Let a browser join the same simulation as a terminal, without a second protocol.

## In scope

- RFC 6455 handshake by hand; framing left to the runtime.
- A single static HTML page, served by the server itself.
- `ClientConnection` generalised from `TcpClient` to `Stream`.

## Out of scope

- Any build step, bundler, or web framework.

## Risks going in

- A second protocol dialect for browsers, doubling the specification.

## Done when

- A browser edit reaches a native client and vice versa.
- The browser is subject to the same edit policy without new code.
- The handshake matches the RFC's published worked example.
