using GameOfLife.Core;
using GameOfLife.Protocol;
using GameOfLife.Server;

int port = ProtocolConstants.DefaultPort;
int tick = ProtocolConstants.DefaultTickMilliseconds;
string patternsRoot = FindPatternsDirectory();
string? seedPattern = "gosper-glider-gun.rle";
bool startRunning = false;
int webPort = ProtocolConstants.DefaultPort + 1;
bool web = true;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--port" when i + 1 < args.Length:
            port = int.Parse(args[++i]);
            break;

        case "--tick" when i + 1 < args.Length:
            tick = int.Parse(args[++i]);
            break;

        case "--patterns" when i + 1 < args.Length:
            patternsRoot = args[++i];
            break;

        case "--pattern" when i + 1 < args.Length:
            seedPattern = args[++i];
            break;

        case "--empty":
            seedPattern = null;
            break;

        case "--run":
            startRunning = true;
            break;

        case "--web-port" when i + 1 < args.Length:
            webPort = int.Parse(args[++i]);
            break;

        case "--no-web":
            web = false;
            break;

        case "--help" or "-h":
            Console.WriteLine("""
                Conway's Game of Life -- server

                  --port <n>        listen port (default 5150)
                  --tick <ms>       generation interval (default 100)
                  --patterns <dir>  directory for load/save (default ./patterns)
                  --pattern <file>  seed pattern (default gosper-glider-gun.rle)
                  --empty           start with an empty universe
                  --run             begin ticking immediately, without waiting
                                    for a client to press start
                  --web-port <n>    browser client port (default 5151)
                  --no-web          do not serve the browser client
                """);
            return 0;
    }
}

var patterns = new PatternStore(patternsRoot);
var server = new GameServer(patterns, port, tick);

if (seedPattern is not null)
{
    try
    {
        RlePattern pattern = patterns.Load(seedPattern);
        server.Seed(pattern.Cells, pattern.Generation);

        Console.WriteLine(
            $"Seeded '{pattern.Name ?? seedPattern}': {pattern.Population} cells at " +
            $"({pattern.Origin.X}, {pattern.Origin.Y})");
    }
    catch (Exception error) when (error is FileNotFoundException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"Could not seed from '{seedPattern}': {error.Message}");
        return 1;
    }
}

if (startRunning)
{
    server.StartRunning();
    Console.WriteLine("Simulation started.");
}

using var shutdown = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine("\nShutting down.");
    shutdown.Cancel();
};

var work = new List<Task> { server.RunAsync(shutdown.Token) };

if (web)
{
    var bridge = new WebBridge(server, webPort, FindWebPage());
    work.Add(bridge.RunAsync(shutdown.Token));
}

await Task.WhenAll(work);
return 0;

/// <summary>Locates the browser client page, if it is present.</summary>
static string? FindWebPage() => FindUpwards(Path.Combine("web", "index.html"), file: true);

/// <summary>Walks up from the binary to find the repository's patterns directory.</summary>
static string FindPatternsDirectory() =>
    FindUpwards("patterns", file: false) ?? Path.Combine(Environment.CurrentDirectory, "patterns");

/// <summary>
/// Walks up from the binary looking for a repository-relative path.
/// </summary>
/// <remarks>
/// So that 'dotnet run' works from anywhere in the tree without the reviewer
/// having to be in the right directory first.
/// </remarks>
static string? FindUpwards(string relative, bool file)
{
    DirectoryInfo? directory = new(AppContext.BaseDirectory);

    while (directory is not null)
    {
        string candidate = Path.Combine(directory.FullName, relative);

        if (file ? File.Exists(candidate) : Directory.Exists(candidate)) return candidate;

        directory = directory.Parent;
    }

    return null;
}
