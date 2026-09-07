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

    /// <summary>
    /// A symlink inside the root pointing outside it must be refused.
    /// </summary>
    /// <remarks>
    /// <see cref="Path.GetFullPath(string)"/> is purely textual and does not
    /// follow links, so a path through a symlink passes a naive containment
    /// check while touching a file outside the root. This is the regression
    /// test for that: an earlier version of the store claimed to reject symlink
    /// escapes and did not.
    /// </remarks>
    [Fact]
    public void Rejects_A_Symlink_That_Points_Outside_The_Root()
    {
        string outside = Path.Combine(Path.GetTempPath(), $"gol-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.rle"), "x = 1, y = 1, rule = B3/S23\no!");

        try
        {
            Directory.CreateSymbolicLink(Path.Combine(_root, "escape"), outside);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            // Unprivileged Windows cannot create links. Nothing to assert here.
            Directory.Delete(outside, recursive: true);
            return;
        }

        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => _store.Resolve("escape/secret.rle"));
            Assert.Throws<UnauthorizedAccessException>(() => _store.Load("escape/secret.rle"));

            // The write side is the damaging half.
            var universe = new Universe();
            universe.Reset([new Cell(1, 1)]);
            Assert.Throws<UnauthorizedAccessException>(() => _store.Save(universe, "escape/planted.rle"));
            Assert.False(File.Exists(Path.Combine(outside, "planted.rle")));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void A_Symlink_Staying_Inside_The_Root_Is_Still_Allowed()
    {
        // Containment is the rule, not a ban on links.
        string inner = Path.Combine(_root, "real");
        Directory.CreateDirectory(inner);
        File.WriteAllText(Path.Combine(inner, "p.rle"), "x = 1, y = 1, rule = B3/S23\no!");

        try
        {
            Directory.CreateSymbolicLink(Path.Combine(_root, "alias"), inner);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        Assert.Equal(1, _store.Load("alias/p.rle").Population);
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
