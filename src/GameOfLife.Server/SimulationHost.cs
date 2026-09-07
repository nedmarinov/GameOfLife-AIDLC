using System.Buffers;
using System.Diagnostics;
using System.Threading.Channels;
using GameOfLife.Core;
using GameOfLife.Protocol;

namespace GameOfLife.Server;

/// <summary>
/// The one thread that owns the universe.
/// </summary>
/// <remarks>
/// <para>
/// Nothing else touches <see cref="Universe"/> or the client list. Connections
/// post <see cref="Command"/>s to a single bounded channel; this loop drains
/// them between ticks and applies them. Mutual exclusion is therefore
/// structural rather than enforced — there is no lock to forget to take, and no
/// lock ordering to get wrong. See ADR 0003.
/// </para>
/// </remarks>
internal sealed class SimulationHost
{
    /// <summary>
    /// Commands applied per tick.
    /// </summary>
    /// <remarks>
    /// Bounded so a client flooding toggles cannot starve the simulation: the
    /// loop applies at most this many and leaves the rest for the next tick,
    /// rather than draining an unbounded queue while generations stall.
    /// </remarks>
    private const int MaxCommandsPerTick = 512;

    /// <summary>Longest a client may hold an uncorrected frame, in milliseconds.</summary>
    private const int RefreshMilliseconds = 1_000;

    private readonly Universe _universe = new();
    private readonly List<ClientConnection> _clients = [];
    private readonly Channel<Command> _commands;
    private readonly PatternStore _patterns;

    private bool _running;
    private int _tickMilliseconds;
    private bool _dirty = true;
    private int _ticksSinceBroadcast;

