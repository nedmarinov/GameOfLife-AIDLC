using System.Buffers;
using System.Buffers.Binary;
using GameOfLife.Core;

namespace GameOfLife.Protocol;

/// <summary>
/// The per-generation payload: one bit per cell of a client's viewport.
/// </summary>
/// <remarks>
/// <para>
/// A 100x100 window is 10,000 cells — 1,250 bytes — plus a 28-byte header,
/// <b>whatever the population</b>. The obvious alternative, a JSON list of live
/// coordinates, is larger above roughly 150 live cells, varies in size with
/// every generation, and allocates per cell. For a system whose whole purpose is
/// pushing state at a fixed tick rate, a constant-size frame is the right
/// shape.
/// </para>
/// <para>
/// Each frame is a complete snapshot rather than a delta. That is what lets a
/// congested client's stale frames be dropped instead of queued (ADR 0003), and
/// what lets a client joining mid-run become consistent with no replay logic.
/// </para>
/// <code>
/// gen:u64 | originX:u64 | originY:u64 | width:u16 | height:u16 | bitmap[ceil(w*h/8)]
/// </code>
/// </remarks>
public static class ViewportFrame
{
    public const int HeaderLength = (sizeof(ulong) * 3) + (sizeof(ushort) * 2) + (sizeof(byte) * 2);

    /// <summary>Total payload size for a viewport of the given dimensions.</summary>
    public static int PayloadLength(in Viewport viewport) => HeaderLength + viewport.BitmapByteCount;

    /// <summary>Encodes a universe's visible cells for one viewport.</summary>
    public static void Write(
        IBufferWriter<byte> writer, Universe universe, in Viewport viewport, ulong generation)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(universe);

        int length = PayloadLength(viewport);
        Span<byte> destination = writer.GetSpan(length);

        WriteHeader(destination, viewport, generation);
        universe.RenderTo(viewport, destination[HeaderLength..]);

        writer.Advance(length);
    }

    /// <summary>Encodes a pre-rendered bitmap. Used when many clients share a viewport.</summary>
    public static void Write(
        IBufferWriter<byte> writer, in Viewport viewport, ulong generation, ReadOnlySpan<byte> bitmap)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (bitmap.Length < viewport.BitmapByteCount)
            throw new ArgumentException("Bitmap is smaller than the viewport requires.", nameof(bitmap));

        int length = PayloadLength(viewport);
        Span<byte> destination = writer.GetSpan(length);

        WriteHeader(destination, viewport, generation);
        bitmap[..viewport.BitmapByteCount].CopyTo(destination[HeaderLength..]);

        writer.Advance(length);
    }

    private static void WriteHeader(Span<byte> destination, in Viewport viewport, ulong generation)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(destination, generation);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[8..], viewport.OriginX);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[16..], viewport.OriginY);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[24..], (ushort)viewport.Width);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[26..], (ushort)viewport.Height);
        destination[28] = (byte)viewport.Zoom;
        destination[29] = 0;
    }

    /// <summary>Decodes a viewport frame payload.</summary>
    /// <exception cref="ProtocolException">The payload is truncated or self-inconsistent.</exception>
    public static (Viewport Viewport, ulong Generation, byte[] Bitmap) Read(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < HeaderLength)
            throw new ProtocolException($"Viewport frame is {payload.Length} bytes, shorter than its header.");

        ulong generation = BinaryPrimitives.ReadUInt64LittleEndian(payload);
        ulong originX = BinaryPrimitives.ReadUInt64LittleEndian(payload[8..]);
        ulong originY = BinaryPrimitives.ReadUInt64LittleEndian(payload[16..]);
        int width = BinaryPrimitives.ReadUInt16LittleEndian(payload[24..]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(payload[26..]);
        int zoom = payload[28];

        if (width == 0 || height == 0)
            throw new ProtocolException($"Viewport frame declared an empty {width}x{height} window.");

        if (zoom > Viewport.MaxZoomFor(width, height))
        {
            throw new ProtocolException(
                $"Viewport frame declared zoom {zoom}, beyond the maximum for a {width}x{height} window.");
        }

        var viewport = new Viewport(originX, originY, width, height, zoom);
        int expected = viewport.BitmapByteCount;

        // The declared dimensions must agree with the bytes actually sent;
        // otherwise a truncated frame would be read as a smaller valid one.
        if (payload.Length - HeaderLength < expected)
        {
            throw new ProtocolException(
                $"Viewport frame declares {width}x{height} ({expected} bitmap bytes) but carries " +
                $"{payload.Length - HeaderLength}.");
        }

        return (viewport, generation, payload.Slice(HeaderLength, expected).ToArray());
    }
}
