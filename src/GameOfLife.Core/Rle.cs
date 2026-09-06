using System.Globalization;
using System.Text;

namespace GameOfLife.Core;

/// <summary>
/// Reader and writer for the standard Life RLE format.
/// </summary>
/// <remarks>
/// <para>
/// RLE was chosen over a bespoke format so the shipped example file can be
/// opened and checked in an independent implementation such as Golly, rather
/// than only against our own reader. Two pieces of state the format has no
/// place for — the absolute <see cref="ulong"/> origin and the generation
/// counter — ride in tagged <c>#C</c> comments, which are free text in every
/// implementation and therefore cannot collide with a defined tag. See ADR
/// 0004.
/// </para>
/// </remarks>
public static class Rle
{
    private const string ExtendedPrefix = "#CXRLE";
    private const int MaxOutputLineLength = 70;

    // ---------------------------------------------------------------- read

    public static RlePattern Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string? name = null;
        List<string> comments = [];
        Cell origin = default;
        ulong generation = 0;
        int width = 0, height = 0;
        bool headerSeen = false;
        StringBuilder body = new();

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim('\r', ' ', '\t');
            if (line.Length == 0) continue;

            if (line[0] == '#')
            {
                ReadCommentLine(line, ref name, comments, ref origin, ref generation);
                continue;
            }

            if (!headerSeen && line.StartsWith('x'))
            {
                (width, height) = ReadHeaderLine(line);
                headerSeen = true;
                continue;
            }

