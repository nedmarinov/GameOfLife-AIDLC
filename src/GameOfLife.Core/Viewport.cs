namespace GameOfLife.Core;

/// <summary>
/// A rectangular window into the universe, addressed by an absolute
/// <see cref="ulong"/> origin.
/// </summary>
/// <remarks>
/// The 100x100 UI is a viewport, not a smaller universe. Containment is a
/// subtraction and one unsigned comparison, which is what makes a window
/// straddling the 2^64 seam work with no special case: the subtraction wraps,
/// so a cell just past <see cref="ulong.MaxValue"/> yields a small positive
/// offset exactly as a cell just past any other coordinate would.
/// </remarks>
public readonly record struct Viewport
{
    public Viewport(ulong originX, ulong originY, int width, int height, int zoom = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegative(zoom);

        if (zoom > MaxZoomFor(width, height))
        {
            throw new ArgumentOutOfRangeException(
                nameof(zoom), zoom,
                $"A {width}x{height} window cannot zoom past {MaxZoomFor(width, height)} " +
                "without covering more than the universe holds.");
        }

        OriginX = originX;
        OriginY = originY;
        Width = width;
        Height = height;
        Zoom = zoom;
    }

    /// <summary>Absolute X coordinate of the window's left column.</summary>
    public ulong OriginX { get; init; }

    /// <summary>Absolute Y coordinate of the window's top row.</summary>
    public ulong OriginY { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    /// <summary>
    /// How many universe cells each displayed cell stands for, as a power of
    /// two: <c>1 &lt;&lt; Zoom</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Zoom 0 is one cell per displayed cell. Higher values zoom <em>out</em>,
    /// each displayed cell covering a 2^Zoom square block and lighting up if
    /// anything in that block is alive. At the maximum the whole 2^64 x 2^64
    /// universe fits in the window, which is the only way to see that the
    /// universe really is that large rather than merely claimed to be.
    /// </para>
    /// <para>
    /// There is deliberately no zoom below 0. A cell is the smallest thing
    /// there is, so magnifying past 1:1 would show no more information — it
    /// would only shrink the visible area, which is the opposite of what a
    /// 2^64 universe needs.
    /// </para>
    /// <para>
    /// A power of two rather than an arbitrary factor keeps the arithmetic
    /// exact and cheap: the division that maps a cell to its block is a shift,
    /// and no rounding can put a cell in the wrong block.
    /// </para>
    /// </remarks>
    public int Zoom { get; init; }

    /// <summary>Universe cells per displayed cell along one axis.</summary>
    public ulong Scale => 1UL << Zoom;

    /// <summary>Universe cells spanned horizontally.</summary>
    public ulong CoverageWidth => (ulong)Width << Zoom;

    /// <summary>Universe cells spanned vertically.</summary>
    public ulong CoverageHeight => (ulong)Height << Zoom;

    /// <summary>The largest zoom this window can take without overflowing.</summary>
    public int MaxZoom => MaxZoomFor(Width, Height);

    /// <summary>
    /// The largest zoom at which coverage still fits in a <see cref="ulong"/>.
    /// </summary>
    /// <remarks>
    /// Beyond this, <c>Width &lt;&lt; Zoom</c> wraps and the containment test
    /// silently starts accepting cells outside the window — a defect that would
    /// look like random cells appearing at extreme zoom rather than like an
    /// overflow.
    /// </remarks>
    public static int MaxZoomFor(int width, int height)
    {
        int widest = Math.Max(width, height);
        int bits = 64 - System.Numerics.BitOperations.LeadingZeroCount((ulong)widest);

        return 64 - bits;
    }

    /// <summary>Total cells in the window.</summary>
    public int CellCount => Width * Height;

    /// <summary>Bytes needed for a one-bit-per-cell bitmap of this window.</summary>
    public int BitmapByteCount => (CellCount + 7) / 8;

    /// <summary>
    /// Maps an absolute cell to window-local coordinates, if it is visible.
    /// </summary>
    public bool TryLocate(in Cell cell, out int localX, out int localY)
    {
        unchecked
        {
            // Both subtractions wrap, so a window spanning the seam needs no
            // special handling. The unsigned compare rejects everything
            // "behind" the origin without a second bounds test.
            ulong dx = cell.X - OriginX;
            ulong dy = cell.Y - OriginY;

            if (dx < CoverageWidth && dy < CoverageHeight)
            {
                // A shift, not a division: exact, and it cannot round a cell
                // into a neighbouring block.
                localX = (int)(dx >> Zoom);
                localY = (int)(dy >> Zoom);
                return true;
            }
        }

        localX = 0;
        localY = 0;
        return false;
    }

    /// <summary>
    /// Maps window-local coordinates back to an absolute cell.
    /// </summary>
    /// <remarks>
    /// When zoomed out this is the <em>top-left</em> cell of the block that the
    /// displayed cell stands for, so an edit at any zoom lands on a definite,
    /// predictable cell rather than an arbitrary one inside the block.
    /// </remarks>
    public Cell CellAt(int localX, int localY) => new(
        unchecked(OriginX + ((ulong)localX << Zoom)),
        unchecked(OriginY + ((ulong)localY << Zoom)));

    /// <summary>
    /// Moves the window by a signed delta in <em>displayed</em> cells.
    /// </summary>
    /// <remarks>
    /// Scaled by the zoom, so a keypress moves the window by what the user sees
    /// rather than by a distance that shrinks to nothing as they zoom out.
    /// </remarks>
    public Viewport Pan(long dx, long dy) => this with
    {
        OriginX = unchecked(OriginX + ((ulong)dx << Zoom)),
        OriginY = unchecked(OriginY + ((ulong)dy << Zoom)),
    };

    /// <summary>
    /// Changes zoom by <paramref name="delta"/>, keeping the window's centre
    /// fixed.
    /// </summary>
    /// <remarks>
    /// Zooming about the centre rather than the origin is what makes it feel
    /// like a camera: the thing being looked at stays put instead of sliding
    /// off toward a corner.
    /// </remarks>
    public Viewport ZoomBy(int delta)
    {
        int target = Math.Clamp(Zoom + delta, 0, MaxZoom);
        if (target == Zoom) return this;

        // Hold the centre: find it at the old scale, then re-derive an origin
        // that puts it in the middle again at the new one.
        ulong centreX = unchecked(OriginX + (CoverageWidth >> 1));
        ulong centreY = unchecked(OriginY + (CoverageHeight >> 1));

        ulong halfWidth = ((ulong)Width << target) >> 1;
        ulong halfHeight = ((ulong)Height << target) >> 1;

        return this with
        {
            Zoom = target,
            OriginX = unchecked(centreX - halfWidth),
            OriginY = unchecked(centreY - halfHeight),
        };
    }

    /// <summary>Bit index within a rendered bitmap for a window-local cell.</summary>
    public int BitIndex(int localX, int localY) => (localY * Width) + localX;

    /// <summary>Reads one cell out of a rendered bitmap.</summary>
    public static bool IsSet(ReadOnlySpan<byte> bitmap, int bitIndex) =>
        (bitmap[bitIndex >> 3] & (1 << (bitIndex & 7))) != 0;
}
