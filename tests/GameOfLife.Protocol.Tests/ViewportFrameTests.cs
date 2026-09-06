using System.Buffers;
using GameOfLife.Core;
using GameOfLife.Protocol;

namespace GameOfLife.Protocol.Tests;

public class ViewportFrameTests
{
    private const int Ui = 100;

    private static byte[] Encode(Universe universe, Viewport viewport, ulong generation)
    {
        var buffer = new ArrayBufferWriter<byte>();
        ViewportFrame.Write(buffer, universe, viewport, generation);
        return buffer.WrittenSpan.ToArray();
    }

    [Fact]
    public void A_Hundred_Square_Frame_Is_1278_Bytes_Whatever_The_Population()
    {
        var viewport = new Viewport(0, 0, Ui, Ui);

        var empty = new Universe();
        var crowded = new Universe();
        crowded.Reset(Enumerable.Range(0, Ui)
            .SelectMany(y => Enumerable.Range(0, Ui).Select(x => new Cell((ulong)x, (ulong)y))));

        byte[] emptyFrame = Encode(empty, viewport, 0);
        byte[] crowdedFrame = Encode(crowded, viewport, 0);

        // 28-byte header + 1250-byte bitmap.
        Assert.Equal(1_278, emptyFrame.Length);
        Assert.Equal(emptyFrame.Length, crowdedFrame.Length);
    }

    [Fact]
    public void Round_Trips_Viewport_Generation_And_Cells()
    {
        var viewport = new Viewport(1UL << 63, 42, Ui, Ui);
        var universe = new Universe();
        universe.Reset(
        [
            viewport.CellAt(0, 0),
            viewport.CellAt(50, 25),
            viewport.CellAt(99, 99),
        ]);

        (Viewport read, ulong generation, byte[] bitmap) =
            ViewportFrame.Read(Encode(universe, viewport, 123_456_789));

        Assert.Equal(viewport, read);
        Assert.Equal(123_456_789UL, generation);
        Assert.True(Viewport.IsSet(bitmap, read.BitIndex(0, 0)));
        Assert.True(Viewport.IsSet(bitmap, read.BitIndex(50, 25)));
        Assert.True(Viewport.IsSet(bitmap, read.BitIndex(99, 99)));
    }

    [Fact]
    public void Origin_Survives_The_Full_Unsigned_Range()
    {
        // The header carries raw u64, so unlike JSON there is no 2^53 cliff --
        // but it still has to be asserted rather than assumed.
        var viewport = new Viewport(ulong.MaxValue, ulong.MaxValue - 7, Ui, Ui);
        var universe = new Universe();

        (Viewport read, _, _) = ViewportFrame.Read(Encode(universe, viewport, ulong.MaxValue));

        Assert.Equal(ulong.MaxValue, read.OriginX);
        Assert.Equal(ulong.MaxValue - 7, read.OriginY);
    }

    [Fact]
    public void Generation_Survives_The_Full_Unsigned_Range()
    {
        var viewport = new Viewport(0, 0, 8, 8);

        (_, ulong generation, _) = ViewportFrame.Read(Encode(new Universe(), viewport, ulong.MaxValue));

        Assert.Equal(ulong.MaxValue, generation);
    }

    [Fact]
    public void Encodes_A_Window_Straddling_The_Seam()
    {
        var viewport = new Viewport(ulong.MaxValue - 9, ulong.MaxValue - 9, Ui, Ui);
        var universe = new Universe();
        universe.Reset([new Cell(ulong.MaxValue, ulong.MaxValue), new Cell(0, 0)]);

        (Viewport read, _, byte[] bitmap) = ViewportFrame.Read(Encode(universe, viewport, 1));

        Assert.True(Viewport.IsSet(bitmap, read.BitIndex(9, 9)));
        Assert.True(Viewport.IsSet(bitmap, read.BitIndex(10, 10)));
    }

    [Fact]
    public void Pre_Rendered_Bitmap_Overload_Matches_The_Direct_One()
    {
        var viewport = new Viewport(500, 600, Ui, Ui);
        var universe = new Universe();
        universe.Reset([viewport.CellAt(3, 4), viewport.CellAt(80, 90)]);

        byte[] direct = Encode(universe, viewport, 7);

        byte[] bitmap = new byte[viewport.BitmapByteCount];
        universe.RenderTo(viewport, bitmap);

        var buffer = new ArrayBufferWriter<byte>();
        ViewportFrame.Write(buffer, viewport, 7, bitmap);

        Assert.Equal(direct, buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void Rejects_A_Truncated_Payload()
    {
        var viewport = new Viewport(0, 0, Ui, Ui);
        byte[] frame = Encode(new Universe(), viewport, 0);

        Assert.Throws<ProtocolException>(() => ViewportFrame.Read(frame.AsSpan(0, frame.Length - 1)));
    }

    [Fact]
    public void Rejects_A_Payload_Shorter_Than_The_Header()
    {
        Assert.Throws<ProtocolException>(() => ViewportFrame.Read(new byte[10]));
    }

    [Fact]
    public void Rejects_A_Declared_Empty_Window()
    {
        // All-zero header declares a 0x0 viewport, which no legitimate writer
        // produces and Viewport itself would reject.
        Assert.Throws<ProtocolException>(() => ViewportFrame.Read(new byte[ViewportFrame.HeaderLength]));
    }

    [Fact]
    public void Travels_Inside_A_Frame()
    {
        var viewport = new Viewport(1UL << 63, 1UL << 63, Ui, Ui);
        var universe = new Universe();
        universe.Reset([viewport.CellAt(10, 10)]);

        var buffer = new ArrayBufferWriter<byte>();
        var payload = new ArrayBufferWriter<byte>();
        ViewportFrame.Write(payload, universe, viewport, 5);
        FrameCodec.Write(buffer, FrameType.Viewport, payload.WrittenSpan);

        var sequence = new ReadOnlySequence<byte>(buffer.WrittenSpan.ToArray());

        Assert.True(FrameCodec.TryRead(ref sequence, out FrameType type, out byte[] read));
        Assert.Equal(FrameType.Viewport, type);

        (Viewport decoded, ulong generation, byte[] bitmap) = ViewportFrame.Read(read);

        Assert.Equal(viewport, decoded);
        Assert.Equal(5UL, generation);
        Assert.True(Viewport.IsSet(bitmap, decoded.BitIndex(10, 10)));
    }

    [Fact]
    public void A_Full_Frame_Is_Far_Below_The_Payload_Limit()
    {
        var viewport = new Viewport(0, 0, Ui, Ui);

        Assert.True(ViewportFrame.PayloadLength(viewport) < FrameCodec.MaxPayloadLength / 100,
            "The frame limit should stay generous relative to the largest legitimate frame.");
    }
}
