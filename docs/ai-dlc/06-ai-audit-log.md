# AI Audit Log

This project was built with AI agents. The interesting record is not where the
model helped — it is **where its output was wrong and what caught it.** Each
entry states what was asked, what came back, what was wrong, and what shipped.

Entries are added as they occur, in bolt order.

## Anticipated failure modes

Recorded before construction so they can be checked for rather than discovered.
Each is a defect this problem is known to invite, and each has a guard.

| # | Expected failure | Guard |
|---|------------------|-------|
| A1 | Dense grid or a bounded `width`/`height` field, silently capping the universe | `Universe` exposes no dimension constant; `TorusTests` at `ulong.MaxValue` |
| A2 | `long` or `int` coordinates instead of `ulong` | seam tests only pass with unsigned wraparound |
| A3 | Modulo arithmetic for wrap where unchecked overflow is free and exact | code review; `%` on a coordinate is a red flag |
| A4 | `ulong` serialized as a JSON number, losing precision above 2^53 | `ProtocolTests` round-trips `ulong.MaxValue - 1` |
| A5 | Framing that assumes one `Read` yields one whole message | `FramingTests` feeds the stream one byte at a time |
| A6 | Unbounded per-client queue — a stalled reader becomes a memory leak | bounded channel, capacity 2, `DropOldest` (ADR 0003) |
| A7 | `lock` sprinkled through `Universe` instead of single-writer ownership | `Universe` contains no synchronisation primitives at all |
| A8 | Neighbour counting that visits dead space, reintroducing O(universe) | step iterates live cells only; complexity argued in 05-logical-design |
| A9 | Trusting an attacker-controlled length prefix, so five bytes can make the server buffer gigabytes | `FrameCodec.MaxPayloadLength`; `Rejects_An_Absurd_Declared_Length_Without_Buffering_It` |
| A10 | Passing a client-supplied file name to the filesystem, allowing path traversal reads and writes | `PatternStore` resolves and bounds every path; `PatternStoreTests` |

## Log

### Bolt 0 — Inception

No code produced. The design decisions in ADRs 0001-0004 were reached by
stating the alternative first and requiring an argument to reject it; the
rejected options are recorded in each ADR rather than discarded, so a reviewer
can check the reasoning rather than the conclusion.

One correction already applied at plan stage: the initial transport
recommendation was WebSocket, chosen for implementation speed. It was replaced
by raw TCP after re-reading the brief — the traffic is server-initiated
broadcast, not request/response, and delegating the framing would have removed
the part of the problem most worth owning and testing. Recorded in ADR 0002
with WebSocket kept as a documented alternative rather than deleted.

### Bolt 1 — Core engine

**Entry 1 — a wrong test that looked like an engine bug.**

*Asked for:* a test proving a glider crosses the `ulong.MaxValue` seam intact.

*Produced:* the test ran the glider three diagonal steps from
`(MaxValue-1, MaxValue-1)`, compared the whole live set at each step, and then
added a final check that `(1, 1)` — the glider's wrapped placement origin — was
alive.

*What was wrong:* the glider's five offsets are `(1,0) (2,1) (0,2) (1,2) (2,2)`.
There is no cell at offset `(0,0)`, so the placement origin is never itself
alive. The engine was correct; the assertion was not. The observed live set,
`(2,1) (2,3) (3,3) (3,2) (1,3)`, is exactly the glider at origin `(1,1)` — the
set-equality assertions had already passed, which is what made the diagnosis
quick.

*Why it matters:* this is the dangerous class of AI defect. A wrong assertion in
a test named "proves the torus works" fails in a way that looks like an engine
bug, and the obvious response — adjusting the wrap arithmetic until the test
goes green — would have introduced a real defect into working code to satisfy a
broken oracle. The guard is that the test asserted the *whole* live set
independently, so the two assertions disagreed with each other rather than both
pointing at the engine.

*Shipped instead:* the final check now asserts every live cell has coordinates
below 10, which states the actual claim — cells this low are only reachable
from a start at `MaxValue - 1` by wrapping — rather than a coincidental
consequence of it.

*Guard added:* none needed; A2 already covered the engine behaviour. The lesson
is recorded rather than automated: when an AI-written test fails, establish
whether the oracle or the subject is wrong before changing either.

### Bolt 2 — Persistence

**Entry 2 — a plausible-looking file format tag that means something else.**

