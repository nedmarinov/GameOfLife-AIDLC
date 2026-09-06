using GameOfLife.Core;

namespace GameOfLife.Core.Tests;

public class RleTests
{
    /// <summary>
    /// Locates the repository's patterns directory from the test binary, so the
    /// shipped example files are what gets tested rather than a copy.
    /// </summary>
    private static string PatternPath(string fileName)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "patterns")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "patterns", fileName);
    }

    // ------------------------------------------------------------ parsing

    [Fact]
    public void Parses_A_Blinker()
    {
        RlePattern pattern = Rle.Parse("x = 3, y = 1, rule = B3/S23\n3o!");

        Assert.Equal(3, pattern.Width);
        Assert.Equal(1, pattern.Height);
        Assert.Equal(3, pattern.Population);
        Assert.Equal([(0, 0), (1, 0), (2, 0)], pattern.Coordinates);
    }

    [Fact]
    public void Parses_Dead_Runs_And_Row_Breaks()
    {
        RlePattern pattern = Rle.Parse("x = 3, y = 3, rule = B3/S23\nbo$2bo$3o!");

        Assert.Equal([(1, 0), (2, 1), (0, 2), (1, 2), (2, 2)], pattern.Coordinates);
    }

    [Fact]
    public void Parses_Multi_Row_Skips()
    {
        // '3$' skips three rows, so the second cell lands on row 3.
        RlePattern pattern = Rle.Parse("x = 1, y = 4, rule = B3/S23\no3$o!");

        Assert.Equal([(0, 0), (0, 3)], pattern.Coordinates);
    }

    [Fact]
    public void Parses_A_Body_Split_Across_Lines()
    {
        string wrapped = "x = 36, y = 9, rule = B3/S23\n24bo$22bobo$12b2o6b2o12b2o$11bo3bo4b2o12b2o$2o8bo5bo3b2o$2o8bo3bob2o4b\nobo$10bo5bo7bo$11bo3bo$12b2o!";
        RlePattern pattern = Rle.Parse(wrapped);

        Assert.Equal(36, pattern.Population);
    }

    [Fact]
    public void Reads_Name_And_Free_Text_Comments()
    {
        RlePattern pattern = Rle.Parse("#N Blinker\n#C just a note\nx = 3, y = 1, rule = B3/S23\n3o!");

        Assert.Equal("Blinker", pattern.Name);
        Assert.Contains("just a note", pattern.Comments);
    }

    [Fact]
    public void Reads_Origin_And_Generation_From_Tagged_Comments()
    {
        RlePattern pattern = Rle.Parse(
            "#C origin: 18446744073709551615 42\n#C generation: 1234\nx = 1, y = 1, rule = B3/S23\no!");

        Assert.Equal(new Cell(ulong.MaxValue, 42), pattern.Origin);
        Assert.Equal(1234UL, pattern.Generation);
        Assert.Empty(pattern.Comments);
    }

    [Fact]
    public void Defaults_Origin_And_Generation_When_Absent()
    {
        RlePattern pattern = Rle.Parse("x = 1, y = 1, rule = B3/S23\no!");

        Assert.Equal(new Cell(0, 0), pattern.Origin);
        Assert.Equal(0UL, pattern.Generation);
    }

    [Fact]
    public void Preserves_Standard_Tags_Without_Reinterpreting_Them()
    {
        // '#O' means author in the RLE spec. It must not be read as an origin.
        RlePattern pattern = Rle.Parse("#O Bill Gosper, 1970\nx = 1, y = 1, rule = B3/S23\no!");

        Assert.Equal(new Cell(0, 0), pattern.Origin);
        Assert.Contains("O Bill Gosper, 1970", pattern.Comments);
    }

    [Fact]
    public void Accepts_The_Older_Survival_Birth_Rule_Notation()
    {
        RlePattern pattern = Rle.Parse("x = 1, y = 1, rule = 23/3\no!");

        Assert.Equal(1, pattern.Population);
    }

    [Fact]
    public void Rejects_A_Rule_The_Engine_Does_Not_Implement()
    {
        // HighLife. Simulating it as Conway would be confidently wrong output
        // with no error anywhere.
        NotSupportedException error = Assert.Throws<NotSupportedException>(
            () => Rle.Parse("x = 1, y = 1, rule = B36/S23\no!"));

        Assert.Contains("B36/S23", error.Message);
    }

    [Fact]
    public void Rejects_A_Missing_Header()
    {
        Assert.Throws<FormatException>(() => Rle.Parse("3o!"));
    }

    [Fact]
    public void Rejects_A_Malformed_Origin()
    {
        Assert.Throws<FormatException>(
            () => Rle.Parse("#C origin: nonsense\nx = 1, y = 1, rule = B3/S23\no!"));
    }

    // ------------------------------------------------------- round-tripping

    [Fact]
    public void Round_Trips_A_Glider()
    {
        var origin = new Cell(1_000, 2_000);
        var universe = new Universe();
        universe.Reset(Patterns.Glider.At(origin), generation: 77);

        RlePattern reparsed = Rle.Parse(Rle.Format(universe, "Glider"));

        Assert.Equal("Glider", reparsed.Name);
        Assert.Equal(77UL, reparsed.Generation);
        UniverseAssert.LiveCellsAre(reparsed.Cells, universe);
    }

    [Fact]
    public void Round_Trips_A_Pattern_Straddling_The_Seam()
    {
        // Naive min/max bounding would produce a box nearly 2^64 wide here.
        var origin = new Cell(ulong.MaxValue - 1, ulong.MaxValue - 1);
        var universe = new Universe();
        universe.Reset(Patterns.Glider.At(origin));

        string text = Rle.Format(universe);
        RlePattern reparsed = Rle.Parse(text);

        Assert.Equal(3, reparsed.Width);
        Assert.Equal(3, reparsed.Height);
        UniverseAssert.LiveCellsAre(reparsed.Cells, universe);
    }

    [Fact]
    public void Round_Trips_An_Empty_Universe()
    {
        var universe = new Universe();

        RlePattern reparsed = Rle.Parse(Rle.Format(universe));

        Assert.Equal(0, reparsed.Population);
    }

    [Fact]
    public void Round_Trips_A_Sparse_Scatter_With_Wide_Gaps()
    {
        var universe = new Universe();
        universe.Reset([new Cell(0, 0), new Cell(500, 0), new Cell(0, 300), new Cell(500, 300)]);

        RlePattern reparsed = Rle.Parse(Rle.Format(universe));

        Assert.Equal(501, reparsed.Width);
        Assert.Equal(301, reparsed.Height);
        UniverseAssert.LiveCellsAre(reparsed.Cells, universe);
    }

    [Fact]
    public void Output_Lines_Stay_Within_Seventy_Columns()
    {
        var universe = new Universe();
        universe.Reset(Rle.ParseFile(PatternPath("gosper-glider-gun.rle")).Cells);

        foreach (string line in Rle.Format(universe).Split('\n'))
            Assert.True(line.Length <= 70, $"Line exceeds 70 columns: '{line}'");
    }

    // ------------------------------------------------- the shipped examples

    [Fact]
    public void Gosper_Glider_Gun_File_Has_Thirty_Six_Live_Cells()
    {
        RlePattern gun = Rle.ParseFile(PatternPath("gosper-glider-gun.rle"));

        Assert.Equal("Gosper glider gun", gun.Name);
        Assert.Equal(36, gun.Population);
        Assert.Equal(36, gun.Width);
        Assert.Equal(9, gun.Height);
    }

    /// <summary>
    /// Checks the codec against an oracle outside this project.
    /// </summary>
    /// <remarks>
    /// Round-tripping through our own parser only proves the reader and writer
    /// agree with each other — they would agree just as happily on a shared
    /// misreading of the format. This asserts the writer reproduces the Gosper
    /// gun exactly as published, so the output is verified against the standard
    /// rather than against ourselves.
    /// </remarks>
    [Fact]
    public void Writer_Reproduces_The_Canonical_Published_Gun_Body()
    {
        const string canonical =
            "24bo$22bobo$12b2o6b2o12b2o$11bo3bo4b2o12b2o$2o8bo5bo3b2o$2o8bo3bob2o4bobo$"
            + "10bo5bo7bo$11bo3bo$12b2o!";

        var universe = new Universe();
        universe.Reset(Rle.ParseFile(PatternPath("gosper-glider-gun.rle")).Cells);

        string produced = string.Concat(Rle.Format(universe)
            .Split('\n')
            .Where(line => line.Length > 0 && !line.StartsWith('#') && !line.StartsWith("x =")));

        Assert.Equal(canonical, produced);
    }

    [Fact]
    public void Gosper_Glider_Gun_Grows_Without_Bound()
    {
        var universe = new Universe();
        universe.Reset(Rle.ParseFile(PatternPath("gosper-glider-gun.rle")).Cells);

        Assert.Equal(36, universe.Population);

        // The gun returns to its own shape every 30 generations while releasing
        // one glider, so population climbs by five per period.
        for (int i = 0; i < 30; i++) universe.Step();
        int afterOnePeriod = universe.Population;

        for (int i = 0; i < 30; i++) universe.Step();
        int afterTwoPeriods = universe.Population;

        Assert.True(afterOnePeriod > 36, $"Population did not grow: {afterOnePeriod}");
        Assert.True(afterTwoPeriods > afterOnePeriod,
            $"Population stopped growing: {afterOnePeriod} then {afterTwoPeriods}");
    }

    [Fact]
    public void Gosper_Glider_Gun_Is_Placed_Near_The_Centre_Of_The_Universe()
    {
        RlePattern gun = Rle.ParseFile(PatternPath("gosper-glider-gun.rle"));

        // Within one screen of 2^63, so the default viewport finds it.
        const ulong centre = 1UL << 63;
        Assert.InRange(gun.Origin.X, centre - 100, centre + 100);
        Assert.InRange(gun.Origin.Y, centre - 100, centre + 100);
    }

    [Fact]
    public void Shipped_Glider_File_Is_Positioned_To_Cross_The_Seam()
    {
        RlePattern glider = Rle.ParseFile(PatternPath("glider.rle"));
        var universe = new Universe();
        universe.Reset(glider.Cells);

        Assert.Equal(5, universe.Population);

        // Four generations of travel from MaxValue-1 carry it across zero.
        for (int i = 0; i < 4; i++) universe.Step();

        UniverseAssert.LiveCellsAre(
            Patterns.Glider.At(glider.Origin.Offset(1, 1)), universe);
    }

    [Fact]
    public void Every_Shipped_Pattern_Parses()
    {
        foreach (string file in Directory.GetFiles(Path.GetDirectoryName(PatternPath("x"))!, "*.rle"))
        {
            RlePattern pattern = Rle.ParseFile(file);
            Assert.True(pattern.Population > 0, $"{Path.GetFileName(file)} parsed as empty.");
        }
    }

    [Fact]
    public void File_Round_Trip_Survives_Disk()
    {
        string path = Path.Combine(Path.GetTempPath(), $"gol-{Guid.NewGuid():N}.rle");

        try
        {
            var universe = new Universe();
            universe.Reset(Rle.ParseFile(PatternPath("gosper-glider-gun.rle")).Cells, generation: 500);

            Rle.WriteFile(universe, path, "Round trip");
            RlePattern reloaded = Rle.ParseFile(path);

            Assert.Equal(500UL, reloaded.Generation);
            UniverseAssert.LiveCellsAre(reloaded.Cells, universe);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
