namespace GameOfLife.Core;

/// <summary>
/// A Life pattern in RLE form: cell coordinates relative to the pattern's own
/// top-left, plus the absolute universe position it was saved at.
/// </summary>
/// <remarks>
/// Local coordinates are kept separate from the absolute origin so a pattern
/// can be placed anywhere — loaded at its saved position to resume a run, or
/// dropped at the centre of the universe as a fresh seed — without reparsing.
/// </remarks>
public sealed record RlePattern
{
    /// <summary>The B3/S23 rule string this project simulates.</summary>
    public const string ConwayRule = "B3/S23";

    public required IReadOnlyList<(int X, int Y)> Coordinates { get; init; }

    /// <summary>Declared width from the RLE header.</summary>
    public required int Width { get; init; }

    /// <summary>Declared height from the RLE header.</summary>
    public required int Height { get; init; }

    /// <summary>Optional <c>#N</c> name.</summary>
    public string? Name { get; init; }

    /// <summary>Free-text <c>#C</c> comments, excluding our own tagged ones.</summary>
    public IReadOnlyList<string> Comments { get; init; } = [];

    /// <summary>
    /// Absolute universe position of the pattern's top-left, from
    /// <c>#C origin:</c>. Defaults to the origin when the file does not say.
    /// </summary>
    public Cell Origin { get; init; }

    /// <summary>Generation counter from <c>#C generation:</c>.</summary>
    public ulong Generation { get; init; }

    public int Population => Coordinates.Count;

    /// <summary>The pattern's cells at its recorded <see cref="Origin"/>.</summary>
    public IEnumerable<Cell> Cells => CellsAt(Origin);

    /// <summary>The pattern's cells placed at an arbitrary origin, wrapping.</summary>
    public IEnumerable<Cell> CellsAt(Cell origin) =>
        Coordinates.Select(c => origin.Offset(c.X, c.Y));

    /// <summary>
    /// Places the pattern so its centre sits at <paramref name="centre"/>.
    /// </summary>
    public IEnumerable<Cell> CellsCentredOn(Cell centre) =>
        CellsAt(centre.Offset(-(Width / 2), -(Height / 2)));
}