*Asked for:* an RLE extension carrying the absolute `ulong` origin and the
generation counter, without breaking compatibility with third-party tools.

*Produced:* `#O <x> <y>` for the origin and `#G <n>` for the generation,
described in ADR 0004 as "legal RLE comment lines".

*What was wrong:* in the RLE format `#O` is already defined — it means **the
author's name and creation date**. `#G` is not a defined tag at all. Golly and
similar tools would have parsed our coordinate pair as the pattern's author and
displayed it as such. The claim "other tools ignore these" was simply false for
`#O`, and the design would have shipped a file that looked correct in our own
reader and wrong in everyone else's.

*Why it matters:* this is the failure mode that survives testing. A round-trip
test against our own parser passes perfectly, because our reader and writer
agree with each other. Nothing inside the project can detect that the agreement
is with a misreading of the spec. The whole reason ADR 0004 chose RLE was
interoperability, and the extension quietly undermined the property it was
chosen for.

*Shipped instead:* initially tagged free-text comments — `#C origin:` and
`#C generation:`. That fixed the collision but was itself superseded; see
entry 3.

*Guard added:* `Writer_Reproduces_The_Canonical_Published_Gun_Body` asserts the
writer's output matches the Gosper gun exactly as published, wrap position
included. Round-tripping through our own parser could never have caught the
`#O` defect — a reader and writer that share a misreading agree with each other
perfectly. The acceptance criterion for U4 is now an oracle outside this
project, not self-consistency.


**Entry 3 — inventing a convention that already existed.**

*Found by:* being asked to document every RLE tag and say where each meaning
came from. The request was the guard. Nothing in the code or the tests could
have surfaced this.

*What the documentation pass turned up:* the fix in entry 2 — carrying position
and generation in `#C origin:` / `#C generation:` comments — was a private
convention for a problem the format had already solved. Golly's **Extended RLE**
defines `#CXRLE Pos=x,y Gen=n` for precisely this, and Golly *restores* those
values on load. Our version would have been silently discarded by every tool
that read it, while looking correct in ours.

*Why it happened:* entry 2 corrected a specific wrong claim (`#O` means author)
without revisiting the premise underneath it — that an extension was needed at
all. Fixing the error rather than re-reading the spec left the larger mistake
standing. The verification was aimed at the answer, not at the question.

*Shipped instead:* `#CXRLE Pos=…` with `Gen=` omitted when zero, matching
Golly's own output byte for byte. `Pos` is signed and our coordinates unsigned,
which turns out to cost nothing: on a torus of exactly 2^64 cells per axis both
are residues modulo 2^64, so the signed and unsigned readings of the same 64
bits name the same cell. The conversion is a bit reinterpretation that loses
nothing in either direction, and it keeps emitted values inside the range other
tools expect.

*Second defect, found while fixing the first:* the hand-written `Pos` for the
Gosper gun was `-9223372036854775826`, which is below `long.MinValue` and
therefore not a value any reader could accept. The gun's origin is `2^63 - 18`,
which is *under* 2^63 and so unchanged by the signed reinterpretation — the
negation was applied where it did not belong. Arithmetic done by hand in a data
file that no test covered.

*Guards added:*
- `docs/rle-format.md` records every tag, its meaning, and the source it came
  from, with each claim marked *Verified* (read in a fetched source) or
  *Reported* (from a summary, LifeWiki being unreachable). Provenance is now
  part of the artifact, so the next correction starts from evidence.
- `Every_Shipped_Pattern_Has_A_Position_Inside_The_Signed_Range` asserts every
  shipped file's origin survives the `ulong -> long -> ulong` round trip *and*
  matches what the writer would emit. Hand-authored data files are now covered
  by the same tests as generated ones.
- `Accepts_A_CXRLE_Position_In_Either_Signed_Or_Unsigned_Form` pins the
  equivalence the design relies on.

*The transferable lesson:* entry 2's correction was verified against the claim
it replaced, not against the format. Checking that `#O` was wrong did not
prompt asking whether the format already had a right answer. When a defect is
found in an assumption, the assumptions next to it are the ones most likely to
be wrong too.


### Bolt 3 — Protocol

**No defects found.** Recorded because a log that only fills up when something
breaks is not evidence of review — the anticipated failures A4 and A5 were the
two most likely defects in this bolt, and both were guarded before any code was
written rather than discovered afterwards.

