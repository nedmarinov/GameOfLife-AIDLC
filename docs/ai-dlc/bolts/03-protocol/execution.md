# Bolt 03 — Protocol · execution

**Commits**

- `69b24d1 feat(protocol): length-prefixed framing and control messages`

**Diff:** 13 files, +1255 −2
**Tests after this bolt:** 98

## What was built

- 41 protocol tests, 98 total. Frames are u32 length, type byte, payload.
- The viewport frame is fixed size regardless of population — asserted against both an empty universe and a fully packed one.
- Coordinates serialise as JSON strings, with a test that *demonstrates* the precision loss rather than only asserting its absence, so the converter cannot later be deleted as ceremony.

## What changed from the plan

- **A9 added to the anticipated failures during construction.** A length prefix is attacker-controlled input: five bytes declaring `0xFFFFFFFF` would have the server buffer four gigabytes for a frame that never arrives. Bounded at 1 MiB.

## What was learned

- A9 was missed at inception because the planning pass never asked *what a hostile peer could do with each field*. For a device gateway that is the question, so the omission is recorded rather than quietly patched.
- The C# compiler independently refused to fold `(ulong)(double)ulong.MaxValue` — the same complaint the converter exists to make at runtime.
