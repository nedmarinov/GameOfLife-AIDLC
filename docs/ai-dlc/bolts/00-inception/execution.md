# Bolt 00 — Inception · execution

**Commits**

- `a764aa2 docs: add AI-DLC implementation plan`
- `c3787aa docs: complete Bolt 0 inception artifacts`

**Diff:** 12 files, +899
**Tests after this bolt:** none (no code)

## What was built

- Nine artifacts written: intent, questions, requirements, stories, units of work, logical design, and four ADRs.
- Four clarifying questions raised with the cost of each assumption being wrong, rather than resolved silently.
- Eight anticipated AI failure modes (A1–A8) recorded **before** construction, each with the guard that would catch it.

## What changed from the plan

- The transport recommendation changed from WebSocket to raw TCP during planning. The traffic is server-initiated broadcast, not request/response, and delegating the framing would have removed the part of the problem most worth owning. WebSocket is kept in ADR 0002 as a documented alternative rather than deleted.

## What was learned

- Requiring each requirement to name its test killed two vague ones at the point of writing.
- Writing the rejected alternatives down forced the transport reconsideration; listing only the decision would have hidden it.
