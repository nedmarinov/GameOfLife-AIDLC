using System.Runtime.InteropServices;

namespace GameOfLife.Core;

/// <summary>
/// A 2^64 x 2^64 toroidal Game of Life universe, stored as the set of live
/// cells.
/// </summary>
/// <remarks>
/// <para>
/// There is no grid and no dimension constant. Memory and per-generation cost
/// are O(population), independent of universe size — which is what makes a
/// 2^64 x 2^64 universe possible at all. A dense representation would need
/// 2^128 cells: not a slow implementation of this, an impossible one.
/// </para>
/// <para>
/// <b>This type is deliberately not thread-safe.</b> Exactly one thread — the
/// simulation loop — ever touches it, and all mutations reach that thread
/// through a command channel. Mutual exclusion is therefore structural rather
/// than enforced: there is no lock to forget to take. See ADR 0003.
/// </para>
/// </remarks>
public sealed class Universe
{
    private HashSet<Cell> _live;
    private HashSet<Cell> _next;

    /// <summary>
    /// Neighbour tallies for one generation. Held as a field and cleared rather
    /// than reallocated, so a steady population produces no steady-state
    /// allocation.
    /// </summary>
    private readonly Dictionary<Cell, int> _neighbourCounts;

    public Universe(int capacityHint = 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacityHint);

        _live = new HashSet<Cell>(capacityHint);
        _next = new HashSet<Cell>(capacityHint);
        _neighbourCounts = new Dictionary<Cell, int>(capacityHint * Cell.NeighbourCount);
    }

    /// <summary>Generations elapsed since the universe was last reset.</summary>
    public ulong Generation { get; private set; }

    /// <summary>Number of live cells.</summary>
    public int Population => _live.Count;

    /// <summary>The live cells. Enumeration order is unspecified.</summary>
    public IReadOnlyCollection<Cell> LiveCells => _live;

    public bool IsAlive(in Cell cell) => _live.Contains(cell);

    /// <summary>Sets a cell's state. Returns true if the state changed.</summary>
    public bool SetAlive(in Cell cell, bool alive) =>
        alive ? _live.Add(cell) : _live.Remove(cell);

    /// <summary>Flips a cell. Returns the resulting state.</summary>
    public bool Toggle(in Cell cell)
    {
        if (_live.Remove(cell)) return false;
        _live.Add(cell);
        return true;
    }

    /// <summary>Empties the universe and resets the generation counter.</summary>
    public void Clear()
    {
        _live.Clear();
        Generation = 0;
    }

    /// <summary>Replaces the entire contents of the universe.</summary>
    public void Reset(IEnumerable<Cell> cells, ulong generation = 0)
    {
        ArgumentNullException.ThrowIfNull(cells);

        _live.Clear();
        foreach (Cell cell in cells) _live.Add(cell);
        Generation = generation;
    }

    /// <summary>
    /// Advances one generation under Conway's B3/S23 rules.
    /// </summary>
    /// <remarks>
    /// Only cells adjacent to something alive can change state, so tallying
    /// neighbours around live cells visits every cell that matters and nothing
    /// else. The empty remainder of the universe is never touched, which is why
    /// this is O(population x 8) rather than O(universe).
    /// </remarks>
    public void Step()
    {
        _neighbourCounts.Clear();

        // One stack buffer for the whole generation, not one per cell.
        Span<Cell> neighbours = stackalloc Cell[Cell.NeighbourCount];

        foreach (Cell cell in _live)
        {
            cell.WriteNeighbours(neighbours);

            foreach (Cell neighbour in neighbours)
            {
                // Single hash lookup per increment instead of TryGetValue + set.
                ref int tally = ref CollectionsMarshal.GetValueRefOrAddDefault(
                    _neighbourCounts, neighbour, out _);
                tally++;
            }
        }

        _next.Clear();

        foreach ((Cell cell, int count) in _neighbourCounts)
        {
            // B3: a dead cell with exactly 3 neighbours is born.
            // S23: a live cell with 2 or 3 neighbours survives.
            // A live cell with no live neighbours never becomes a key here, so
            // it is absent from _next and dies of underpopulation — correct.
            if (count == 3 || (count == 2 && _live.Contains(cell)))
                _next.Add(cell);
        }

        (_live, _next) = (_next, _live);
        Generation++;
    }

    /// <summary>
    /// Renders the live cells visible in <paramref name="viewport"/> into a
    /// one-bit-per-cell bitmap, row-major from the window's top-left.
    /// </summary>
    /// <remarks>
    /// Fixed size regardless of population: a 100x100 window is always 1250
    /// bytes. Cost is O(population) because the live set is scanned and tested
    /// for visibility. For the mostly-empty universe in the brief that is the
    /// cheaper direction; a universe dense enough to invert that tradeoff would
    /// want a spatial index, which ADR 0001 records as deferred.
    /// </remarks>
    public void RenderTo(in Viewport viewport, Span<byte> destination)
    {
        int byteCount = viewport.BitmapByteCount;

        if (destination.Length < byteCount)
            throw new ArgumentException(
                $"Destination must hold at least {byteCount} bytes.", nameof(destination));

        destination[..byteCount].Clear();

        foreach (Cell cell in _live)
        {
            if (!viewport.TryLocate(cell, out int localX, out int localY)) continue;

            int bit = viewport.BitIndex(localX, localY);
            destination[bit >> 3] |= (byte)(1 << (bit & 7));
        }
    }
}
