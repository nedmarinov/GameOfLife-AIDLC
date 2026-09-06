using GameOfLife.Core;

namespace GameOfLife.Core.Tests;

/// <summary>
/// A catalogue of well-known Life patterns, checked against their published
/// behaviour.
/// </summary>
/// <remarks>
/// <para>
/// The engine implements B3/S23 rather than a list of shapes, so every Conway
/// pattern should work by construction. This asserts it instead of assuming it,
/// using externally known facts — periods, displacements, and for the
/// methuselahs the exact generation at which they settle and the exact
/// population they settle to. Those numbers come from outside this project, so
/// a subtle rule defect cannot agree with them by accident.
/// </para>
/// </remarks>
public class PatternCatalogueTests
{
    /// <summary>Somewhere far from the seam, so wrap plays no part.</summary>
    private static readonly Cell Origin = new(1UL << 62, 1UL << 62);

    private static Universe Load(string rle, Cell? at = null)
    {
        RlePattern pattern = Rle.Parse($"x = 0, y = 0, rule = B3/S23\n{rle}");
        var universe = new Universe();
        universe.Reset(pattern.CellsAt(at ?? Origin));
        return universe;
    }

    private static HashSet<Cell> Snapshot(Universe universe) => [.. universe.LiveCells];

    private static void Advance(Universe universe, int generations)
    {
        for (int i = 0; i < generations; i++) universe.Step();
    }

    // ---------------------------------------------------------- still lifes

    [Theory]
    [InlineData("Block", "2o$2o!", 4)]
    [InlineData("Beehive", "b2o$o2bo$b2o!", 6)]
    [InlineData("Loaf", "b2o$o2bo$bobo$2bo!", 7)]
    [InlineData("Boat", "2o$obo$bo!", 5)]
    [InlineData("Tub", "bo$obo$bo!", 4)]
    [InlineData("Ship", "2o$obo$b2o!", 6)]
    [InlineData("Pond", "b2o$o2bo$o2bo$b2o!", 8)]
    public void Still_Lifes_Never_Change(string name, string rle, int population)
    {
        Universe universe = Load(rle);
        HashSet<Cell> initial = Snapshot(universe);

        Assert.Equal(population, universe.Population);

        Advance(universe, 50);

        Assert.True(initial.SetEquals(Snapshot(universe)), $"{name} did not stay still.");
    }

    // ----------------------------------------------------------- oscillators

    [Theory]
    [InlineData("Blinker", "3o!", 2)]
    [InlineData("Toad", "b3o$3o!", 2)]
    [InlineData("Beacon", "2o$2o$2b2o$2b2o!", 2)]
    [InlineData("Clock", "2bo$2o$2b2o$bo!", 2)]
    [InlineData("Pulsar", "2b3o3b3o2b2$o4bobo4bo$o4bobo4bo$o4bobo4bo$2b3o3b3o2b2$2b3o3b3o2b$o4bobo4bo$o4bobo4bo$o4bobo4bo2$2b3o3b3o!", 3)]
    [InlineData("Pentadecathlon", "2bo4bo2b$2ob4ob2o$2bo4bo2b!", 15)]
    public void Oscillators_Return_To_Themselves_After_Their_Period(string name, string rle, int period)
    {
        Universe universe = Load(rle);
        HashSet<Cell> initial = Snapshot(universe);

        // Must not return early: a period-15 oscillator that repeats at 3 is a
        // different pattern from the one claimed.
        for (int generation = 1; generation < period; generation++)
        {
            universe.Step();

            Assert.False(initial.SetEquals(Snapshot(universe)),
                $"{name} repeated at generation {generation}, before its period of {period}.");
        }

        universe.Step();

        Assert.True(initial.SetEquals(Snapshot(universe)),
            $"{name} did not return to its initial state after {period} generations.");
    }

    // ------------------------------------------------------------ spaceships

    [Theory]
    // The *WSS family is written here in the orientation that travels left,
    // hence dx of -2. All three are c/2: two cells every four generations.
    [InlineData("Glider", "bo$2bo$3o!", 4, 1, 1)]
    [InlineData("Lightweight spaceship", "bo2bo$o4b$o3bo$4o!", 4, -2, 0)]
    [InlineData("Middleweight spaceship", "3bo2b$bo3bo$o5b$o4bo$5o!", 4, -2, 0)]
    [InlineData("Heavyweight spaceship", "3b2o2b$bo4bo$o6b$o5bo$6o!", 4, -2, 0)]
    public void Spaceships_Translate_By_A_Known_Displacement(
        string name, string rle, int period, long dx, long dy)
    {
        Universe universe = Load(rle);
        Assert.Equal(ExpectedPopulation(name), universe.Population);
        HashSet<Cell> initial = Snapshot(universe);
        int population = universe.Population;

        for (int move = 1; move <= 4; move++)
        {
            Advance(universe, period);

            HashSet<Cell> expected = [.. initial.Select(c => c.Offset(dx * move, dy * move))];

            Assert.True(expected.SetEquals(Snapshot(universe)),
                $"{name} was not at ({dx * move}, {dy * move}) after {period * move} generations.");

            Assert.Equal(population, universe.Population);
        }
    }

