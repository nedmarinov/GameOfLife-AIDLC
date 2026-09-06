using GameOfLife.Client.Console;
using GameOfLife.Core;

namespace GameOfLife.Client.Console.Tests;

/// <summary>
/// The half-block packing, asserted without a terminal.
/// </summary>
/// <remarks>
/// Packing two cell rows into one glyph is easy to get subtly wrong — an
/// off-by-one in the row pairing produces output that still looks like Life and
/// is wrong by one row everywhere. These tests read the composed frame back.
/// </remarks>
public class ScreenTests
{
    private const int Size = 100;
    private const char HalfBlock = '▀';

    private const int Dead = 234;
    private const int Alive = 231;
    private const int CursorDead = 88;
    private const int CursorAlive = 208;

    private static (Viewport Viewport, byte[] Bitmap) Universe(params Cell[] live)
    {
        var viewport = new Viewport(1UL << 63, 1UL << 63, Size, Size);
        var universe = new Universe();
        universe.Reset(live);

        byte[] bitmap = new byte[viewport.BitmapByteCount];
        universe.RenderTo(viewport, bitmap);

        return (viewport, bitmap);
    }

    private static string Compose(Viewport viewport, byte[] bitmap, Cell cursor) =>
        new Screen().Compose(viewport, bitmap, cursor, "status", "help", prompt: null);

    /// <summary>Only the grid rows, without the status and help lines.</summary>
    private static string[] GridLines(string composed) =>
        composed.Split('\n')[..(Size / 2)];

    [Fact]
    public void A_Hundred_Cell_Rows_Become_Fifty_Terminal_Rows()
    {
        (Viewport viewport, byte[] bitmap) = Universe();

        string[] lines = GridLines(Compose(viewport, bitmap, viewport.CellAt(0, 0)));

        Assert.Equal(50, lines.Length);
    }

    [Fact]
    public void Every_Glyph_Is_The_Half_Block()
    {
        (Viewport viewport, byte[] bitmap) = Universe(
            new Cell((1UL << 63) + 5, (1UL << 63) + 5));

        string composed = Compose(viewport, bitmap, viewport.CellAt(0, 0));

        // One glyph per column per rendered row, and no other printable
        // characters inside the grid region.
        Assert.Equal(Size * (Size / 2), composed[..composed.IndexOf("status", StringComparison.Ordinal)]
            .Count(c => c == HalfBlock));
    }

    [Fact]
    public void An_Empty_Universe_Paints_Only_Dead_Colours()
    {
        (Viewport viewport, byte[] bitmap) = Universe();

        // Cursor parked outside the window so it cannot tint anything.
        string composed = Compose(viewport, bitmap, new Cell(0, 0));
        string grid = string.Join('\n', GridLines(composed));

        Assert.Contains($"\e[38;5;{Dead}m", grid);
        Assert.DoesNotContain($"\e[38;5;{Alive}m", grid);
        Assert.DoesNotContain($"\e[48;5;{Alive}m", grid);
    }

    [Fact]
    public void A_Live_Cell_On_An_Even_Row_Colours_The_Foreground()
    {
        // Foreground paints the upper half of the glyph, so an even cell row
        // must appear as a foreground change and never a background one.
        (Viewport viewport, byte[] bitmap) = Universe(viewport0Cell(0, 4));

        string grid = string.Join('\n', GridLines(Compose(viewport, bitmap, new Cell(0, 0))));

        Assert.Contains($"\e[38;5;{Alive}m", grid);
        Assert.DoesNotContain($"\e[48;5;{Alive}m", grid);

        static Cell viewport0Cell(int x, int y) => new Viewport(1UL << 63, 1UL << 63, Size, Size).CellAt(x, y);
    }

    [Fact]
    public void A_Live_Cell_On_An_Odd_Row_Colours_The_Background()
    {
        var viewport = new Viewport(1UL << 63, 1UL << 63, Size, Size);
        (_, byte[] bitmap) = Universe(viewport.CellAt(0, 5));

        string grid = string.Join('\n', GridLines(Compose(viewport, bitmap, new Cell(0, 0))));

        Assert.Contains($"\e[48;5;{Alive}m", grid);
        Assert.DoesNotContain($"\e[38;5;{Alive}m", grid);
    }

