using GameOfLife.Core;

namespace GameOfLife.Server;

/// <summary>
/// Loads and saves patterns, confined to a directory the operator chose.
/// </summary>
/// <remarks>
/// <para>
/// The file name in a load or save request arrives from a network peer, so it
/// is untrusted input. Passing it to <see cref="File.ReadAllText(string)"/>
/// unchecked would let any connected client read <c>../../../etc/passwd</c> or
/// overwrite an arbitrary file — a path traversal, and the more damaging half
/// is the write.
/// </para>
/// <para>
/// Containment is by resolved path rather than by inspecting the string for
/// <c>..</c>: the candidate is combined with the root, fully resolved, and then
/// required to still sit beneath the root. That rejects traversal, absolute
/// paths, and symlink escapes together, without a blocklist to keep current.
/// </para>
/// </remarks>
public sealed class PatternStore
{
    private readonly string _root;

    public PatternStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }

    public string Root => _root;

    /// <summary>Resolves a client-supplied name to a path inside the root.</summary>
    /// <exception cref="UnauthorizedAccessException">The name escapes the root.</exception>
    public string Resolve(string requested)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requested);

        string resolved = Path.GetFullPath(Path.Combine(_root, requested));

        // Compare against the root plus a separator, so '/data-evil' cannot
        // pass as a child of '/data'.
        string boundary = _root + Path.DirectorySeparatorChar;

        if (!resolved.StartsWith(boundary, StringComparison.Ordinal) && resolved != _root)
        {
            throw new UnauthorizedAccessException(
                $"'{requested}' resolves outside the pattern directory.");
        }

        return resolved;
    }

    public RlePattern Load(string requested)
    {
        string path = Resolve(requested);

        if (!File.Exists(path))
            throw new FileNotFoundException($"No such pattern: '{requested}'.", path);

        return Rle.ParseFile(path);
    }

    /// <summary>
    /// Lists the RLE files under the root, newest-looking name first.
    /// </summary>
    /// <remarks>
    /// A file that fails to parse is listed with its error rather than omitted.
    /// Silently hiding it would leave a user staring at a directory they know
    /// has a file in it and a client that claims otherwise.
    /// </remarks>
    public IReadOnlyList<(string File, RlePattern? Pattern, string? Error)> List()
    {
        if (!Directory.Exists(_root)) return [];

        var entries = new List<(string, RlePattern?, string?)>();

        foreach (string path in Directory.EnumerateFiles(_root, "*.rle", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            string relative = Path.GetRelativePath(_root, path).Replace(Path.DirectorySeparatorChar, '/');

            try
            {
                entries.Add((relative, Rle.ParseFile(path), null));
            }
            catch (Exception error) when (error is FormatException or NotSupportedException or IOException)
            {
                entries.Add((relative, null, error.Message));
            }
        }

        return entries;
    }

    public void Save(Universe universe, string requested, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(universe);

        string path = Resolve(requested);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Rle.WriteFile(universe, path, name);
    }
}
