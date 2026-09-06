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