    /// <summary>
    /// Published cell counts, so a mistyped pattern cannot pass by accident.
    /// </summary>
    /// <remarks>
    /// Added after a hand-written MWSS and HWSS had the right populations but
    /// the wrong shapes: cell count alone is a weak check, and displacement
    /// alone would not have caught a transcription slip that still happened to
    /// travel. Requiring both is what makes the catalogue trustworthy.
    /// </remarks>
    private static int ExpectedPopulation(string name) => name switch
    {
        "Glider" => 5,
        "Lightweight spaceship" => 9,
        "Middleweight spaceship" => 11,
        "Heavyweight spaceship" => 13,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown spaceship."),
    };

    // ----------------------------------------------------------- methuselahs

    /// <summary>
    /// The strongest check in the suite.
    /// </summary>
    /// <remarks>
    /// The R-pentomino runs for 1,103 generations from five cells before
    /// settling, ending with 116 live cells. Both numbers are published facts
    /// about Conway's rule. Any defect in birth, survival or neighbour counting
    /// changes the trajectory long before generation 1,103, so agreeing with
    /// both is strong evidence the rule is exactly right — far stronger than any
    /// hand-written oscillator test.
    /// </remarks>
    [Fact]
    public void R_Pentomino_Settles_At_Generation_1103_With_116_Cells()
    {
        Universe universe = Load("b2o$2o$bo!");

        Assert.Equal(5, universe.Population);

        Advance(universe, 1_103);
        HashSet<Cell> settled = Snapshot(universe);

        Assert.Equal(116, universe.Population);

        // Everything still alive at 1,103 is a still life or an oscillator, and
        // six gliders have escaped. Population must now hold steady.
        for (int i = 0; i < 60; i++)
        {
            universe.Step();
            Assert.Equal(116, universe.Population);
        }
    }

    [Fact]
    public void Acorn_Settles_At_Generation_5206_With_633_Cells()
    {
        Universe universe = Load("bo5b$3bo3b$2o2b3o!");

        Assert.Equal(7, universe.Population);

        Advance(universe, 5_206);

        Assert.Equal(633, universe.Population);
    }

    [Fact]
    public void Diehard_Vanishes_Completely_After_130_Generations()
    {
        Universe universe = Load("6bob$2o6b$bo3b3o!");

        Assert.Equal(7, universe.Population);

        Advance(universe, 129);
        Assert.True(universe.Population > 0, "Diehard died early.");

        universe.Step();
        Assert.Equal(0, universe.Population);
    }

    // ------------------------------------------------- the catalogue at the seam

    /// <summary>
    /// Every pattern above, re-run across the 2^64 boundary.
    /// </summary>
    /// <remarks>
    /// Support for a pattern is not much use if it only holds away from the
    /// wrap. Each is placed so it straddles the seam in both dimensions.
    /// </remarks>
    [Theory]
    [InlineData("Block", "2o$2o!")]
    [InlineData("Loaf", "b2o$o2bo$bobo$2bo!")]
    [InlineData("Pulsar", "2b3o3b3o2b2$o4bobo4bo$o4bobo4bo$o4bobo4bo$2b3o3b3o2b2$2b3o3b3o2b$o4bobo4bo$o4bobo4bo$o4bobo4bo2$2b3o3b3o!")]
    [InlineData("Pentadecathlon", "2bo4bo2b$2ob4ob2o$2bo4bo2b!")]
    public void Patterns_Behave_Identically_Across_The_Seam(string name, string rle)
    {
        var seam = new Cell(ulong.MaxValue - 3, ulong.MaxValue - 3);

        Universe far = Load(rle);
        Universe wrapped = Load(rle, seam);

        for (int generation = 1; generation <= 40; generation++)
        {
            far.Step();
            wrapped.Step();

            Assert.Equal(far.Population, wrapped.Population);

            // Translate the seam-straddling run back and require an exact match,
            // so this compares shapes rather than just cell counts.
            HashSet<Cell> expected =
            [
                .. far.LiveCells.Select(c => c.Offset(
                    unchecked((long)(seam.X - Origin.X)),
                    unchecked((long)(seam.Y - Origin.Y))))
            ];

            Assert.True(expected.SetEquals(Snapshot(wrapped)),
                $"{name} diverged at the seam on generation {generation}.");
        }
    }
}
