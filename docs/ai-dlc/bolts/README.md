# Bolts

One directory per bolt. Each holds a `plan.md` written to be reviewable before
work starts, and an `execution.md` recording what actually happened — including
where the two diverged.

**A bolt with a plan and no `execution.md` has not been done.** That is the
signal this structure exists to give.

## Honesty about these records

Bolts 00–08 were executed before this per-bolt structure existed, and their
plans are marked **Reconstructed** at the top. They are recovered from
`00-plan.md`, `04-units-of-work.md`, the audit log and the commits themselves —
not documents that were written in advance and then followed.

The distinction is deliberate. A planning record backdated to match its own
outcome proves nothing: every plan looks prescient once you know the answer.
Bolt 09 onward are written before execution, and Bolt 09 is currently a plan
with no execution file.

## Status

| Bolt | Unit(s) | Commits | Tests after | Plan | Done |
|------|---------|---------|-------------|------|------|
| [00 Inception](00-inception/) | — | 2 | — | reconstructed | yes |
| [01 Core engine](01-core-engine/) | U1–U3 | 1 | 28 | reconstructed | yes |
| [02 Persistence](02-persistence/) | U4 | 2 | 57 | reconstructed | yes |
| [03 Protocol](03-protocol/) | U5, U6 | 1 | 98 | reconstructed | yes |
| [04 Server](04-server/) | U7 | 1 | 126 | reconstructed | yes |
| [05 Console client](05-console-client/) | U8 | 1 | 138 | reconstructed | yes |
| [06 Docs and portability](06-docs-and-portability/) | U9 | 2 | 163 | reconstructed | yes |
| [07 Web bridge](07-web-bridge/) | U10 | 1 | 171 | reconstructed | yes |
| [08 Zoom](08-zoom/) | U11 | 1 | 189 | reconstructed | yes |
| [09 Cross-platform](09-cross-platform/) | U9 (rest) | 0 | — | **written ahead** | **no** |

## What the record is for

Reading the execution files in order shows the shape of the work rather than
its summary. Two things stand out that no single commit message conveys:

- **Every wrong test oracle was hand-authored; every externally sourced one was
  right.** Entries 1, 5 and the pattern catalogue all failed because the
  expected value came from the author's head. The R-pentomino at generation
  1,103, the RFC 6455 worked example, and the published Gosper gun all passed
  first time. Where a published figure exists, the test now uses it.
- **Both real defects lived in interactions, not in functions.** A reader
  completing its own stream and a `WhenAny` on two loops are each correct; a
  dirty flag and a drop-oldest queue are each correct. Each pair produced a bug.
  Per-file review would not have found either.
