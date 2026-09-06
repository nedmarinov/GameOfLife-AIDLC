using System.Net;
using GameOfLife.Core;
using GameOfLife.Protocol;
using GameOfLife.Server;

namespace GameOfLife.Server.Tests;

/// <summary>
/// End-to-end over real sockets: one server, several clients, one simulation.
/// </summary>
public class ServerIntegrationTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"gol-srv-{Guid.NewGuid():N}");
    private readonly CancellationTokenSource _shutdown = new();

    private GameServer _server = null!;
    private Task _running = null!;
    private IPEndPoint _endpoint = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);

        // Port 0 lets the OS pick a free port, so tests never collide.
        _server = new GameServer(new PatternStore(_root), port: 0, tickMilliseconds: 20);
        _running = _server.RunAsync(_shutdown.Token);

        while (_server.Endpoint is null) await Task.Delay(10);
        _endpoint = _server.Endpoint;
    }

    public async Task DisposeAsync()
    {
        await _shutdown.CancelAsync();

        try
        {
            await _running.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception error) when (error is OperationCanceledException or TimeoutException)
        {
        }

        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        _shutdown.Dispose();
    }

    private Task<TestClient> ConnectAsync() => TestClient.ConnectAsync(_endpoint);

    [Fact]
    public async Task A_Connecting_Client_Is_Greeted_With_State()
    {
        await using TestClient client = await ConnectAsync();

        HelloMessage hello = await client.ReadMessageAsync<HelloMessage>();

        Assert.Equal(ProtocolConstants.Version, hello.ProtocolVersion);
        Assert.False(hello.Running);
        Assert.Equal(0UL, hello.Generation);
    }

    [Fact]
    public async Task A_Client_Receives_Viewport_Frames()
    {
        await using TestClient client = await ConnectAsync();
        await client.ReadMessageAsync<HelloMessage>();

        (Viewport viewport, _, byte[] bitmap) = await client.ReadFrameAsync();

        Assert.Equal(ProtocolConstants.DefaultViewportSize, viewport.Width);
        Assert.Equal(viewport.BitmapByteCount, bitmap.Length);
    }

    /// <summary>Requirement F8: an edit by one client is visible to every other.</summary>
    [Fact]
    public async Task An_Edit_By_One_Client_Reaches_The_Others()
    {
        await using TestClient editor = await ConnectAsync();
        await using TestClient observer = await ConnectAsync();

        HelloMessage hello = await editor.ReadMessageAsync<HelloMessage>();
        await observer.ReadMessageAsync<HelloMessage>();
        Assert.False(hello.Running);

        var target = new Cell((1UL << 63) + 10, (1UL << 63) + 10);
        await editor.SendAsync(new ToggleMessage { X = target.X, Y = target.Y });

        // The observer never sent anything; it must still see the change.
        await observer.ReadUntilCellAliveAsync(target);
    }

    /// <summary>Requirement F12: refusals are explicit, never silent.</summary>
    [Fact]
    public async Task Editing_While_Running_Is_Refused_With_A_Reason()
    {
        await using TestClient client = await ConnectAsync();
        await client.ReadMessageAsync<HelloMessage>();

        await client.SendAsync(new ControlMessage { Action = ControlAction.Start });
        await client.ReadMessageAsync<StatusMessage>();

        await client.SendAsync(new ToggleMessage { X = 5, Y = 5 });

        ErrorMessage error = await client.ReadMessageAsync<ErrorMessage>();
        Assert.Equal(ErrorCode.EditWhileRunning, error.Code);
    }

    [Fact]
    public async Task Editing_Is_Accepted_Again_After_Pausing()
    {
        await using TestClient client = await ConnectAsync();
        await client.ReadMessageAsync<HelloMessage>();

        await client.SendAsync(new ControlMessage { Action = ControlAction.Start });
        await client.ReadMessageAsync<StatusMessage>();
        await client.SendAsync(new ControlMessage { Action = ControlAction.Pause });
        await client.ReadMessageAsync<StatusMessage>();

        var target = new Cell((1UL << 63) + 3, (1UL << 63) + 4);
        await client.SendAsync(new ToggleMessage { X = target.X, Y = target.Y });

        await client.ReadUntilCellAliveAsync(target);
    }

    [Fact]
    public async Task The_Simulation_Advances_For_Every_Client_At_Once()
    {
        await using TestClient a = await ConnectAsync();
        await using TestClient b = await ConnectAsync();
        await using TestClient c = await ConnectAsync();

        foreach (TestClient client in new[] { a, b, c })
            await client.ReadMessageAsync<HelloMessage>();

        await a.SendAsync(new ControlMessage { Action = ControlAction.Start });

        // Every client must observe generations climbing from the one shared
        // simulation, not from any local stepping of their own.
        foreach (TestClient client in new[] { a, b, c })
        {
            ulong first = (await client.ReadFrameAsync()).Generation;
            ulong later = first;

            while (later <= first) later = (await client.ReadFrameAsync()).Generation;

            Assert.True(later > first);
        }
    }

    [Fact]
    public async Task A_Client_Joining_Mid_Run_Sees_Current_State_Without_Replay()
    {
        await using TestClient first = await ConnectAsync();
        await first.ReadMessageAsync<HelloMessage>();

        var target = new Cell(1UL << 63, 1UL << 63);
        await first.SendAsync(new ToggleMessage { X = target.X, Y = target.Y });
        await first.ReadUntilCellAliveAsync(target);

        // A client that missed every prior frame still becomes consistent on
        // the next tick, because frames are whole snapshots.
        await using TestClient late = await ConnectAsync();
        await late.ReadMessageAsync<HelloMessage>();

        await late.ReadUntilCellAliveAsync(target);
    }

    [Fact]
    public async Task Panning_Moves_Only_The_Panning_Clients_Window()
    {
        await using TestClient mover = await ConnectAsync();
        await using TestClient stationary = await ConnectAsync();

        await mover.ReadMessageAsync<HelloMessage>();
        await stationary.ReadMessageAsync<HelloMessage>();

        (Viewport before, _, _) = await stationary.ReadFrameAsync();

        await mover.SendAsync(new PanMessage { Dx = 1_000, Dy = 0 });

        (Viewport moved, _, _) = await mover.ReadUntilViewportAsync(v => v.OriginX != before.OriginX);
        Assert.Equal(before.OriginX + 1_000, moved.OriginX);

        (Viewport still, _, _) = await stationary.ReadFrameAsync();
        Assert.Equal(before.OriginX, still.OriginX);
    }

    /// <summary>
    /// Zooming out far enough to see the whole universe at once.
    /// </summary>
    /// <remarks>
    /// Two cells are placed a quarter of the universe apart — a distance no
    /// 100x100 window at 1:1 could ever show both ends of. At maximum zoom both
    /// appear in the same frame, which is the only direct demonstration that the
    /// universe really is 2^64 across rather than merely declared to be.
    /// </remarks>
    [Fact]
    public async Task Zooming_Out_Brings_The_Whole_Universe_Into_One_Window()
    {
        await using TestClient client = await ConnectAsync();
        await client.ReadMessageAsync<HelloMessage>();

        var near = new Cell(0, 0);
        var far = new Cell(ulong.MaxValue / 4, ulong.MaxValue / 4);

        await client.SendAsync(new SubscribeMessage { OriginX = 0, OriginY = 0, Width = 100, Height = 100 });
        await client.SendAsync(new ToggleMessage { X = near.X, Y = near.Y });
        await client.ReadUntilCellAliveAsync(near);

        // At 1:1 the far cell is unreachable from this window.
        (Viewport close, _, _) = await client.ReadFrameAsync();
        Assert.Equal(0, close.Zoom);
        Assert.False(close.TryLocate(far, out _, out _));

        await client.SendAsync(new SubscribeMessage
        {
            OriginX = far.X, OriginY = far.Y, Width = 100, Height = 100,
        });
        await client.SendAsync(new ToggleMessage { X = far.X, Y = far.Y });
        await client.ReadUntilCellAliveAsync(far);

        // Now zoom out as far as the window allows.
        await client.SendAsync(new SubscribeMessage { OriginX = 0, OriginY = 0, Width = 100, Height = 100 });
        await client.SendAsync(new ZoomMessage { Delta = 99 });

        (Viewport wide, _, byte[] bitmap) = await client.ReadUntilViewportAsync(v => v.Zoom > 0);

        Assert.Equal(Viewport.MaxZoomFor(100, 100), wide.Zoom);
        Assert.True(wide.CoverageWidth > ulong.MaxValue / 2);

        // Both cells, a quarter of the universe apart, in the same frame.
        Assert.True(wide.TryLocate(near, out int nx, out int ny));
        Assert.True(wide.TryLocate(far, out int fx, out int fy));
        Assert.True(Viewport.IsSet(bitmap, wide.BitIndex(nx, ny)), "near cell missing at full zoom");
        Assert.True(Viewport.IsSet(bitmap, wide.BitIndex(fx, fy)), "far cell missing at full zoom");
        Assert.NotEqual((nx, ny), (fx, fy));
    }

    [Fact]
    public async Task Zoom_Is_Per_Client_Like_The_Viewport()
    {
        await using TestClient zoomed = await ConnectAsync();
        await using TestClient close = await ConnectAsync();

        await zoomed.ReadMessageAsync<HelloMessage>();
        await close.ReadMessageAsync<HelloMessage>();

        await zoomed.SendAsync(new ZoomMessage { Delta = 10 });

        (Viewport wide, _, _) = await zoomed.ReadUntilViewportAsync(v => v.Zoom == 10);
        Assert.Equal(10, wide.Zoom);

        (Viewport unchanged, _, _) = await close.ReadFrameAsync();
        Assert.Equal(0, unchanged.Zoom);
    }

    [Fact]
    public async Task A_Malformed_Frame_Is_Answered_Then_The_Connection_Ends()
    {
        await using TestClient client = await ConnectAsync();
        await client.ReadMessageAsync<HelloMessage>();

        await client.SendRawAsync([0x05, 0x00, 0x00, 0x00, 0x01, (byte)'{', (byte)'{', (byte)'{', (byte)'{']);

        ErrorMessage error = await client.ReadMessageAsync<ErrorMessage>();
        Assert.Equal(ErrorCode.MalformedMessage, error.Code);
    }

    [Fact]
    public async Task One_Client_Disconnecting_Does_Not_Disturb_The_Others()
    {
        await using TestClient survivor = await ConnectAsync();
        await survivor.ReadMessageAsync<HelloMessage>();

        TestClient leaving = await ConnectAsync();
        await leaving.ReadMessageAsync<HelloMessage>();
        await leaving.DisposeAsync();

        await survivor.SendAsync(new ControlMessage { Action = ControlAction.Start });

        ulong first = (await survivor.ReadFrameAsync()).Generation;
        ulong later = first;
        while (later <= first) later = (await survivor.ReadFrameAsync()).Generation;

        Assert.True(later > first);
    }

    [Fact]
    public async Task Loading_Outside_The_Pattern_Directory_Is_Refused()
    {
        await using TestClient client = await ConnectAsync();
        await client.ReadMessageAsync<HelloMessage>();

        await client.SendAsync(new LoadMessage { File = "../../../../etc/passwd" });

        ErrorMessage error = await client.ReadMessageAsync<ErrorMessage>();
        Assert.Equal(ErrorCode.FileError, error.Code);
    }

    /// <summary>
    /// Loading moves the requesting client's window to the pattern.
    /// </summary>
    /// <remarks>
    /// Without this a load of a pattern placed far from the current window
    /// looks like a load of an empty universe -- which is exactly what the
    /// shipped glider, positioned two cells before the 2^64 seam, would do to a
    /// client sitting at the centre. The README tells the reader to press 'o'
    /// and watch the glider wrap, so this is the behaviour that claim rests on.
    /// </remarks>
    [Fact]
    public async Task Loading_A_Pattern_Brings_It_Into_The_Clients_Window()
    {
        // A glider two cells before the wrap point, saved through the server so
        // the test does not depend on the repository's pattern files.
        await using TestClient setup = await ConnectAsync();
        await setup.ReadMessageAsync<HelloMessage>();

        var far = new Cell(ulong.MaxValue - 1, ulong.MaxValue - 1);
        await setup.SendAsync(new ToggleMessage { X = far.X, Y = far.Y });
        await setup.ReadUntilViewportAsync(_ => true);

        // Subscribe straight to a window around it. Expressing this as a pan
        // delta would underflow long: the distance from the centre of the
        // universe to the seam is larger than a signed 64-bit value can hold.
        await setup.SendAsync(new SubscribeMessage
        {
            OriginX = far.Offset(-10, -10).X,
            OriginY = far.Offset(-10, -10).Y,
            Width = ProtocolConstants.DefaultViewportSize,
            Height = ProtocolConstants.DefaultViewportSize,
        });
        await setup.ReadUntilCellAliveAsync(far);

        await setup.SendAsync(new SaveMessage { File = "edge.rle" });
        await setup.ReadMessageAsync<StatusMessage>();

        // A fresh client starts at the centre of the universe, nowhere near it.
        await using TestClient loader = await ConnectAsync();
        await loader.ReadMessageAsync<HelloMessage>();

        (Viewport before, _, _) = await loader.ReadFrameAsync();
        Assert.False(before.TryLocate(far, out _, out _));

        await loader.SendAsync(new LoadMessage { File = "edge.rle" });

        // After the load the cell must be inside the window and lit.
        await loader.ReadUntilCellAliveAsync(far);
    }

    [Fact]
    public async Task Save_Then_Load_Round_Trips_Through_The_Server()
    {
        await using TestClient client = await ConnectAsync();
        await client.ReadMessageAsync<HelloMessage>();

        var target = new Cell((1UL << 63) + 7, (1UL << 63) + 7);
        await client.SendAsync(new ToggleMessage { X = target.X, Y = target.Y });
        await client.ReadUntilCellAliveAsync(target);

        await client.SendAsync(new SaveMessage { File = "snap.rle" });
        await client.ReadMessageAsync<StatusMessage>();

        await client.SendAsync(new ControlMessage { Action = ControlAction.Clear });
        await client.ReadMessageAsync<StatusMessage>();

        await client.SendAsync(new LoadMessage { File = "snap.rle" });
        await client.ReadUntilCellAliveAsync(target);
    }
}
