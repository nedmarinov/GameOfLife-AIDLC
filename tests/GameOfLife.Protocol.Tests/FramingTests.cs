using System.Buffers;
using GameOfLife.Protocol;

namespace GameOfLife.Protocol.Tests;

/// <summary>
/// Framing over a byte stream — the part TCP does not do for you.
/// </summary>
public class FramingTests
{
    private static byte[] Framed(FrameType type, byte[] payload)
    {
        var buffer = new ArrayBufferWriter<byte>();
        FrameCodec.Write(buffer, type, payload);
        return buffer.WrittenSpan.ToArray();
    }

    private static bool TryReadOne(byte[] bytes, out FrameType type, out byte[] payload)
    {
        var sequence = new ReadOnlySequence<byte>(bytes);
        return FrameCodec.TryRead(ref sequence, out type, out payload);
    }

    [Fact]
    public void Round_Trips_A_Frame()
    {
        byte[] payload = [1, 2, 3, 4, 5];

        Assert.True(TryReadOne(Framed(FrameType.Control, payload), out FrameType type, out byte[] read));
        Assert.Equal(FrameType.Control, type);
        Assert.Equal(payload, read);
    }

    [Fact]
    public void Round_Trips_An_Empty_Payload()
    {
        Assert.True(TryReadOne(Framed(FrameType.Control, []), out FrameType type, out byte[] read));
        Assert.Equal(FrameType.Control, type);
        Assert.Empty(read);
    }

    [Fact]
    public void Header_Is_Five_Bytes_And_Length_Covers_The_Type()
    {
        byte[] framed = Framed(FrameType.Viewport, [0xAA, 0xBB]);

        Assert.Equal(FrameCodec.HeaderLength + 2, framed.Length);
        // Little-endian length of 3 = type byte + two payload bytes.
        Assert.Equal([0x03, 0x00, 0x00, 0x00, (byte)FrameType.Viewport, 0xAA, 0xBB], framed);
    }

    /// <summary>
    /// The test the framing exists to pass.
    /// </summary>
    /// <remarks>
    /// A single TCP read can return any number of bytes: half a header, one
    /// byte, or three frames at once. Feeding the reader one byte at a time
    /// exercises every possible split point in a frame, including inside the
    /// length prefix. An implementation that assumes one read yields one whole
    /// message — the standard defect in hand-written socket code — fails here
    /// immediately.
    /// </remarks>
    [Fact]
    public void Reader_Survives_A_Stream_Delivered_One_Byte_At_A_Time()
    {
        byte[] payload = [.. Enumerable.Range(0, 300).Select(i => (byte)i)];
        byte[] framed = Framed(FrameType.Viewport, payload);

        var accumulated = new List<byte>();
        byte[]? recovered = null;
        FrameType recoveredType = default;

        foreach (byte b in framed)
        {
            accumulated.Add(b);

            var sequence = new ReadOnlySequence<byte>([.. accumulated]);

            if (FrameCodec.TryRead(ref sequence, out FrameType type, out byte[] read))
            {
                recovered = read;
                recoveredType = type;

                // It must not complete before the final byte arrives.
                Assert.Equal(framed.Length, accumulated.Count);
            }
        }

        Assert.NotNull(recovered);
        Assert.Equal(FrameType.Viewport, recoveredType);
        Assert.Equal(payload, recovered);
    }

    [Fact]
    public void Reader_Consumes_Exactly_One_Frame_From_A_Batch()
    {
        byte[] first = Framed(FrameType.Control, [1, 1, 1]);
        byte[] second = Framed(FrameType.Viewport, [2, 2]);
        byte[] third = Framed(FrameType.Control, [3]);

        var sequence = new ReadOnlySequence<byte>([.. first, .. second, .. third]);

        Assert.True(FrameCodec.TryRead(ref sequence, out FrameType t1, out byte[] p1));
        Assert.Equal(FrameType.Control, t1);
        Assert.Equal([1, 1, 1], p1);

        Assert.True(FrameCodec.TryRead(ref sequence, out FrameType t2, out byte[] p2));
        Assert.Equal(FrameType.Viewport, t2);
        Assert.Equal([2, 2], p2);

        Assert.True(FrameCodec.TryRead(ref sequence, out FrameType t3, out byte[] p3));
        Assert.Equal([3], p3);

        Assert.False(FrameCodec.TryRead(ref sequence, out _, out _));
        Assert.Equal(0, sequence.Length);
    }

