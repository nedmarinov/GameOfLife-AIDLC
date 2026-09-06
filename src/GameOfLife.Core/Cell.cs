namespace GameOfLife.Core;

/// <summary>
/// A coordinate in the 2^64 x 2^64 toroidal universe.
/// </summary>
/// <remarks>
/// Wrapping is not implemented here; it is inherited from the machine. Because
/// each dimension is exactly 2^64 and coordinates are <see cref="ulong"/>,
/// stepping off one edge <em>is</em> unchecked integer overflow:
/// <c>ulong.MaxValue + 1 == 0</c>. There is deliberately no modulo, no bounds
/// check and no seam special case anywhere in this type — a cell at the origin
/// and a cell at <see cref="ulong.MaxValue"/> are neighbours by arithmetic, not
/// by a branch that someone has to remember to write.
/// </remarks>
public readonly record struct Cell(ulong X, ulong Y)
{
    /// <summary>Every cell on a torus has exactly eight neighbours.</summary>
    public const int NeighbourCount = 8;

    /// <summary>Translates by a signed delta, wrapping the universe.</summary>
    public Cell Offset(long dx, long dy) =>
        new(unchecked(X + (ulong)dx), unchecked(Y + (ulong)dy));

    /// <summary>
    /// Writes this cell's eight neighbours into <paramref name="destination"/>.
    /// </summary>
    /// <remarks>
    /// Takes a span rather than returning a collection so the hot path in
    /// <see cref="Universe.Step"/> can use a single stack buffer for the whole
    /// generation instead of allocating per cell.
    /// </remarks>
    public void WriteNeighbours(Span<Cell> destination)
    {
        if (destination.Length < NeighbourCount)
            throw new ArgumentException(
                $"Destination must hold at least {NeighbourCount} cells.", nameof(destination));

        unchecked
        {
            ulong xm = X - 1, xp = X + 1;
            ulong ym = Y - 1, yp = Y + 1;

            destination[0] = new Cell(xm, ym);
            destination[1] = new Cell(X,  ym);
            destination[2] = new Cell(xp, ym);
            destination[3] = new Cell(xm, Y);
            destination[4] = new Cell(xp, Y);
            destination[5] = new Cell(xm, yp);
            destination[6] = new Cell(X,  yp);
            destination[7] = new Cell(xp, yp);
        }
    }

    public override string ToString() => $"({X}, {Y})";
}
