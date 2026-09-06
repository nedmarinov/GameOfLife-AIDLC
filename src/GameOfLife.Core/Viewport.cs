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
    public Viewport(ulong originX, ulong originY, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        OriginX = originX;
        OriginY = originY;
        Width = width;
        Height = height;
    }

    /// <summary>Absolute X coordinate of the window's left column.</summary>
    public ulong OriginX { get; init; }

    /// <summary>Absolute Y coordinate of the window's top row.</summary>
    public ulong OriginY { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

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

            if (dx < (ulong)(uint)Width && dy < (ulong)(uint)Height)
            {
                localX = (int)dx;
                localY = (int)dy;
                return true;
            }
        }

        localX = 0;
        localY = 0;
        return false;
    }

    /// <summary>Maps window-local coordinates back to an absolute cell.</summary>
    public Cell CellAt(int localX, int localY) => new(
        unchecked(OriginX + (ulong)localX),
        unchecked(OriginY + (ulong)localY));

    /// <summary>Moves the window by a signed delta, wrapping the universe.</summary>
    public Viewport Pan(long dx, long dy) => this with
    {
        OriginX = unchecked(OriginX + (ulong)dx),
        OriginY = unchecked(OriginY + (ulong)dy),
    };

    /// <summary>Bit index within a rendered bitmap for a window-local cell.</summary>
    public int BitIndex(int localX, int localY) => (localY * Width) + localX;

    /// <summary>Reads one cell out of a rendered bitmap.</summary>
    public static bool IsSet(ReadOnlySpan<byte> bitmap, int bitIndex) =>
        (bitmap[bitIndex >> 3] & (1 << (bitIndex & 7))) != 0;
}