            body.Append(line);
            if (line.Contains('!')) break;
        }

        if (!headerSeen)
            throw new FormatException("RLE is missing its 'x = ..., y = ...' header line.");

        List<(int X, int Y)> coordinates = ReadBody(body.ToString(), width, height);

        return new RlePattern
        {
            Coordinates = coordinates,
            Width = width,
            Height = height,
            Name = name,
            Comments = comments,
            Origin = origin,
            Generation = generation,
        };
    }

    public static RlePattern ParseFile(string path) => Parse(File.ReadAllText(path));

    private static void ReadCommentLine(
        string line, ref string? name, List<string> comments, ref Cell origin, ref ulong generation)
    {
        if (line.Length < 2) return;

        // Checked against the whole line: '#CXRLE' shares its first two
        // characters with an ordinary '#C' comment, so it has to be recognised
        // before the tag is consumed.
        if (line.StartsWith(ExtendedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            ReadExtendedLine(line[ExtendedPrefix.Length..], ref origin, ref generation);
            return;
        }

        char tag = line[1];
        string value = line.Length > 2 ? line[2..].Trim() : string.Empty;

        if (tag == 'N')
        {
            name = value;
            return;
        }

        if (tag is not ('C' or 'c'))
        {
            // #O (author), #P / #R (legacy 32-bit position), #r (rules) and any
            // unknown tag are preserved verbatim, never reinterpreted. The RLE
            // convention is that a reader ignores tags it does not know.
            comments.Add(line[1..].Trim());
            return;
        }

        comments.Add(value);
    }

    /// <summary>
    /// Reads a <c>#CXRLE</c> line: space-separated <c>Keyword=Value</c> pairs,
    /// of which <c>Pos</c> and <c>Gen</c> are defined.
    /// </summary>
    private static void ReadExtendedLine(string value, ref Cell origin, ref ulong generation)
    {
        foreach (string pair in value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=');
            if (equals < 0) continue;

            string keyword = pair[..equals].Trim();
            string operand = pair[(equals + 1)..].Trim();

            if (keyword.Equals("Pos", StringComparison.OrdinalIgnoreCase))
                origin = ParsePosition(operand);
            else if (keyword.Equals("Gen", StringComparison.OrdinalIgnoreCase))
                generation = ParseGeneration(operand);

            // Unknown keywords are ignored, as the format requires.
        }
    }

    /// <summary>
    /// Parses a signed <c>Pos</c> pair into unsigned universe coordinates.
    /// </summary>
    /// <remarks>
    /// Extended RLE writes signed coordinates; this universe uses
    /// <see cref="ulong"/>. On a torus of exactly 2^64 cells per axis both are
    /// residues modulo 2^64, so the signed and unsigned readings of the same 64
    /// bits name the same cell. The conversion is therefore a bit
    /// reinterpretation that loses nothing in either direction.
    /// </remarks>
    private static Cell ParsePosition(string value)
    {
        string[] parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
            throw new FormatException($"Malformed CXRLE Pos: '{value}'. Expected 'Pos=x,y'.");

        return new Cell(ToCoordinate(parts[0], value), ToCoordinate(parts[1], value));
    }

    private static ulong ToCoordinate(string token, string context)
    {
        if (long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long signed))
            return unchecked((ulong)signed);

        // Also accept the full unsigned range, which a writer targeting a 2^64
        // universe may legitimately emit.
        if (ulong.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out ulong unsigned))
            return unsigned;

        throw new FormatException($"Malformed CXRLE Pos component '{token}' in '{context}'.");
    }

    private static ulong ParseGeneration(string value)
    {
        if (ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong generation))
            return generation;

        if (long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long signed))
            return unchecked((ulong)signed);

        throw new FormatException($"Malformed CXRLE Gen: '{value}'.");
    }

    private static (int Width, int Height) ReadHeaderLine(string line)
    {
        int width = 0, height = 0;

        foreach (string field in line.Split(',', StringSplitOptions.TrimEntries))
        {
            int equals = field.IndexOf('=');
            if (equals < 0) continue;

            string key = field[..equals].Trim();
            string value = field[(equals + 1)..].Trim();

            switch (key)
            {
                case "x":
                    width = ParseDimension(value, "x");
                    break;
                case "y":
                    height = ParseDimension(value, "y");
                    break;
                case "rule":
                    RequireConwayRule(value);
                    break;
            }
        }

        return (width, height);
    }

    private static int ParseDimension(string value, string key) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : throw new FormatException($"Malformed '{key}' in RLE header: '{value}'.");

    /// <summary>
    /// Rejects any rule this engine does not implement.
    /// </summary>
    /// <remarks>
    /// Loading a HighLife (B36/S23) file and quietly simulating it as Conway
    /// would produce confidently wrong output with no error anywhere, so an
    /// unsupported rule is a hard failure rather than a warning.
    /// </remarks>
    private static void RequireConwayRule(string rule)
    {
        string normalised = rule.Replace(" ", string.Empty).ToUpperInvariant();

        // "B3/S23" (modern) and "23/3" (survival/birth, older files).
        if (normalised is "B3/S23" or "23/3") return;

        throw new NotSupportedException(
            $"Rule '{rule}' is not supported. This engine implements Conway's B3/S23 only.");
    }

    private static List<(int X, int Y)> ReadBody(string body, int width, int height)
    {
        List<(int X, int Y)> coordinates = [];
        int x = 0, y = 0, runLength = 0;

        foreach (char c in body)
        {
            if (char.IsAsciiDigit(c))
            {
                runLength = (runLength * 10) + (c - '0');
                continue;
            }

            int count = runLength == 0 ? 1 : runLength;

            switch (c)
            {
                case 'b' or 'B' or '.':
                    x += count;
                    break;

                case 'o' or 'O':
                    for (int i = 0; i < count; i++) coordinates.Add((x + i, y));
                    x += count;
                    break;

                case '$':
                    y += count;
                    x = 0;
                    break;

                case '!':
                    return coordinates;

                default:
                    if (!char.IsWhiteSpace(c))
                        throw new FormatException($"Unexpected character '{c}' in RLE body.");
                    continue;
            }

            runLength = 0;
        }

        return coordinates;
    }

    // --------------------------------------------------------------- write

    /// <summary>Serialises a universe's live cells to RLE.</summary>
    public static string Format(Universe universe, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(universe);

        return Format(universe.LiveCells, universe.Generation, name);
    }

    public static string Format(IEnumerable<Cell> cells, ulong generation = 0, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(cells);

        Cell[] live = [.. cells];
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(name)) builder.Append("#N ").Append(name).Append('\n');

        if (live.Length == 0)
        {
            AppendExtendedLine(builder, 0, 0, generation);
            builder.Append("x = 0, y = 0, rule = ").Append(RlePattern.ConwayRule).Append("\n!\n");
            return builder.ToString();
        }

        (ulong originX, int width) = BoundingSpan(live.Select(c => c.X));
        (ulong originY, int height) = BoundingSpan(live.Select(c => c.Y));

        AppendExtendedLine(builder, originX, originY, generation);

        builder.Append("x = ").Append(width)
               .Append(", y = ").Append(height)
               .Append(", rule = ").Append(RlePattern.ConwayRule).Append('\n');

        AppendBody(builder, live, originX, originY, width, height);
        return builder.ToString();
    }

    public static void WriteFile(Universe universe, string path, string? name = null) =>
        File.WriteAllText(path, Format(universe, name));

    /// <summary>
    /// Writes the Extended RLE metadata line.
    /// </summary>
    /// <remarks>
    /// Coordinates are emitted as their signed reinterpretation. On a 2^64
    /// torus that names the same cell (see <see cref="ParsePosition"/>), and it
    /// keeps values inside the range other tools expect instead of overflowing
    /// it: an origin two cells before the wrap point is written as -2 rather
    /// than 18446744073709551614.
    /// </remarks>
    private static void AppendExtendedLine(
        StringBuilder builder, ulong originX, ulong originY, ulong generation)
    {
        builder.Append("#CXRLE Pos=")
               .Append(unchecked((long)originX).ToString(CultureInfo.InvariantCulture))
               .Append(',')
               .Append(unchecked((long)originY).ToString(CultureInfo.InvariantCulture));

        // Golly writes Gen only when non-zero; matching that keeps our output
        // byte-identical to what it would produce.
        if (generation != 0)
            builder.Append(" Gen=").Append(generation.ToString(CultureInfo.InvariantCulture));

        builder.Append('\n');
    }

    /// <summary>
    /// Finds the occupied span on one axis of the toroidal universe.
    /// </summary>
    /// <remarks>
    /// Naive min/max is wrong here: a pattern straddling the 2^64 seam has
    /// coordinates near both zero and <see cref="ulong.MaxValue"/>, which would
    /// yield a bounding box nearly the width of the universe. Instead, locate
    /// the largest empty run and take its complement — the occupied span begins
    /// immediately after the largest gap. This is correct whether or not the
    /// pattern crosses the seam, so no special case is needed for either.
    /// </remarks>
    private static (ulong Origin, int Size) BoundingSpan(IEnumerable<ulong> coordinates)
    {
        ulong[] sorted = [.. coordinates.Distinct().Order()];

        if (sorted.Length == 1) return (sorted[0], 1);

        ulong largestGap = 0;
        ulong spanStart = sorted[0];

        for (int i = 0; i < sorted.Length; i++)
        {
            ulong current = sorted[i];
            ulong next = sorted[(i + 1) % sorted.Length];

            // Wraps for the final pair, which is exactly the gap that closes
            // the ring.
            ulong gap = unchecked(next - current);

            if (gap > largestGap)
            {
                largestGap = gap;
                spanStart = next;
            }
        }

        // 2^64 - largestGap + 1, computed without needing 2^64 to be
        // representable.
        ulong size = unchecked(0UL - largestGap) + 1;

        if (size > int.MaxValue)
        {
            throw new NotSupportedException(
                $"Pattern spans {size} cells on one axis, which exceeds the RLE header's " +
                "integer range. Save a smaller region.");
        }

        return (spanStart, (int)size);
    }

    private static void AppendBody(
        StringBuilder builder, Cell[] live, ulong originX, ulong originY, int width, int height)
    {
        // Bucket by row so the writer emits rows in order without sorting the
        // whole set, and skips empty rows with a run-length '$'.
        var rows = new Dictionary<int, List<int>>();

        foreach (Cell cell in live)
        {
            int localY = (int)unchecked(cell.Y - originY);
            int localX = (int)unchecked(cell.X - originX);

            if (!rows.TryGetValue(localY, out List<int>? row))
                rows[localY] = row = [];

            row.Add(localX);
        }

        var line = new StringBuilder();
        int pendingBlankRows = 0;

        for (int y = 0; y < height; y++)
        {
            if (!rows.TryGetValue(y, out List<int>? row))
            {
                pendingBlankRows++;
                continue;
            }

            if (y > 0) AppendToken(builder, line, pendingBlankRows + 1, '$');
            pendingBlankRows = 0;

            row.Sort();
            int cursor = 0;

            for (int i = 0; i < row.Count;)
            {
                int start = row[i];
                int run = 1;
                while (i + run < row.Count && row[i + run] == start + run) run++;

                if (start > cursor) AppendToken(builder, line, start - cursor, 'b');
                AppendToken(builder, line, run, 'o');

                cursor = start + run;
                i += run;
            }
        }

        line.Append('!');
        builder.Append(line).Append('\n');
    }

    /// <summary>Appends one RLE token, wrapping at the conventional 70 columns.</summary>
    private static void AppendToken(StringBuilder output, StringBuilder line, int count, char tag)
    {
        string token = count == 1 ? tag.ToString() : $"{count}{tag}";

        if (line.Length + token.Length > MaxOutputLineLength)
        {
            output.Append(line).Append('\n');
            line.Clear();
        }

        line.Append(token);
    }
}
