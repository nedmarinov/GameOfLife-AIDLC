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


### Bolt 5 — Console client

**Entry 5 — a wrong test oracle, again, and the same cause as entry 1.**

*Symptom:* `Renders_A_Window_Straddling_The_Seam` failed asserting that a live
cell painted the foreground colour. It painted the cursor colour instead.

*What was wrong:* the test parked the cursor at `(0, 0)` "outside the window" so
it could not tint anything. But the window under test starts at
`ulong.MaxValue - 9` and wraps, so `(0, 0)` is **inside** it, at local
`(10, 10)` — precisely the live cell being asserted. The renderer was correct;
the test's mental model of the geometry was not.

*Why it matters:* this is the second time a test oracle rather than the code was
wrong, and both times the subject was seam geometry (see entry 1). The pattern
is worth naming: on a torus, intuitions about "far away" and "outside" stop
holding. `(0, 0)` reads as the opposite corner from `MaxValue` and is in fact
adjacent to it. Every wrong oracle so far has come from reasoning about wrapped
coordinates in the head instead of on the page.

*Shipped instead:* the cursor moved to `(1_000, 1_000)`, genuinely outside, and
the test now asserts both halves — background for the odd cell row, foreground
for the even one — so it pins the half-block packing rather than merely
detecting colour.

*Guard:* `A_Cursor_Outside_The_Window_Tints_Nothing` uses a non-wrapping
viewport, where "outside" means what it appears to mean.

**Verified end to end, not just by unit test.** Two clients were run
concurrently against one server with the gun seeded: both reported identical
generations 0..206 and population climbing from 36 as gliders were emitted. A
renderer that passes its own tests can still be wrong about what the server
sends; two independent processes agreeing on 290 frames is a different kind of
evidence.

**One robustness fix found by the same exercise.** `Console.KeyAvailable`
throws when stdin is redirected, which is exactly how the client runs under a
script or a pipe. Rather than treat that as an error, the client now runs as a
pure observer when there is no keyboard — which is what made the two-client
verification above possible in the first place.


### Bolt 6 — Documentation and cross-platform

**Entry 6 — a portability gap that would have passed every test and failed on a
reviewer's machine.**

*Found by:* asking what differs per platform rather than waiting for a failure.
There was no failing test, and there could not have been: the whole suite runs
on macOS.

*What was wrong:* macOS and Linux terminals interpret ANSI escapes
unconditionally. Windows does not, and .NET does **not** enable virtual terminal
processing on your behalf. Without it the client prints every escape literally
and the screen fills with text like `[38;5;231m` instead of a grid. Windows
Terminal happens to enable it already, so this is precisely the defect that
works on one reviewer's machine and fails on another's — and it would have been
reported as "your client doesn't work", not as a terminal setting.

*Shipped:* `Screen.Enter` calls `SetConsoleMode` with
`ENABLE_VIRTUAL_TERMINAL_PROCESSING`, guarded by `OperatingSystem.IsWindows()`
and tolerant of failure, since an older console losing colour is better than a
crash. `DllImport` rather than `LibraryImport`: the source-generated form
requires `AllowUnsafeBlocks` across the whole assembly, which three interop
declarations do not justify.

*Still outstanding, and recorded as such:* the client has not actually been run
on Linux or Windows. N5 in `02-requirements.md` and the table in
`07-operations.md` both say so rather than claiming coverage. Reasoning that
code is portable is not the same as having run it, and the difference is exactly
what this entry is about.

**One observation worth keeping.** A test tried to express "pan from the centre
of the universe to the seam" as a signed delta and would not compile: that
distance exceeds `long`. It is not a real limitation — the universe wraps, so
every destination is reachable by going the shorter way round, which always
fits — but it is a good reminder that on a 2^64 torus the difference between two
coordinates is not always a representable number. Recorded in
`07-operations.md` under known limits; the client uses absolute `subscribe` for
long jumps rather than computing a delta.


### Bolt 6b — Pattern catalogue

Prompted by the question "do we support all oscillators, spaceships and still
lifes?" The answer is yes by construction — the engine implements B3/S23 rather
than a catalogue of shapes — but that is exactly the kind of claim entries 2 and
3 were also confident about, so it was tested instead of asserted.

**The engine was right. The test data was wrong, for the third time.**

Of eighteen catalogue entries, four failed, and every failure was a mistyped
pattern or a wrong expectation of mine:

| Claimed | Actual |
|---------|--------|
| Clock `2bo$2ob$b2o$bo!`, period 2 | never repeats — one row was shifted a column |
| LWSS travels `(+2, 0)` | travels `(-2, 0)`; written in the left-moving orientation |
| MWSS `3bob$o4bo$5bo$o4bo$b5o!` | 11 cells, correct count, wrong shape, never repeats |
| HWSS `3b2ob$o5bo$6bo$o5bo$b6o!` | 13 cells, correct count, wrong shape, never repeats |

Meanwhile every check whose expected value came from **outside** this project
passed on the first run: the R-pentomino settling at generation 1,103 with
exactly 116 cells, the acorn at 5,206 with 633, the diehard vanishing on
generation 130, the pulsar at period 3, the pentadecathlon at period 15. A rule
defect anywhere in birth, survival or neighbour counting diverges from those
trajectories long before generation 1,103, so agreeing with all five is far
stronger evidence than any pattern the author writes by hand.

*The lesson, and it is now a pattern rather than an incident:* across entries 1,
5 and this one, **every wrong oracle has been hand-authored and every externally
sourced one has been right.** Data invented alongside the code inherits the
author's assumptions; data taken from outside does not. Where a published figure
exists, use it — it is the difference between a test that confirms what was
believed and a test that could have proved it wrong.

