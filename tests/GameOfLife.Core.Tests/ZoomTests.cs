using GameOfLife.Core;

namespace GameOfLife.Core.Tests;

/// <summary>
/// Zooming out until the whole 2^64 x 2^64 universe fits in one window.
/// </summary>
public class ZoomTests
{
    private const int Ui = 100;

    private static Viewport Window(int zoom = 0, ulong originX = 0, ulong originY = 0) =>
        new(originX, originY, Ui, Ui, zoom);

    [Fact]
    public void Zoom_Zero_Is_One_Cell_Per_Displayed_Cell()
    {
        Viewport viewport = Window();

        Assert.Equal(1UL, viewport.Scale);
        Assert.Equal(100UL, viewport.CoverageWidth);
    }

    [Theory]
    [InlineData(0, 100UL)]
    [InlineData(1, 200UL)]
    [InlineData(8, 25_600UL)]
    [InlineData(20, 104_857_600UL)]
    public void Coverage_Doubles_With_Every_Zoom_Level(int zoom, ulong coverage)
    {
        Assert.Equal(coverage, Window(zoom).CoverageWidth);
    }

    /// <summary>
    /// The boundary that matters: one past the maximum, coverage would wrap.
    /// </summary>
    /// <remarks>
    /// If <c>Width &lt;&lt; Zoom</c> overflows, the containment test starts
    /// silently accepting cells outside the window, which would look like random
    /// cells appearing at extreme zoom rather than like an arithmetic fault. The
    /// constructor refuses instead.
    /// </remarks>
    [Fact]
    public void Zoom_Is_Capped_Where_Coverage_Would_Overflow()
    {
        int max = Viewport.MaxZoomFor(Ui, Ui);

        // 100 needs 7 bits, so 64 - 7 = 57 is the last safe shift.
        Assert.Equal(57, max);
        Assert.True(Window(max).CoverageWidth > 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => Window(max + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Viewport(0, 0, Ui, Ui, -1));
    }

    [Fact]
    public void At_Maximum_Zoom_The_Window_Covers_Most_Of_The_Universe()
    {
        Viewport viewport = Window(Viewport.MaxZoomFor(Ui, Ui));

        // 100 * 2^57 is within a factor of two of 2^64: the whole universe is
        // in view, give or take the rounding a power-of-two scale imposes.
        Assert.True(viewport.CoverageWidth > ulong.MaxValue / 2,
            $"Maximum zoom covers only {viewport.CoverageWidth} cells.");
    }

    [Fact]
    public void A_Block_Lights_Up_If_Anything_In_It_Is_Alive()
    {
        Viewport viewport = Window(zoom: 4);   // 16x16 cells per displayed cell
        var universe = new Universe();

        // One cell in the middle of the block at displayed (3, 5).
        universe.Reset([viewport.CellAt(3, 5).Offset(7, 9)]);

        byte[] bitmap = new byte[viewport.BitmapByteCount];
        universe.RenderTo(viewport, bitmap);

        Assert.True(Viewport.IsSet(bitmap, viewport.BitIndex(3, 5)));

        int lit = 0;
        for (int i = 0; i < viewport.CellCount; i++) if (Viewport.IsSet(bitmap, i)) lit++;
        Assert.Equal(1, lit);
    }

    [Fact]
    public void Many_Cells_In_One_Block_Light_Exactly_One_Displayed_Cell()
    {
        Viewport viewport = Window(zoom: 3);   // 8x8 per displayed cell
        var universe = new Universe();

        // Fill the whole block at displayed (2, 2): 64 cells, one bit.
        Cell corner = viewport.CellAt(2, 2);
        universe.Reset(
            from y in Enumerable.Range(0, 8)
            from x in Enumerable.Range(0, 8)
            select corner.Offset(x, y));

        Assert.Equal(64, universe.Population);

        byte[] bitmap = new byte[viewport.BitmapByteCount];
        universe.RenderTo(viewport, bitmap);

        int lit = 0;
        for (int i = 0; i < viewport.CellCount; i++) if (Viewport.IsSet(bitmap, i)) lit++;

        Assert.Equal(1, lit);
        Assert.True(Viewport.IsSet(bitmap, viewport.BitIndex(2, 2)));
    }

    [Fact]
    public void CellAt_Returns_The_Blocks_Top_Left_So_Edits_Are_Predictable()
    {
        Viewport viewport = Window(zoom: 5, originX: 1_000, originY: 2_000);

        Assert.Equal(new Cell(1_000, 2_000), viewport.CellAt(0, 0));
        Assert.Equal(new Cell(1_000 + 32, 2_000), viewport.CellAt(1, 0));
        Assert.Equal(new Cell(1_000, 2_000 + 64), viewport.CellAt(0, 2));
    }

    [Fact]
    public void CellAt_And_TryLocate_Round_Trip_At_Every_Zoom()
    {
        for (int zoom = 0; zoom <= 20; zoom++)
        {
            Viewport viewport = Window(zoom, 1UL << 63, 1UL << 63);

            for (int y = 0; y < Ui; y += 13)
            {
                for (int x = 0; x < Ui; x += 13)
                {
                    Assert.True(viewport.TryLocate(viewport.CellAt(x, y), out int rx, out int ry));
                    Assert.Equal((x, y), (rx, ry));
                }
            }
        }
    }

    [Fact]
    public void Panning_Moves_By_Displayed_Cells_Not_Universe_Cells()
    {
        // Otherwise a keypress would move the window by an ever smaller
        // fraction of the view as the user zooms out, until it appeared stuck.
        Viewport viewport = Window(zoom: 10);

        Assert.Equal(1_024UL, viewport.Pan(1, 0).OriginX);
        Assert.Equal(unchecked(0UL - 1_024UL), viewport.Pan(-1, 0).OriginX);
    }

    [Fact]
    public void Zooming_Keeps_The_Centre_Of_The_Window_Fixed()
    {
        Viewport viewport = Window(zoom: 4, originX: 1UL << 63, originY: 1UL << 63);

        ulong centreX = viewport.OriginX + (viewport.CoverageWidth >> 1);
        ulong centreY = viewport.OriginY + (viewport.CoverageHeight >> 1);

        foreach (int delta in new[] { 1, 3, -2, 6, -8 })
        {
            viewport = viewport.ZoomBy(delta);

            Assert.Equal(centreX, viewport.OriginX + (viewport.CoverageWidth >> 1));
            Assert.Equal(centreY, viewport.OriginY + (viewport.CoverageHeight >> 1));
        }
    }

    [Fact]
    public void ZoomBy_Clamps_Instead_Of_Throwing()
    {
        // A client may send a large delta to jump to either extreme without
        // knowing the limit.
        Assert.Equal(0, Window(3).ZoomBy(-999).Zoom);
        Assert.Equal(Viewport.MaxZoomFor(Ui, Ui), Window(3).ZoomBy(999).Zoom);
    }

    [Fact]
    public void Zoom_Works_Across_The_Seam()
    {
        Viewport viewport = new(ulong.MaxValue - 511, ulong.MaxValue - 511, Ui, Ui, zoom: 4);
        var universe = new Universe();

        // Either side of the wrap, 512 cells apart at zoom 4 -> 32 blocks apart.
        universe.Reset([new Cell(ulong.MaxValue - 511, ulong.MaxValue - 511), new Cell(0, 0)]);

        byte[] bitmap = new byte[viewport.BitmapByteCount];
        universe.RenderTo(viewport, bitmap);

        Assert.True(Viewport.IsSet(bitmap, viewport.BitIndex(0, 0)));
        Assert.True(Viewport.IsSet(bitmap, viewport.BitIndex(32, 32)));
    }

    [Fact]
    public void The_Whole_Universe_Fits_At_Maximum_Zoom()
    {
        Viewport viewport = Window(Viewport.MaxZoomFor(Ui, Ui));
        var universe = new Universe();

        // Cells spread to the far corners of the universe must all be visible
        // at once -- which is the entire point of zooming out this far.
        universe.Reset(
        [
            new Cell(0, 0),
            new Cell(ulong.MaxValue / 4, ulong.MaxValue / 4),
            new Cell(ulong.MaxValue / 2, ulong.MaxValue / 2),
        ]);

        byte[] bitmap = new byte[viewport.BitmapByteCount];
        universe.RenderTo(viewport, bitmap);

        int lit = 0;
        for (int i = 0; i < viewport.CellCount; i++) if (Viewport.IsSet(bitmap, i)) lit++;

        Assert.Equal(3, lit);
    }
}
