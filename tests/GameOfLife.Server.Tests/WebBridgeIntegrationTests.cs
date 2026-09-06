using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using GameOfLife.Core;
using GameOfLife.Protocol;
using GameOfLife.Server;

namespace GameOfLife.Server.Tests;

/// <summary>
/// A real WebSocket client against the bridge, over real sockets.
/// </summary>
/// <remarks>
/// Uses <see cref="ClientWebSocket"/> rather than a hand-written client, so the
/// handshake and framing are validated by an independent implementation of RFC
/// 6455 instead of by the one under test.
/// </remarks>
public class WebBridgeIntegrationTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"gol-web-{Guid.NewGuid():N}");
    private readonly CancellationTokenSource _shutdown = new();

    private GameServer _server = null!;
    private WebBridge _bridge = null!;
    private Task _running = null!;
    private IPEndPoint _endpoint = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);

        _server = new GameServer(new PatternStore(_root), port: 0, tickMilliseconds: 20);
        _bridge = new WebBridge(_server, port: 0, pagePath: null);

        _running = Task.WhenAll(
            _server.RunAsync(_shutdown.Token),
            _bridge.RunAsync(_shutdown.Token));

        while (_bridge.Endpoint is null) await Task.Delay(10);
        _endpoint = _bridge.Endpoint;
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

    private async Task<ClientWebSocket> ConnectAsync()
    {
        var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://{_endpoint}/ws"), CancellationToken.None);
        return client;
    }

    private static async Task SendAsync(ClientWebSocket socket, ProtocolMessage message)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        ControlCodec.WriteFrame(buffer, message);

        await socket.SendAsync(buffer.WrittenMemory, WebSocketMessageType.Binary, true, CancellationToken.None);
    }

    /// <summary>Receives one whole frame; each server frame is one message.</summary>
    private static async Task<(FrameType Type, byte[] Payload)> ReceiveAsync(
        ClientWebSocket socket, TimeSpan? timeout = null)
    {
        using var deadline = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        var buffer = new ArrayBufferWriter<byte>(4096);
        byte[] scratch = new byte[4096];

        while (true)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(scratch, deadline.Token);
            buffer.Write(scratch.AsSpan(0, result.Count));

            if (!result.EndOfMessage) continue;

            var sequence = new ReadOnlySequence<byte>(buffer.WrittenSpan.ToArray());

            if (FrameCodec.TryRead(ref sequence, out FrameType type, out byte[] payload))
                return (type, payload);

            buffer.Clear();
        }
    }

    private static async Task<T> ReceiveMessageAsync<T>(ClientWebSocket socket) where T : ProtocolMessage
    {
        while (true)
        {
            (FrameType type, byte[] payload) = await ReceiveAsync(socket);

            if (type == FrameType.Control && ControlCodec.Read(payload) is T message) return message;
        }
    }

    [Fact]
    public async Task A_Browser_Client_Completes_The_Handshake_And_Is_Greeted()
    {
        using ClientWebSocket socket = await ConnectAsync();

        Assert.Equal(WebSocketState.Open, socket.State);

        HelloMessage hello = await ReceiveMessageAsync<HelloMessage>(socket);
        Assert.Equal(ProtocolConstants.Version, hello.ProtocolVersion);
    }

    [Fact]
    public async Task A_Browser_Client_Receives_Viewport_Frames()
    {
        using ClientWebSocket socket = await ConnectAsync();
        await ReceiveMessageAsync<HelloMessage>(socket);

        while (true)
        {
            (FrameType type, byte[] payload) = await ReceiveAsync(socket);
            if (type != FrameType.Viewport) continue;

            (Viewport viewport, _, byte[] bitmap) = ViewportFrame.Read(payload);

            Assert.Equal(ProtocolConstants.DefaultViewportSize, viewport.Width);
            Assert.Equal(viewport.BitmapByteCount, bitmap.Length);
            return;
        }
    }

    /// <summary>
    /// The point of the bridge: a browser and a native client share one universe.
    /// </summary>
    [Fact]
    public async Task An_Edit_In_The_Browser_Reaches_A_Native_Client()
    {
        using ClientWebSocket browser = await ConnectAsync();
        await ReceiveMessageAsync<HelloMessage>(browser);

        await using TestClient native = await TestClient.ConnectAsync(_server.Endpoint!);
        await native.ReadMessageAsync<HelloMessage>();

        var target = new Cell((1UL << 63) + 21, (1UL << 63) + 12);
        await SendAsync(browser, new ToggleMessage { X = target.X, Y = target.Y });

        // The native client sent nothing and is on a different transport.
        await native.ReadUntilCellAliveAsync(target);
    }

    [Fact]
    public async Task An_Edit_On_A_Native_Client_Reaches_The_Browser()
    {
        using ClientWebSocket browser = await ConnectAsync();
        await ReceiveMessageAsync<HelloMessage>(browser);

        await using TestClient native = await TestClient.ConnectAsync(_server.Endpoint!);
        await native.ReadMessageAsync<HelloMessage>();

        var target = new Cell((1UL << 63) + 5, (1UL << 63) + 6);
        await native.SendAsync(new ToggleMessage { X = target.X, Y = target.Y });

        while (true)
        {
            (FrameType type, byte[] payload) = await ReceiveAsync(browser);
            if (type != FrameType.Viewport) continue;

            (Viewport viewport, _, byte[] bitmap) = ViewportFrame.Read(payload);

            if (viewport.TryLocate(target, out int x, out int y)
                && Viewport.IsSet(bitmap, viewport.BitIndex(x, y)))
            {
                return;
            }
        }
    }

    [Fact]
    public async Task The_Browser_Is_Subject_To_The_Same_Edit_Policy()
    {
        using ClientWebSocket socket = await ConnectAsync();
        await ReceiveMessageAsync<HelloMessage>(socket);

        await SendAsync(socket, new ControlMessage { Action = ControlAction.Start });
        await ReceiveMessageAsync<StatusMessage>(socket);

        await SendAsync(socket, new ToggleMessage { X = 1, Y = 1 });

        ErrorMessage error = await ReceiveMessageAsync<ErrorMessage>(socket);
        Assert.Equal(ErrorCode.EditWhileRunning, error.Code);
    }

    [Fact]
    public async Task A_Plain_Http_Request_Without_A_Page_Is_Answered_Not_Dropped()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(_endpoint);

        await using NetworkStream stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: x\r\n\r\n"));

        byte[] buffer = new byte[256];
        int read = await stream.ReadAsync(buffer);

        // pagePath is null in these tests, so 404 is the correct answer -- the
        // point is that the bridge replies rather than hanging up silently.
        Assert.Contains("404", Encoding.ASCII.GetString(buffer, 0, read), StringComparison.Ordinal);
    }
}