*Method:* rather than guess again at the corrections, the actual period and
displacement of each pattern were **measured** with a throwaway program and the
test data set from the measurements. Two of the four wrong patterns had the
right cell count, so `ExpectedPopulation` now pins population *and* displacement
— either alone would have let a transcription slip through that still happened
to travel.


### Bolt 7 — Web bridge

**No defects found.** The stretch bolt went in cleanly, and the reason is worth
recording: it reused decisions already made rather than making new ones.

- **One protocol, two transports.** The browser receives the identical
  length-prefixed frames a native client receives, carried inside WebSocket
  messages. That costs four redundant bytes per message — WebSocket already
  delimits — and buys a single specification. `ClientConnection` was generalised
  from `TcpClient` to `Stream`, after which the server cannot tell the two apart
  and every existing policy, including the paused-only edit rule, applies to
  browsers for free. `The_Browser_Is_Subject_To_The_Same_Edit_Policy` asserts
  that rather than assuming it.
- **The handshake is owned; the framing is not.** RFC 6455's handshake is four
  lines — hash the client key with a fixed GUID and echo it back — so it is
  written here. Masking, fragmentation and control frames are left to
  `WebSocket.CreateFromStream`. Owning the parts worth owning was the argument
  in ADR 0002 for hand-rolling TCP framing; the same argument says *not* to
  reimplement RFC 6455.
- **`leaveOpen` was already understood.** The bridge hands its stream to a
  `WebSocket` that outlives the handshake, exactly the ownership question that
  produced entry 4. It was handled at the point of writing rather than found by
  a flaky test.

**The handshake test uses the specification's own numbers.** RFC 6455 §1.3
publishes a worked example: key `dGhlIHNhbXBsZSBub25jZQ==` must produce
`s3pPLMBiTxaQ9kYGzzhZRbK+xOo=`. Asserting against that rather than against
output generated by this code is the same principle that caught the `#O` defect
and validated the pattern catalogue — and by now it is a standing rule rather
than a lesson: **where a published figure exists, the test uses it.** Verified
over the wire too, with `curl` returning that exact accept value from a running
server.

The integration tests use `ClientWebSocket`, so the handshake and framing are
exercised by an independent implementation of the RFC rather than by the one
under test.


### Bolt 8 — Zoom

**Entry 7 — a stale frame that could never be corrected.**

*Symptom:* a new integration test timed out waiting for a viewport frame after
toggling a cell while paused.

*What was wrong:* the tick loop broadcast only when a `_dirty` flag was set —
on an edit, a load, a viewport change or a connection. That is a sound
optimisation for an idle universe and it was wrong for a subtle reason. Egress
queues drop the **oldest** frame when full, so a client that fell behind at the
moment the simulation was paused would keep a stale frame **forever**: nothing
would change, so nothing would ever be sent to correct it. The two mechanisms
were individually reasonable — drop stale frames because a newer one is always
coming; skip redundant broadcasts because nothing changed — and together they
produced a state where the newer frame never came.

*Why the test found it and review had not:* the interaction only exists when the
simulation is paused *and* a client is behind. Every earlier test either ran the
simulation or read fast enough. The failure was reported as a timeout, which
looks like a slow test rather than a defect.

*Shipped:* broadcast on change **or** at least once per second, whichever comes
first. That bounds how long any client can display something wrong to one
second, while an idle universe still costs a kilobyte a second per client.

*Guard:* `Zooming_Out_Brings_The_Whole_Universe_Into_One_Window` exercises the
paused path and would time out again if the floor were removed.

**On the feature itself.** Zoom is a power of two so the cell-to-block mapping is
a shift, not a division: exact, and no rounding can place a cell in the
neighbouring block. There is deliberately no zoom *in* past 1:1 — a cell is the
smallest thing there is, so magnifying would show no more information and only
shrink the visible area, which is the opposite of what a 2^64 universe needs.
The buttons are honest about this: zooming in clamps at 1:1.

The overflow boundary is guarded rather than discovered:
`Width << Zoom` wraps above zoom 57 for a 100-wide window, and a wrapped
coverage value would make the containment test silently accept cells *outside*
the window — which would look like random cells appearing at extreme zoom rather
than like an arithmetic fault. `Viewport.MaxZoomFor` computes the limit from the
width's bit length and the constructor refuses to exceed it.


### Terminology — a misused term, found by being asked

*Asked:* what a bolt is, and why that name.

*What was wrong:* the definition was right — a bolt is AI-DLC's replacement for
a sprint, a cycle measured in hours or days — but its **scope** was not. Bolts
belong to the Construction phase. Inception, Construction and Operations are the
three phases; bolts are the execution mechanism inside Construction. Labelling
the inception work "Bolt 0" applied a Construction-phase term to a phase.

*Why it survived this long:* the term was introduced in the very first plan
document and never defined. Nothing downstream had reason to question it, and an
undefined term cannot be checked — it just propagates. Every artifact since has
used the numbering without anyone, including me, going back to the source.

*Shipped:* the bolts README now defines the term, cites what AWS does and does
not say about it, and records the inaccuracy plainly. The numbering is kept,
because the commits, the units-of-work table and this log all reference it, and
renumbering for pedantic correctness would break more than it fixes.

*The pattern this belongs to:* the definition came from recall and happened to
be right; the scope came from recall and was not. That is the same failure shape
as entries 2 and 3, where `#O` and then the whole `#CXRLE` question were
answered from memory rather than from the source. **The standing rule — where a
published source exists, check it — applies to vocabulary, not only to formats
and figures.** A term introduced without a citation is an unchecked claim that
then spreads through every artifact that uses it.
