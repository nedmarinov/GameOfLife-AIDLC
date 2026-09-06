using GameOfLife.Core;

namespace GameOfLife.Core.Tests;

/// <summary>Well-known Life patterns as offsets from a placement origin.</summary>
internal static class Patterns
{
    /// <summary>
    /// The standard glider. Travels one cell diagonally (+1,+1) every four
    /// generations, which is what makes it the right probe for the seam: its
    /// displacement is exactly predictable, so an incorrect wrap shows up as a
    /// wrong position rather than a plausible-looking mess.
    /// <code>
    /// . O .
    /// . . O
    /// O O O
    /// </code>
    /// </summary>
    public static readonly (long Dx, long Dy)[] Glider =
        [(1, 0), (2, 1), (0, 2), (1, 2), (2, 2)];

    /// <summary>2x2 still life.</summary>
    public static readonly (long Dx, long Dy)[] Block =
        [(0, 0), (1, 0), (0, 1), (1, 1)];

    /// <summary>Horizontal blinker; period 2.</summary>
    public static readonly (long Dx, long Dy)[] BlinkerHorizontal =
        [(0, 0), (1, 0), (2, 0)];

    /// <summary>Vertical blinker; the other phase of <see cref="BlinkerHorizontal"/>.</summary>
    public static readonly (long Dx, long Dy)[] BlinkerVertical =
        [(1, -1), (1, 0), (1, 1)];

    /// <summary>Places a pattern at an absolute origin, wrapping the universe.</summary>
    public static IEnumerable<Cell> At(this (long Dx, long Dy)[] pattern, Cell origin) =>
        pattern.Select(p => origin.Offset(p.Dx, p.Dy));

    public static IEnumerable<Cell> At(this (long Dx, long Dy)[] pattern, ulong x, ulong y) =>
        pattern.At(new Cell(x, y));
}

internal static class UniverseAssert
{
    /// <summary>
    /// Asserts the live set matches exactly, order-independently, with a
    /// failure message that names the actual differences rather than dumping
    /// two unordered lists.
    /// </summary>
    public static void LiveCellsAre(IEnumerable<Cell> expected, Universe universe)
    {
        HashSet<Cell> want = [.. expected];
        HashSet<Cell> got = [.. universe.LiveCells];

        if (want.SetEquals(got)) return;

        IEnumerable<Cell> missing = want.Except(got).OrderBy(c => c.Y).ThenBy(c => c.X);
        IEnumerable<Cell> extra = got.Except(want).OrderBy(c => c.Y).ThenBy(c => c.X);

        Assert.Fail(
            $"Live set mismatch at generation {universe.Generation}.\n" +
            $"  missing ({missing.Count()}): {string.Join(", ", missing)}\n" +
            $"  unexpected ({extra.Count()}): {string.Join(", ", extra)}");
    }
}
