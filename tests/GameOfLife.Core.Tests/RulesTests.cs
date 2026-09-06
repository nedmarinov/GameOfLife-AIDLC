using GameOfLife.Core;

namespace GameOfLife.Core.Tests;

/// <summary>Conway's B3/S23 rules, away from the seam.</summary>
public class RulesTests
{
    private static Universe Seeded(IEnumerable<Cell> cells)
    {
        var universe = new Universe();
        universe.Reset(cells);
        return universe;
    }

    [Fact]
    public void Empty_Universe_Stays_Empty()
    {
        var universe = new Universe();
        universe.Step();

        Assert.Equal(0, universe.Population);
        Assert.Equal(1UL, universe.Generation);
    }

    [Fact]
    public void Lone_Cell_Dies_Of_Underpopulation()
    {
        Universe universe = Seeded([new Cell(100, 100)]);
        universe.Step();

        Assert.Equal(0, universe.Population);
    }

    [Fact]
    public void Pair_Dies_Of_Underpopulation()
    {
        Universe universe = Seeded([new Cell(10, 10), new Cell(11, 10)]);
        universe.Step();

        Assert.Equal(0, universe.Population);
    }

    [Fact]
    public void Block_Is_A_Still_Life()
    {
        var origin = new Cell(500, 500);
        Universe universe = Seeded(Patterns.Block.At(origin));

        for (int i = 0; i < 10; i++) universe.Step();

        UniverseAssert.LiveCellsAre(Patterns.Block.At(origin), universe);
    }

    [Fact]
    public void Blinker_Has_Period_Two()
    {
        var origin = new Cell(50, 50);
        Universe universe = Seeded(Patterns.BlinkerHorizontal.At(origin));

        universe.Step();
        UniverseAssert.LiveCellsAre(Patterns.BlinkerVertical.At(origin), universe);

        universe.Step();
        UniverseAssert.LiveCellsAre(Patterns.BlinkerHorizontal.At(origin), universe);
    }

    [Fact]
    public void Glider_Translates_One_Diagonal_Step_Every_Four_Generations()
    {
        var origin = new Cell(1_000, 1_000);
        Universe universe = Seeded(Patterns.Glider.At(origin));

        for (int move = 1; move <= 5; move++)
        {
            for (int i = 0; i < 4; i++) universe.Step();

            UniverseAssert.LiveCellsAre(
                Patterns.Glider.At(origin.Offset(move, move)), universe);
        }
    }

    [Fact]
    public void Glider_Population_Is_Constant()
    {
        Universe universe = Seeded(Patterns.Glider.At(new Cell(0, 0)));

        for (int i = 0; i < 40; i++)
        {
            universe.Step();
            Assert.Equal(5, universe.Population);
        }
    }

    [Fact]
    public void Generation_Counter_Advances_By_One_Per_Step()
    {
        var universe = new Universe();

        for (ulong expected = 1; expected <= 25; expected++)
        {
            universe.Step();
            Assert.Equal(expected, universe.Generation);
        }
    }

    [Fact]
    public void Toggle_Flips_State_And_Reports_Result()
    {
        var universe = new Universe();
        var cell = new Cell(7, 9);

        Assert.False(universe.IsAlive(cell));
        Assert.True(universe.Toggle(cell));
        Assert.True(universe.IsAlive(cell));
        Assert.False(universe.Toggle(cell));
        Assert.False(universe.IsAlive(cell));
    }

    [Fact]
    public void Clear_Empties_The_Universe_And_Resets_Generation()
    {
        Universe universe = Seeded(Patterns.Block.At(new Cell(3, 3)));
        universe.Step();
        universe.Clear();

        Assert.Equal(0, universe.Population);
        Assert.Equal(0UL, universe.Generation);
    }
}