    public SimulationHost(PatternStore patterns, int tickMilliseconds = ProtocolConstants.DefaultTickMilliseconds)
    {
        _patterns = patterns;
        _tickMilliseconds = tickMilliseconds;

        _commands = Channel.CreateBounded<Command>(
            new BoundedChannelOptions(4096)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            });
    }

    public ChannelWriter<Command> Commands => _commands.Writer;

    /// <summary>Ticks between unconditional broadcasts, at the current speed.</summary>
    private int RefreshTicks => Math.Max(1, RefreshMilliseconds / Math.Max(1, _tickMilliseconds));

    public ulong Generation => _universe.Generation;

    public int Population => _universe.Population;

    public bool IsRunning => _running;

    /// <summary>Seeds the universe before the server starts accepting clients.</summary>
    public void Seed(IEnumerable<Cell> cells, ulong generation = 0)
    {
        _universe.Reset(cells, generation);
        _dirty = true;
    }

    /// <summary>Starts ticking immediately, without waiting for a client to ask.</summary>
    public void StartRunning() => _running = true;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var clock = new Stopwatch();

        while (!cancellationToken.IsCancellationRequested)
        {
            clock.Restart();

            DrainCommands();

            if (_running)
            {
                _universe.Step();
                _dirty = true;
            }

            // Broadcast on change, and at least once a second regardless.
            //
            // The periodic refresh is not cosmetic. Egress queues drop the
            // oldest frame when full, so a client that falls behind at the
            // moment the simulation is paused would otherwise hold a stale
            // frame forever -- nothing would change, so nothing would correct
            // it. A floor on broadcast rate bounds how long any client can be
            // wrong to one second, while an idle universe still costs almost
            // nothing.
            _ticksSinceBroadcast++;

            if (_dirty || _ticksSinceBroadcast >= RefreshTicks)
            {
                Broadcast();
                _dirty = false;
                _ticksSinceBroadcast = 0;
            }

            // Subtract the work already done so the tick rate is the period,
            // not the period plus however long a generation took.
            TimeSpan remaining = TimeSpan.FromMilliseconds(_tickMilliseconds) - clock.Elapsed;

            if (remaining > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private void DrainCommands()
    {
        for (int applied = 0; applied < MaxCommandsPerTick; applied++)
        {
            if (!_commands.Reader.TryRead(out Command? command)) return;
            Apply(command);
        }
    }

    private void Apply(Command command)
    {
        switch (command)
        {
            case ClientConnected(var client):
                _clients.Add(client);
                _ = client.SendAsync(new HelloMessage
                {
                    Generation = _universe.Generation,
                    Running = _running,
                    TickMilliseconds = _tickMilliseconds,
                    Population = _universe.Population,
                });
                _dirty = true;
                break;

            case ClientDisconnected(var client):
                _clients.Remove(client);
                break;

            case SetViewport(var client, var viewport):
                client.Viewport = viewport;
                _dirty = true;
                break;

            case PanViewport(var client, long dx, long dy):
                client.Viewport = client.Viewport.Pan(dx, dy);
                _dirty = true;
                break;

            case ZoomViewport(var client, int delta):
                // ZoomBy clamps, so a client can send a large delta to reach
                // either extreme without knowing the limit.
                client.Viewport = client.Viewport.ZoomBy(delta);
                _dirty = true;
                break;

            case ToggleCell(var client, var cell):
                ApplyToggle(client, cell);
                break;

            case Control(var client, var action, int value):
                ApplyControl(client, action, value);
                break;

            case ListPatterns(var client):
                _ = client.SendAsync(new CatalogueMessage
                {
                    Patterns = [.. _patterns.List().Select(entry => new PatternEntry
                    {
                        File = entry.File,
                        Name = entry.Pattern?.Name,
                        Population = entry.Pattern?.Population,
                        Width = entry.Pattern?.Width,
                        Height = entry.Pattern?.Height,
                        Error = entry.Error,
                    })],
                });
                break;

            case LoadPattern(var client, string file):
                ApplyLoad(client, file);
                break;

            case SavePattern(var client, string file):
                ApplySave(client, file);
                break;
        }
    }

    /// <summary>
    /// Applies an edit, or explains why it was refused.
    /// </summary>
    /// <remarks>
    /// The brief has clients "observe" a running simulation and separately asks
    /// the UI to configure the "initial state", so edits are confined to the
    /// paused state. Refusals are explicit: a silently dropped toggle would look
    /// to the user like a lost keystroke rather than a rule.
    /// </remarks>
    private void ApplyToggle(ClientConnection client, Cell cell)
    {
        if (_running)
        {
            _ = client.SendErrorAsync(ErrorCode.EditWhileRunning,
                "Pause the simulation before editing cells.");
            return;
        }

        _universe.Toggle(cell);
        _dirty = true;
    }

    private void ApplyControl(ClientConnection client, ControlAction action, int value)
    {
        switch (action)
        {
            case ControlAction.Start:
                _running = true;
                break;

            case ControlAction.Pause:
                _running = false;
                break;

            case ControlAction.Step:
                // Single-stepping while running would race the tick loop, so it
                // only applies when paused.
                if (!_running) _universe.Step();
                break;

            case ControlAction.Clear:
                _universe.Clear();
                _running = false;
                break;

            case ControlAction.Speed:
                _tickMilliseconds = Math.Clamp(value, 10, 5_000);
                break;
        }

        _dirty = true;
        BroadcastStatus();
    }

    private void ApplyLoad(ClientConnection client, string file)
    {
        try
        {
            RlePattern pattern = _patterns.Load(file);

            _universe.Reset(pattern.Cells, pattern.Generation);
            _running = false;

            // Move the requesting client's window to the pattern, so a load is
            // visibly a load rather than an apparently empty universe.
            client.Viewport = Centre(client.Viewport, pattern);

            _dirty = true;
            BroadcastStatus();
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            _ = client.SendErrorAsync(ErrorCode.FileNotFound, error.Message);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or FormatException or NotSupportedException or IOException)
        {
            _ = client.SendErrorAsync(ErrorCode.FileError, error.Message);
        }
    }

    /// <summary>
    /// Centres a window on a pattern, at whatever zoom the window is using.
    /// </summary>
    /// <remarks>
    /// The offset must be measured in universe cells, which means scaling by
    /// the zoom. An earlier version subtracted half the window's <em>width in
    /// displayed cells</em>, so at zoom 8 a loaded pattern landed some twelve
    /// thousand cells from where it belonged and appeared jammed in the
    /// top-left corner instead of the middle. <see cref="Viewport.CoverageWidth"/>
    /// is the same quantity already scaled.
    /// </remarks>
    private static Viewport Centre(Viewport viewport, RlePattern pattern)
    {
        ulong centreX = unchecked(pattern.Origin.X + ((ulong)pattern.Width >> 1));
        ulong centreY = unchecked(pattern.Origin.Y + ((ulong)pattern.Height >> 1));

        return viewport with
        {
            OriginX = unchecked(centreX - (viewport.CoverageWidth >> 1)),
            OriginY = unchecked(centreY - (viewport.CoverageHeight >> 1)),
        };
    }

    private void ApplySave(ClientConnection client, string file)
    {
        try
        {
            _patterns.Save(_universe, file, name: $"Saved at generation {_universe.Generation}");
            _ = client.SendAsync(BuildStatus());
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or NotSupportedException)
        {
            _ = client.SendErrorAsync(ErrorCode.FileError, error.Message);
        }
    }

    /// <summary>
    /// Sends the current generation to every client.
    /// </summary>
    /// <remarks>
    /// Rendering and framing happen once per <em>distinct</em> viewport rather
    /// than once per client, so ten clients watching the same window cost one
    /// render. The frame bytes are shared by reference: every client receives
    /// identical content, and nothing mutates a frame after it is published.
    /// </remarks>
    private void Broadcast()
    {
        if (_clients.Count == 0) return;

        Dictionary<Viewport, ReadOnlyMemory<byte>> rendered = [];

        foreach (ClientConnection client in _clients)
        {
            if (!rendered.TryGetValue(client.Viewport, out ReadOnlyMemory<byte> frame))
            {
                frame = RenderFrame(client.Viewport);
                rendered[client.Viewport] = frame;
            }

            client.Publish(frame);
        }
    }

    private ReadOnlyMemory<byte> RenderFrame(in Viewport viewport)
    {
        var payload = new ArrayBufferWriter<byte>(ViewportFrame.PayloadLength(viewport));
        ViewportFrame.Write(payload, _universe, viewport, _universe.Generation);

        var framed = new ArrayBufferWriter<byte>(payload.WrittenCount + FrameCodec.HeaderLength);
        FrameCodec.Write(framed, FrameType.Viewport, payload.WrittenSpan);

        return framed.WrittenMemory;
    }

    private StatusMessage BuildStatus() => new()
    {
        Generation = _universe.Generation,
        Running = _running,
        TickMilliseconds = _tickMilliseconds,
        Population = _universe.Population,
    };

    private void BroadcastStatus()
    {
        StatusMessage status = BuildStatus();
        foreach (ClientConnection client in _clients) _ = client.SendAsync(status);
    }
}