    [Fact]
    public void Reader_Handles_A_Segmented_Sequence()
    {
        // Pipelines hands over multi-segment sequences whenever a frame spans
        // buffer boundaries, so the reader must never assume a single span.
        byte[] framed = Framed(FrameType.Control, [.. Enumerable.Repeat((byte)0x7F, 100)]);

        ReadOnlySequence<byte> sequence = Segmented(framed, chunkSize: 7);

        Assert.True(FrameCodec.TryRead(ref sequence, out FrameType type, out byte[] payload));
        Assert.Equal(FrameType.Control, type);
        Assert.Equal(100, payload.Length);
        Assert.All(payload, b => Assert.Equal(0x7F, b));
    }

    [Fact]
    public void Incomplete_Frame_Leaves_The_Buffer_Untouched()
    {
        byte[] framed = Framed(FrameType.Control, [9, 9, 9, 9]);
        var sequence = new ReadOnlySequence<byte>(framed[..6]);
        long before = sequence.Length;

        Assert.False(FrameCodec.TryRead(ref sequence, out _, out _));
        Assert.Equal(before, sequence.Length);
    }

    [Fact]
    public void Header_Alone_Is_Not_A_Frame()
    {
        byte[] framed = Framed(FrameType.Control, [1, 2, 3]);
        var sequence = new ReadOnlySequence<byte>(framed[..FrameCodec.HeaderLength]);

        Assert.False(FrameCodec.TryRead(ref sequence, out _, out _));
    }

    [Fact]
    public void Rejects_A_Declared_Length_Of_Zero()
    {
        // len must cover the type byte, so zero cannot be legal.
        byte[] malformed = [0x00, 0x00, 0x00, 0x00, 0x01];
        var sequence = new ReadOnlySequence<byte>(malformed);

        Assert.Throws<ProtocolException>(() => FrameCodec.TryRead(ref sequence, out _, out _));
    }

    /// <summary>
    /// A length prefix is attacker-controlled input.
    /// </summary>
    /// <remarks>
    /// Five bytes claiming a four-gigabyte frame would otherwise have the
    /// server buffer indefinitely for a frame that never arrives. The ceiling
    /// turns a denial of service into a dropped connection.
    /// </remarks>
    [Fact]
    public void Rejects_An_Absurd_Declared_Length_Without_Buffering_It()
    {
        byte[] hostile = [0xFF, 0xFF, 0xFF, 0xFF, 0x01];
        var sequence = new ReadOnlySequence<byte>(hostile);

        ProtocolException error = Assert.Throws<ProtocolException>(
            () => FrameCodec.TryRead(ref sequence, out _, out _));

        Assert.Contains("limit", error.Message);
    }

    [Fact]
    public void Writer_Rejects_An_Oversized_Payload()
    {
        var buffer = new ArrayBufferWriter<byte>();
        byte[] tooBig = new byte[FrameCodec.MaxPayloadLength + 1];

        Assert.Throws<ArgumentException>(() => FrameCodec.Write(buffer, FrameType.Control, tooBig));
    }

    [Fact]
    public void Accepts_A_Payload_Exactly_At_The_Limit()
    {
        var buffer = new ArrayBufferWriter<byte>();
        byte[] atLimit = new byte[FrameCodec.MaxPayloadLength];

        FrameCodec.Write(buffer, FrameType.Control, atLimit);

        var sequence = new ReadOnlySequence<byte>(buffer.WrittenSpan.ToArray());
        Assert.True(FrameCodec.TryRead(ref sequence, out _, out byte[] payload));
        Assert.Equal(FrameCodec.MaxPayloadLength, payload.Length);
    }

    [Fact]
    public void Unknown_Frame_Type_Is_Surfaced_Rather_Than_Guessed()
    {
        // Framing must stay agnostic about payload meaning: an unrecognised
        // type is the caller's problem to route, not the codec's to reject.
        var buffer = new ArrayBufferWriter<byte>();
        FrameCodec.Write(buffer, (FrameType)0x7E, [1]);

        var sequence = new ReadOnlySequence<byte>(buffer.WrittenSpan.ToArray());

        Assert.True(FrameCodec.TryRead(ref sequence, out FrameType type, out _));
        Assert.Equal((FrameType)0x7E, type);
        Assert.False(Enum.IsDefined(type));
    }

    private static ReadOnlySequence<byte> Segmented(byte[] data, int chunkSize)
    {
        Segment? head = null;
        Segment? tail = null;

        for (int offset = 0; offset < data.Length; offset += chunkSize)
        {
            int length = Math.Min(chunkSize, data.Length - offset);
            var memory = new ReadOnlyMemory<byte>(data, offset, length);

            if (head is null)
            {
                head = tail = new Segment(memory, 0);
                continue;
            }

            tail = tail!.Append(memory);
        }

        return new ReadOnlySequence<byte>(head!, 0, tail!, tail!.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory, long runningIndex)
        {
            Memory = memory;
            RunningIndex = runningIndex;
        }

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory, RunningIndex + Memory.Length);
            Next = next;
            return next;
        }
    }
}