    [Fact]
    public void The_Cursor_Is_Visible_Over_An_Empty_Cell()
    {
        var viewport = new Viewport(1UL << 63, 1UL << 63, Size, Size);
        (_, byte[] bitmap) = Universe();

        string grid = string.Join('\n', GridLines(Compose(viewport, bitmap, viewport.CellAt(10, 10))));

        Assert.Contains($"\e[38;5;{CursorDead}m", grid);
    }

    [Fact]
    public void The_Cursor_Is_Distinguishable_Over_A_Live_Cell()
    {
        var viewport = new Viewport(1UL << 63, 1UL << 63, Size, Size);
        Cell target = viewport.CellAt(10, 10);
        (_, byte[] bitmap) = Universe(target);

        string grid = string.Join('\n', GridLines(Compose(viewport, bitmap, target)));

        Assert.Contains($"\e[38;5;{CursorAlive}m", grid);
    }

    [Fact]
    public void A_Cursor_Outside_The_Window_Tints_Nothing()
    {
        var viewport = new Viewport(1UL << 63, 1UL << 63, Size, Size);
        (_, byte[] bitmap) = Universe();

        string grid = string.Join('\n', GridLines(Compose(viewport, bitmap, new Cell(0, 0))));

        Assert.DoesNotContain($"\e[38;5;{CursorDead}m", grid);
        Assert.DoesNotContain($"\e[38;5;{CursorAlive}m", grid);
    }

    [Fact]
    public void Renders_A_Window_Straddling_The_Seam()
    {
        var viewport = new Viewport(ulong.MaxValue - 9, ulong.MaxValue - 9, Size, Size);
        var universe = new Universe();

        // Two cells either side of the wrap: local (9,9), an odd row and so a
        // background half, and local (10,10), an even row and a foreground one.
        universe.Reset([new Cell(ulong.MaxValue, ulong.MaxValue), new Cell(0, 0)]);

        byte[] bitmap = new byte[viewport.BitmapByteCount];
        universe.RenderTo(viewport, bitmap);

        // The cursor must sit outside this window, which rules out (0,0) --
        // a seam-straddling viewport contains it, at local (10,10).
        string[] lines = GridLines(new Screen().Compose(
            viewport, bitmap, new Cell(1_000, 1_000), "status", "help", null));

        string grid = string.Join('\n', lines);

        Assert.Equal(50, lines.Length);
        Assert.Contains($"\e[48;5;{Alive}m", grid);
        Assert.Contains($"\e[38;5;{Alive}m", grid);
    }

    [Fact]
    public void Status_And_Help_Follow_The_Grid()
    {
        (Viewport viewport, byte[] bitmap) = Universe();

        string composed = new Screen().Compose(
            viewport, bitmap, viewport.CellAt(0, 0), "STATUSLINE", "HELPLINE", null);

        Assert.Contains("STATUSLINE", composed);
        Assert.Contains("HELPLINE", composed);
        Assert.True(composed.IndexOf("STATUSLINE", StringComparison.Ordinal)
                  < composed.IndexOf("HELPLINE", StringComparison.Ordinal));
    }

    [Fact]
    public void A_Prompt_Replaces_The_Help_Line()
    {
        (Viewport viewport, byte[] bitmap) = Universe();

        string composed = new Screen().Compose(
            viewport, bitmap, viewport.CellAt(0, 0), "status", "HELPLINE", "load: glider.rle");

        Assert.Contains("load: glider.rle", composed);
        Assert.DoesNotContain("HELPLINE", composed);
    }

    [Fact]
    public void Colour_Escapes_Are_Emitted_Only_On_Change()
    {
        // A mostly-empty universe should collapse to a handful of escapes per
        // row, not two per cell. Guards the run-length behaviour that keeps the
        // frame small enough to write in one call.
        (Viewport viewport, byte[] bitmap) = Universe();

        string composed = Compose(viewport, bitmap, new Cell(0, 0));
        int escapes = composed.Split("\e[38;5;").Length - 1;

        Assert.True(escapes < Size, $"Expected far fewer than {Size} foreground escapes, got {escapes}.");
    }
}
