using System.Runtime.InteropServices;
using System.Text;
using GameOfLife.Core;

namespace GameOfLife.Client.Console;

/// <summary>
/// Draws a viewport to the terminal using half-block glyphs.
/// </summary>
/// <remarks>
/// <para>
/// A 100x100 window will not fit in a terminal as one character per cell —
/// almost no terminal is 100 rows tall. The half-block glyph U+2580 (<c>▀</c>)
/// carries <b>two</b> cells: its foreground colour paints the upper half and
/// its background colour the lower half. Emitting only that one glyph, with the
/// two colours set per position, packs 100 cell-rows into 50 terminal rows and
/// keeps cells square-ish, since terminal cells are roughly twice as tall as
/// they are wide.
/// </para>
/// <para>
/// The whole frame is composed into a single string and written in one call
/// after homing the cursor. Writing per cell would emit 5,000 separate writes
/// and visibly tear. Colour escapes are emitted only when the colour actually
/// changes, which on a mostly-empty universe collapses a row to a handful of
/// sequences.
/// </para>
/// </remarks>
internal sealed class Screen
{
    // Half-block: foreground is the top half, background the bottom half.
    private const char HalfBlock = '▀';

    private const string EnterAlternateBuffer = "\e[?1049h";
    private const string LeaveAlternateBuffer = "\e[?1049l";
    private const string HideCursor = "\e[?25l";
    private const string ShowCursor = "\e[?25h";
    private const string Home = "\e[H";
    private const string Reset = "\e[0m";
    private const string ClearToEndOfLine = "\e[K";

    // 256-colour palette indices.
    private const int Dead = 234;      // near-black, so the grid reads as a surface
    private const int Alive = 231;     // white
    private const int CursorDead = 88; // dark red
    private const int CursorAlive = 208;
    private const int Dim = 244;

    private readonly StringBuilder _buffer = new(64 * 1024);

    private int _currentForeground = -1;
    private int _currentBackground = -1;

    public static void Enter()
    {
        // Half-blocks are non-ASCII, so Windows needs to be told before any
        // output happens.
        System.Console.OutputEncoding = Encoding.UTF8;

        EnableVirtualTerminalOnWindows();

        System.Console.Write(EnterAlternateBuffer + HideCursor);
    }

    /// <summary>
    /// Turns on ANSI escape handling on Windows.
    /// </summary>
    /// <remarks>
    /// macOS and Linux terminals interpret escape sequences unconditionally.
    /// Windows does not: .NET does not enable virtual terminal processing for
    /// you, and without it every sequence in this file is printed literally, so
    /// the screen fills with text like <c>[38;5;231m</c> instead of a grid.
    /// Windows Terminal happens to enable it already, but the classic console
    /// host does not, and "works on my machine" is exactly the failure this
    /// avoids. Failure is non-fatal: an older console simply gets no colour.
    /// </remarks>
    private static void EnableVirtualTerminalOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;

        const int StdOutputHandle = -11;
        const uint EnableVirtualTerminalProcessing = 0x0004;

        try
        {
            nint handle = GetStdHandle(StdOutputHandle);

            if (handle != 0 && handle != -1 && GetConsoleMode(handle, out uint mode))
                SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing);
        }
        catch (DllNotFoundException)
        {
            // Not a real Windows console. Nothing to enable.
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    // DllImport rather than LibraryImport: the source-generated form requires
    // AllowUnsafeBlocks across the whole assembly, which three interop
    // declarations do not justify.
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleMode(nint hConsoleHandle, uint dwMode);

    public static void Leave() => System.Console.Write(Reset + ShowCursor + LeaveAlternateBuffer);

    /// <summary>Renders one frame plus the status and help lines.</summary>
    public void Draw(
        in Viewport viewport,
        ReadOnlySpan<byte> bitmap,
        Cell cursor,
        string status,
        string help,
        string? prompt)
    {
        System.Console.Write(Compose(viewport, bitmap, cursor, status, help, prompt));
    }

    /// <summary>
    /// Builds the frame as a single string.
    /// </summary>
    /// <remarks>
    /// Separated from <see cref="Draw"/> so the half-block packing can be
    /// asserted without a terminal. Writing per cell would emit 5,000 separate
    /// writes and visibly tear, so composition happens here and output is one
    /// call.
    /// </remarks>
    public string Compose(
        in Viewport viewport,
        ReadOnlySpan<byte> bitmap,
        Cell cursor,
        string status,
        string help,
        string? prompt)
    {
        _buffer.Clear();
        _buffer.Append(Home);

        _currentForeground = -1;
        _currentBackground = -1;

        viewport.TryLocate(cursor, out int cursorX, out int cursorY);
        bool cursorVisible = viewport.TryLocate(cursor, out _, out _);

        for (int row = 0; row < viewport.Height; row += 2)
        {
            for (int column = 0; column < viewport.Width; column++)
            {
                bool top = IsAlive(viewport, bitmap, column, row);
                bool bottom = row + 1 < viewport.Height && IsAlive(viewport, bitmap, column, row + 1);

                bool cursorOnTop = cursorVisible && cursorX == column && cursorY == row;
                bool cursorOnBottom = cursorVisible && cursorX == column && cursorY == row + 1;

                AppendCell(
                    Colour(top, cursorOnTop),
                    Colour(bottom, cursorOnBottom));
            }

            _buffer.Append(Reset).Append(ClearToEndOfLine).Append('\n');
            _currentForeground = -1;
            _currentBackground = -1;
        }

        _buffer.Append(Reset).Append(status).Append(ClearToEndOfLine).Append('\n');
        _buffer.Append("\e[38;5;").Append(Dim).Append('m')
               .Append(prompt ?? help).Append(Reset).Append(ClearToEndOfLine);

        return _buffer.ToString();
    }

    private static int Colour(bool alive, bool underCursor) => (alive, underCursor) switch
    {
        (true, true) => CursorAlive,
        (false, true) => CursorDead,
        (true, false) => Alive,
        (false, false) => Dead,
    };

    private static bool IsAlive(in Viewport viewport, ReadOnlySpan<byte> bitmap, int x, int y)
    {
        int index = viewport.BitIndex(x, y);
        return index < viewport.CellCount && Viewport.IsSet(bitmap, index);
    }

    /// <summary>Appends one glyph, emitting colour escapes only when they change.</summary>
    private void AppendCell(int foreground, int background)
    {
        if (foreground != _currentForeground)
        {
            _buffer.Append("\e[38;5;").Append(foreground).Append('m');
            _currentForeground = foreground;
        }

        if (background != _currentBackground)
        {
            _buffer.Append("\e[48;5;").Append(background).Append('m');
            _currentBackground = background;
        }

        _buffer.Append(HalfBlock);
    }

    /// <summary>Warns once if the terminal is too small to show the whole window.</summary>
    public static string? SizeWarning(in Viewport viewport)
    {
        try
        {
            int neededRows = (viewport.Height / 2) + 3;

            if (System.Console.WindowWidth < viewport.Width || System.Console.WindowHeight < neededRows)
            {
                return $"Terminal is {System.Console.WindowWidth}x{System.Console.WindowHeight}; " +
                       $"{viewport.Width}x{neededRows} is needed to see the whole window.";
            }
        }
        catch (IOException)
        {
            // No console attached (redirected output). Not worth failing over.
        }

        return null;
    }
}
