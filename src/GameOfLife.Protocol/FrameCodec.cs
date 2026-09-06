using System.Buffers;
using System.Buffers.Binary;

namespace GameOfLife.Protocol;

/// <summary>The kind of payload a frame carries.</summary>
public enum FrameType : byte
{
    /// <summary>UTF-8 JSON control message. Both directions.</summary>
    Control = 0x01,

    /// <summary>Binary viewport bitmap. Server to client.</summary>
    Viewport = 0x02,
}

/// <summary>Raised when a peer sends something the framing rules forbid.</summary>
public sealed class ProtocolException(string message) : Exception(message);

/// <summary>
/// Length-prefixed framing over a TCP byte stream.
/// </summary>
/// <remarks>
/// <para>
/// TCP is a stream, not a message channel: a single read may return half a
/// frame, three frames, or one byte. Framing is what turns that back into
/// messages, and owning it explicitly — rather than delegating to WebSocket or
/// gRPC — is the point of ADR 0002.
/// </para>
/// <code>
/// +---------+--------+--------------------+
/// | len:u32 | type:u8|  payload (len-1)   |   little-endian
/// +---------+--------+--------------------+
/// </code>
/// <para>
/// <c>len</c> counts the type byte plus the payload, so the smallest legal
/// frame has <c>len == 1</c>. Reading follows the
/// <see cref="System.IO.Pipelines"/> convention: <see cref="TryRead"/> consumes
/// from the sequence only when a whole frame is present, and leaves the
/// sequence untouched otherwise, so the caller can simply ask for more data.
/// </para>
/// </remarks>
public static class FrameCodec
{
    /// <summary>Bytes of length prefix plus type byte.</summary>
    public const int HeaderLength = sizeof(uint) + sizeof(byte);

    /// <summary>
    /// Largest payload accepted, in bytes.
    /// </summary>
    /// <remarks>
    /// A length prefix is attacker-controlled input. Without a ceiling, one
    /// peer sending <c>len = 0xFFFFFFFF</c> would have the server buffer four
    /// gigabytes waiting for a frame that never arrives — a denial of service
    /// costing the sender five bytes. The largest legitimate frame here is a
    /// viewport bitmap, on the order of a kilobyte, so this limit is generous
    /// by three orders of magnitude and still bounds the damage.
    /// </remarks>
    public const int MaxPayloadLength = 1 << 20;

    /// <summary>Writes one frame.</summary>
    public static void Write(IBufferWriter<byte> writer, FrameType type, ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (payload.Length > MaxPayloadLength)
        {
            throw new ArgumentException(
                $"Payload of {payload.Length} bytes exceeds the {MaxPayloadLength}-byte limit.",
                nameof(payload));
        }

        Span<byte> destination = writer.GetSpan(HeaderLength + payload.Length);

        BinaryPrimitives.WriteUInt32LittleEndian(destination, (uint)(payload.Length + sizeof(byte)));
        destination[sizeof(uint)] = (byte)type;
        payload.CopyTo(destination[HeaderLength..]);

        writer.Advance(HeaderLength + payload.Length);
    }

    /// <summary>
    /// Reads one frame if a whole one is buffered.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> and advances <paramref name="buffer"/> past the
    /// frame; otherwise <see langword="false"/> with the buffer unchanged.
    /// </returns>
    /// <exception cref="ProtocolException">
    /// The peer declared a frame that cannot be legal, so the connection can no
    /// longer be trusted to be in sync and must be dropped rather than resumed.
    /// </exception>
    public static bool TryRead(ref ReadOnlySequence<byte> buffer, out FrameType type, out byte[] payload)
    {
        type = default;
        payload = [];

        if (buffer.Length < HeaderLength) return false;

        Span<byte> header = stackalloc byte[HeaderLength];
        buffer.Slice(0, HeaderLength).CopyTo(header);

        uint declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(header);

        // len covers the type byte, so a frame carrying no type is malformed
        // rather than merely empty.
        if (declaredLength < sizeof(byte))
            throw new ProtocolException("Frame declared a length of zero; the type byte is mandatory.");

        if (declaredLength - sizeof(byte) > MaxPayloadLength)
        {
            throw new ProtocolException(
                $"Frame declared a {declaredLength - sizeof(byte)}-byte payload, over the " +
                $"{MaxPayloadLength}-byte limit.");
        }

        long totalLength = sizeof(uint) + declaredLength;

        // The whole frame has not arrived yet. Report incompleteness without
        // consuming anything, so the caller reads more and asks again.
        if (buffer.Length < totalLength) return false;

        type = (FrameType)header[sizeof(uint)];

        int payloadLength = (int)(declaredLength - sizeof(byte));
        payload = payloadLength == 0
            ? []
            : buffer.Slice(HeaderLength, payloadLength).ToArray();

        buffer = buffer.Slice(totalLength);
        return true;
    }
}