- **A4** (`ulong` as a JSON number, losing precision above 2^53) is prevented by
  `UInt64StringConverter` and pinned by
  `Coordinates_Above_Two_To_The_Fifty_Three_Survive_A_Round_Trip`.
  `A_Number_Would_Have_Lost_Precision` additionally demonstrates the defect
  rather than only asserting its absence, so the test explains why the converter
  exists and cannot be deleted as ceremony.
- **A5** (framing that assumes one read yields one whole message) is pinned by
  `Reader_Survives_A_Stream_Delivered_One_Byte_At_A_Time`, which exercises every
  split point in a frame including inside the length prefix, and by
  `Reader_Handles_A_Segmented_Sequence`, since Pipelines hands over
  multi-segment sequences whenever a frame spans buffer boundaries.

**A9 added to the anticipated list during construction.** The length prefix is
attacker-controlled input: five bytes declaring `0xFFFFFFFF` would have the
server buffer four gigabytes for a frame that never arrives, a denial of service
that costs the sender nothing. This was not on the original list — it emerged
from asking what a hostile peer could do with each field, which is a question
worth asking of every wire format and was not asked at inception.
`FrameCodec.MaxPayloadLength` bounds it at 1 MiB, three orders of magnitude
above the largest legitimate frame.

**One compile-time catch worth noting.** The test demonstrating the 2^53 defect
was first written as `(ulong)(double)ulong.MaxValue`, which the C# compiler
refused outright: *"Constant value '1.8446744073709552E+19' cannot be converted
to 'ulong'"*. The compiler rejected the constant-folded form of exactly the
conversion the converter exists to prevent at runtime. The test now reads the
value from an array so the demonstration survives to execution.


### Bolt 4 — Server

**Entry 4 — cleanup that closed the socket out from under the writer.**

*Symptom:* `A_Malformed_Frame_Is_Answered_Then_The_Connection_Ends` passed when
run alone and failed in the full suite, with the client reporting *"Server
closed the connection"* before the promised `ErrorMessage` arrived. A flaky test
is normally the test's fault. This one was the server's.

*First layer, found by reading the shutdown path:* on a protocol violation the
read loop queued an `ErrorMessage` and returned; `ServeAsync` then cancelled the
write loop through `Task.WhenAny` and the `finally`. The message was queued and
the queue was abandoned. The server had a documented promise — refusals are
explicit, never silent — and a shutdown path that quietly broke it whenever the
refusal was the *last* thing to send.

*Second layer, the actual root cause:* fixing the ordering did not fix the test.
`PipeReader.Create(Stream)` defaults to **`leaveOpen: false`**, so the read
loop's own `reader.CompleteAsync()` in its `finally` disposed the
`NetworkStream`. The socket was closed by the reader's cleanup before the writer
could flush anything. The first fix was correct and insufficient; the defect was
one layer below where the symptom pointed.

*Shipped:*
- `PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true))`, so
  the reader's lifecycle no longer owns the socket's.
- `CompleteOutbound()` separates graceful shutdown (stop accepting, drain what
  is queued) from `Close()` (abandon now), and `ServeAsync` awaits the flush
  under a two-second timeout so a peer that will not read cannot hold the
  connection open.
- `WriteLoopAsync` now exits on queue *completion* rather than cancellation, so
  the last message on a doomed connection still goes out.

*Why it matters:* every individual piece was defensible. The read loop
completing its own reader is correct hygiene; `Task.WhenAny` on two loops is a
normal pattern; a bounded queue is the right design. The defect lived in the
interaction, and only appeared when two connections ran concurrently — which is
why it showed up in the full suite and not in isolation. Concurrency defects
that hide under sequential testing are exactly what a per-file review does not
catch.

*Guard:* the test now runs green five consecutive times, and the full suite
three, since a single green run proves nothing about a race.

**A10 added and guarded proactively.** Load and save take a file name from a
network peer. Handing that to the filesystem unchecked is a path traversal, and
the write side is the damaging half — any client could overwrite any file the
server process can reach. `PatternStore` resolves each candidate against its
root and requires the result to stay beneath it, which rejects `..`, absolute
paths and symlink escapes together rather than blocklisting patterns. The
boundary check compares against root-plus-separator, so a sibling directory
whose name merely *starts with* the root (`/data-evil` against `/data`) is
rejected too — the defect a naive `StartsWith` would have shipped.
