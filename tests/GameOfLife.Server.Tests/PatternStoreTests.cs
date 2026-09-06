using GameOfLife.Core;
using GameOfLife.Server;

namespace GameOfLife.Server.Tests;

/// <summary>
/// Containment of client-supplied file names.
/// </summary>
/// <remarks>
/// The file name in a load or save request comes from a network peer. Handing
/// it to the filesystem unchecked would let any connected client read arbitrary
/// files, and — worse — overwrite them.
/// </remarks>
public class PatternStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"gol-store-{Guid.NewGuid():N}");
    private readonly PatternStore _store;

    public PatternStoreTests()
    {
        Directory.CreateDirectory(_root);
        _store = new PatternStore(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("../escaped.rle")]
    [InlineData("../../etc/passwd")]
    [InlineData("subdir/../../escaped.rle")]
    [InlineData("./../../escaped.rle")]
    [InlineData("a/b/c/../../../../escaped.rle")]
    public void Rejects_Traversal_Out_Of_The_Root(string hostile)
    {
        Assert.Throws<UnauthorizedAccessException>(() => _store.Resolve(hostile));
    }

    [Fact]
    public void Rejects_An_Absolute_Path()
    {
        string absolute = OperatingSystem.IsWindows() ? @"C:\Windows\System32\drivers\etc\hosts" : "/etc/passwd";

        Assert.Throws<UnauthorizedAccessException>(() => _store.Resolve(absolute));
    }

    [Fact]
    public void Rejects_A_Sibling_Directory_With_The_Root_As_A_Prefix()
    {
        // '/tmp/gol-store-abc-evil' starts with '/tmp/gol-store-abc' as a
        // string but is not inside it. A naive StartsWith check passes this.
        string sibling = Path.GetFileName(_root) + "-evil";

        Assert.Throws<UnauthorizedAccessException>(() => _store.Resolve(Path.Combine("..", sibling, "x.rle")));
    }

    [Theory]
    [InlineData("glider.rle")]
    [InlineData("subdir/glider.rle")]
    [InlineData("./glider.rle")]
    [InlineData("a/../glider.rle")]
    public void Accepts_Names_That_Stay_Inside_The_Root(string benign)
    {
        string resolved = _store.Resolve(benign);

        Assert.StartsWith(_root, resolved, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_Empty_And_Whitespace_Names()
    {
        Assert.Throws<ArgumentException>(() => _store.Resolve(""));
        Assert.Throws<ArgumentException>(() => _store.Resolve("   "));
    }

    [Fact]
    public void Load_Reports_A_Missing_File_As_Missing_Not_As_Forbidden()
    {
        // The two failures must stay distinguishable: one is the user's typo,
        // the other is an attack.
        Assert.Throws<FileNotFoundException>(() => _store.Load("nope.rle"));
    }

    [Fact]
    public void Saves_And_Reloads_Through_The_Store()
    {
        var universe = new Universe();
        universe.Reset([new Cell(ulong.MaxValue, ulong.MaxValue), new Cell(0, 0)], generation: 99);

        _store.Save(universe, "snapshots/run.rle", name: "Test");
        RlePattern reloaded = _store.Load("snapshots/run.rle");

        Assert.Equal(99UL, reloaded.Generation);
        Assert.Equal(2, reloaded.Population);
    }

    [Fact]
    public void Save_Creates_Intermediate_Directories_Inside_The_Root()
    {
        var universe = new Universe();
        universe.Reset([new Cell(1, 1)]);

        _store.Save(universe, "deep/nested/path/x.rle");

        Assert.True(File.Exists(Path.Combine(_root, "deep", "nested", "path", "x.rle")));
    }

    [Fact]
    public void Save_Cannot_Write_Outside_The_Root()
    {
        var universe = new Universe();
        universe.Reset([new Cell(1, 1)]);

        Assert.Throws<UnauthorizedAccessException>(() => _store.Save(universe, "../escaped.rle"));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_root)!, "escaped.rle")));
    }
}
