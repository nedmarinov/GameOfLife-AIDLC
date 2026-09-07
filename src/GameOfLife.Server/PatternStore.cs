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
/// Containment is checked twice, because one check is not enough.
/// </para>
/// <para>
/// <b>Textually</b>, the candidate is combined with the root and normalised
/// with <see cref="Path.GetFullPath(string)"/>, which collapses <c>..</c> and
/// makes absolute paths obvious. That handles traversal and absolute paths
/// without a blocklist to keep current.
/// </para>
/// <para>
/// <b>Physically</b>, every component of the path is then resolved through any
/// symbolic links and the result is required to still sit under the resolved
/// root. This second check exists because <see cref="Path.GetFullPath(string)"/>
/// is <em>purely textual</em> and does not follow links: a symlink inside the
/// root pointing elsewhere produces a path that passes the first check while
/// reading or writing a file outside. An earlier version of this comment
/// claimed the first check rejected symlink escapes. It does not, and the
/// claim was wrong — see the audit log.
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

        if (!IsUnder(resolved, _root))
        {
            throw new UnauthorizedAccessException(
                $"'{requested}' resolves outside the pattern directory.");
        }

        // Follow symlinks and re-check. The target need not exist yet -- a save
        // names a file that is about to be created -- so this resolves the
        // deepest part of the path that does exist, which is where any link
        // would have to be.
        if (!IsUnder(ResolveThroughLinks(resolved), ResolveThroughLinks(_root)))
        {
            throw new UnauthorizedAccessException(
                $"'{requested}' resolves outside the pattern directory through a symbolic link.");
        }

        return resolved;
    }

    /// <summary>
    /// Is <paramref name="candidate"/> the root itself or something beneath it?
    /// </summary>
    /// <remarks>
    /// Compares against the root plus a separator, so a sibling whose name
    /// merely starts with the root's -- <c>/data-evil</c> against <c>/data</c>
    /// -- is not mistaken for a child.
    /// </remarks>
    private static bool IsUnder(string candidate, string root) =>
        candidate == root
        || candidate.StartsWith(root + Path.DirectorySeparatorChar, PathComparison);

    /// <summary>
    /// Follows symbolic links on every component of a path that exists.
    /// </summary>
    /// <remarks>
    /// .NET has no <c>realpath</c>. <see cref="FileSystemInfo.ResolveLinkTarget"/>
    /// resolves only the component it is given, so a link part-way along a path
    /// would be missed by resolving the final segment alone. This walks the
    /// path from the root outward and resolves each component in turn.
    /// Components that do not exist are appended unchanged, which is correct:
    /// a path that has not been created cannot be a link to anywhere.
    /// </remarks>
    private static string ResolveThroughLinks(string path, int depth = 0)
    {
        // A link's target may itself sit behind further links, so resolution
        // recurses. macOS makes this the normal case rather than an exotic one:
        // /var is a link to /private/var, so almost any temporary path involves
        // one. The depth cap stops a circular link from recursing forever.
        const int MaxDepth = 16;

        string current = Path.GetPathRoot(path) ?? string.Empty;
        if (current.Length == 0 || depth >= MaxDepth) return path;

        foreach (string segment in
                 path[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);

            FileSystemInfo info = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);

            if (!info.Exists || info.LinkTarget is null) continue;

            try
            {
                string? target = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName;
                if (target is null) continue;

                // Re-resolve the target: it is a fresh path that may cross
                // links of its own.
                current = ResolveThroughLinks(target, depth + 1);
            }
            catch (IOException)
            {
                // A broken or circular link resolves to nothing. Leaving the
                // path as-is is safe: the textual check already bounded it, and
                // opening it will fail on its own terms.
            }
        }

        return current;
    }

    /// <summary>
    /// How paths are compared for containment.
    /// </summary>
    /// <remarks>
    /// Windows and macOS default to case-insensitive filesystems, so an
    /// ordinal comparison there would reject legitimate paths that differ only
    /// in case. Linux is case-sensitive and must stay so, since two names
    /// differing in case are genuinely two files.
    /// </remarks>
    private static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

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
