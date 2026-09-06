using GameOfLife.Core;

namespace GameOfLife.Core.Tests;

public class ViewportTests
{
    private const int Ui = 100;

    [Fact]
    public void Hundred_Square_Viewport_Is_Exactly_1250_Bytes()
    {
        var viewport = new Viewport(0, 0, Ui, Ui);

        Assert.Equal(10_000, viewport.CellCount);
        Assert.Equal(1_250, viewport.BitmapByteCount);
    }

    [Fact]
    public void Rejects_Non_Positive_Dimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Viewport(0, 0, 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Viewport(0, 0, 10, -1));
    }

    [Fact]
    public void Locates_Cells_Inside_And_Rejects_Cells_Outside()
    {
        var viewport = new Viewport(1_000, 2_000, Ui, Ui);

        Assert.True(viewport.TryLocate(new Cell(1_000, 2_000), out int x, out int y));
        Assert.Equal((0, 0), (x, y));

        Assert.True(viewport.TryLocate(new Cell(1_099, 2_099), out x, out y));
        Assert.Equal((99, 99), (x, y));

        Assert.False(viewport.TryLocate(new Cell(1_100, 2_000), out _, out _));
        Assert.False(viewport.TryLocate(new Cell(999, 2_000), out _, out _));
        Assert.False(viewport.TryLocate(new Cell(1_000, 2_100), out _, out _));
    }

    [Fact]
    public void Locates_Cells_In_A_Viewport_Straddling_The_Seam()
    {
        // Window starts ten cells before the wrap point, so its right-hand
        // ninety columns live past zero.
        var viewport = new Viewport(ulong.MaxValue - 9, ulong.MaxValue - 9, Ui, Ui);

        Assert.True(viewport.TryLocate(new Cell(ulong.MaxValue - 9, ulong.MaxValue - 9), out int x, out int y));
        Assert.Equal((0, 0), (x, y));

        Assert.True(viewport.TryLocate(new Cell(ulong.MaxValue, ulong.MaxValue), out x, out y));
        Assert.Equal((9, 9), (x, y));

        // The cell immediately across the seam is local (10, 10).
        Assert.True(viewport.TryLocate(new Cell(0, 0), out x, out y));
        Assert.Equal((10, 10), (x, y));

        Assert.True(viewport.TryLocate(new Cell(89, 89), out x, out y));
        Assert.Equal((99, 99), (x, y));

        Assert.False(viewport.TryLocate(new Cell(90, 0), out _, out _));
    }

    [Fact]
    public void CellAt_Is_The_Inverse_Of_TryLocate_Across_The_Seam()
    {
        var viewport = new Viewport(ulong.MaxValue - 4, 17, Ui, Ui);

        for (int y = 0; y < Ui; y += 7)
        {
            for (int x = 0; x < Ui; x += 7)
            {
                Cell absolute = viewport.CellAt(x, y);

                Assert.True(viewport.TryLocate(absolute, out int rx, out int ry));
                Assert.Equal((x, y), (rx, ry));
            }
        }
    }

    [Fact]
    public void Pan_Wraps_The_Origin()
    {
        var viewport = new Viewport(0, 0, Ui, Ui);

        Viewport back = viewport.Pan(-1, -1);
        Assert.Equal(ulong.MaxValue, back.OriginX);
        Assert.Equal(ulong.MaxValue, back.OriginY);

        Viewport forward = back.Pan(1, 1);
        Assert.Equal(0UL, forward.OriginX);
        Assert.Equal(0UL, forward.OriginY);
    }

    [Fact]
    public void Render_Sets_Exactly_The_Visible_Live_Cells()
    {
        var universe = new Universe();
        var viewport = new Viewport(1_000, 1_000, Ui, Ui);

        universe.Reset(
        [
            new Cell(1_000, 1_000),   // local (0,0)
            new Cell(1_050, 1_010),   // local (50,10)
            new Cell(1_099, 1_099),   // local (99,99)
            new Cell(5_000, 5_000),   // outside the window
        ]);

        Span<byte> bitmap = stackalloc byte[viewport.BitmapByteCount];
        universe.RenderTo(viewport, bitmap);

        Assert.True(Viewport.IsSet(bitmap, viewport.BitIndex(0, 0)));
        Assert.True(Viewport.IsSet(bitmap, viewport.BitIndex(50, 10)));
        Assert.True(Viewport.IsSet(bitmap, viewport.BitIndex(99, 99)));

        int set = 0;
        for (int i = 0; i < viewport.CellCount; i++)
            if (Viewport.IsSet(bitmap, i)) set++;

        Assert.Equal(3, set);
    }

    [Fact]
    public void Render_Handles_A_Viewport_Across_The_Seam()
    {
        var universe = new Universe();
        var viewport = new Viewport(ulong.MaxValue - 9, ulong.MaxValue - 9, Ui, Ui);

        universe.Reset(Patterns.Block.At(new Cell(ulong.MaxValue, ulong.MaxValue)));

        Span<byte> bitmap = stackalloc byte[viewport.BitmapByteCount];
        universe.RenderTo(viewport, bitmap);

        // The block spans the wrap corner, so it renders as a contiguous 2x2
        // at local (9,9)-(10,10) with no discontinuity.
        Assert.True(Viewport.IsSet(bitmap, viewport.BitIndex(9, 9)));
        Assert.True(Viewport.IsSet(bitmap, viewport.BitIndex(10, 9)));
        Assert.True(Viewport.IsSet(bitmap, viewport.BitIndex(9, 10)));
        Assert.True(Viewport.IsSet(bitmap, viewport.BitIndex(10, 10)));

        int set = 0;
        for (int i = 0; i < viewport.CellCount; i++)
            if (Viewport.IsSet(bitmap, i)) set++;

        Assert.Equal(4, set);
    }

    [Fact]
    public void Render_Is_Fixed_Size_Regardless_Of_Population()
    {
        var viewport = new Viewport(0, 0, Ui, Ui);
        var empty = new Universe();

        var full = new Universe();
        full.Reset(Enumerable.Range(0, Ui)
            .SelectMany(y => Enumerable.Range(0, Ui).Select(x => new Cell((ulong)x, (ulong)y))));

        Span<byte> a = stackalloc byte[viewport.BitmapByteCount];
        Span<byte> b = stackalloc byte[viewport.BitmapByteCount];

        empty.RenderTo(viewport, a);
        full.RenderTo(viewport, b);

        Assert.Equal(1_250, viewport.BitmapByteCount);
        Assert.All(a.ToArray(), byteValue => Assert.Equal(0, byteValue));
        Assert.All(b.ToArray(), byteValue => Assert.Equal(0xFF, byteValue));
    }

    [Fact]
    public void Render_Rejects_An_Undersized_Destination()
    {
        var universe = new Universe();
        var viewport = new Viewport(0, 0, Ui, Ui);
        byte[] tooSmall = new byte[viewport.BitmapByteCount - 1];

        Assert.Throws<ArgumentException>(() => universe.RenderTo(viewport, tooSmall));
    }
}
