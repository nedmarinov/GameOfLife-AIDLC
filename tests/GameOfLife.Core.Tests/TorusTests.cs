using GameOfLife.Core;

namespace GameOfLife.Core.Tests;

/// <summary>
/// The tests that prove the 2^64 x 2^64 torus claim rather than asserting it.
/// </summary>
/// <remarks>
/// Every test here operates at or across the <see cref="ulong.MaxValue"/> seam.
/// None of them can pass on a dense-grid implementation, and none can pass if
/// coordinates are signed or if wrap is done with modulo against a stored
/// dimension. They are the reason the representation is what it is.
/// </remarks>
public class TorusTests
{
    private static Universe Seeded(IEnumerable<Cell> cells)
    {
        var universe = new Universe();
        universe.Reset(cells);
        return universe;
    }

    [Fact]
    public void Origin_And_MaxValue_Corner_Are_Neighbours()
    {
        Span<Cell> neighbours = stackalloc Cell[Cell.NeighbourCount];
        new Cell(0, 0).WriteNeighbours(neighbours);

        Assert.Contains(new Cell(ulong.MaxValue, ulong.MaxValue), neighbours.ToArray());
        Assert.Contains(new Cell(0, ulong.MaxValue), neighbours.ToArray());
        Assert.Contains(new Cell(ulong.MaxValue, 0), neighbours.ToArray());
    }

    [Fact]
    public void Neighbours_Are_Eight_Distinct_Cells_At_The_Seam()
    {
        Span<Cell> neighbours = stackalloc Cell[Cell.NeighbourCount];
        new Cell(0, 0).WriteNeighbours(neighbours);

        Assert.Equal(Cell.NeighbourCount, neighbours.ToArray().Distinct().Count());
        Assert.DoesNotContain(new Cell(0, 0), neighbours.ToArray());
    }

    [Fact]
    public void Offset_Wraps_In_Both_Directions()
    {
        Assert.Equal(new Cell(0, 0), new Cell(ulong.MaxValue, ulong.MaxValue).Offset(1, 1));
        Assert.Equal(new Cell(ulong.MaxValue, ulong.MaxValue), new Cell(0, 0).Offset(-1, -1));
    }

    [Fact]
    public void Block_Straddling_The_Seam_Is_Still_A_Still_Life()
    {
        // Top-left at (MaxValue, MaxValue), so the block occupies all four
        // quadrants around the origin corner simultaneously.
        var origin = new Cell(ulong.MaxValue, ulong.MaxValue);
        Universe universe = Seeded(Patterns.Block.At(origin));

        Assert.Equal(4, universe.Population);

        for (int i = 0; i < 10; i++) universe.Step();

        UniverseAssert.LiveCellsAre(Patterns.Block.At(origin), universe);
    }

    [Fact]
    public void Blinker_Oscillates_Across_The_Seam()
    {
        var origin = new Cell(ulong.MaxValue - 1, 0);
        Universe universe = Seeded(Patterns.BlinkerHorizontal.At(origin));

        universe.Step();
        UniverseAssert.LiveCellsAre(Patterns.BlinkerVertical.At(origin), universe);

        universe.Step();
        UniverseAssert.LiveCellsAre(Patterns.BlinkerHorizontal.At(origin), universe);
    }

    /// <summary>
    /// The load-bearing test for the entire design.
    /// </summary>
    /// <remarks>
    /// A glider is placed two cells short of the far corner and run for twelve
    /// generations — three diagonal steps. It therefore crosses the
    /// <see cref="ulong.MaxValue"/> to zero boundary in both dimensions while
    /// moving, and is checked at every intermediate step, not just at the end.
    /// </remarks>
    [Fact]
    public void Glider_Crossing_The_MaxValue_Seam_Arrives_Intact()
    {
        var origin = new Cell(ulong.MaxValue - 1, ulong.MaxValue - 1);
        Universe universe = Seeded(Patterns.Glider.At(origin));

        for (int move = 1; move <= 3; move++)
        {
            for (int i = 0; i < 4; i++) universe.Step();

            UniverseAssert.LiveCellsAre(
                Patterns.Glider.At(origin.Offset(move, move)), universe);
            Assert.Equal(5, universe.Population);
        }

        // Every cell now sits just past zero, having entered from the far end
        // of the range. Coordinates this low are only reachable from a start
        // at ulong.MaxValue - 1 by wrapping.
        Assert.All(universe.LiveCells, cell =>
        {
            Assert.True(cell.X < 10, $"{cell} did not wrap in X");
            Assert.True(cell.Y < 10, $"{cell} did not wrap in Y");
        });
    }

    [Fact]
    public void Glider_Travelling_The_Long_Way_Reaches_Every_Quadrant_Boundary()
    {
        // Start just before the seam in X only, so the pattern wraps in one
        // dimension while running normally in the other. Asymmetric wrap is
        // where a hand-written modulo implementation typically breaks.
        var origin = new Cell(ulong.MaxValue - 3, 1_000);
        Universe universe = Seeded(Patterns.Glider.At(origin));

        for (int move = 1; move <= 8; move++)
        {
            for (int i = 0; i < 4; i++) universe.Step();

            UniverseAssert.LiveCellsAre(
                Patterns.Glider.At(origin.Offset(move, move)), universe);
        }
    }

    [Fact]
    public void Universe_Accepts_Coordinates_Across_The_Entire_Range()
    {
        // If any dimension constant existed, one of these would be rejected or
        // silently folded onto another.
        Cell[] extremes =
        [
            new(0, 0),
            new(ulong.MaxValue, 0),
            new(0, ulong.MaxValue),
            new(ulong.MaxValue, ulong.MaxValue),
            new(ulong.MaxValue / 2, ulong.MaxValue / 2),
            new(1UL << 63, 1UL << 63),
        ];

        Universe universe = Seeded(extremes);

        Assert.Equal(extremes.Length, universe.Population);
        foreach (Cell cell in extremes) Assert.True(universe.IsAlive(cell));
    }
}
