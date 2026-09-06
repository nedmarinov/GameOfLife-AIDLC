using System.Buffers;
using System.Net;
using System.Net.Sockets;
using GameOfLife.Core;
using GameOfLife.Protocol;

namespace GameOfLife.Server.Tests;

/// <summary>A real TCP client, so the integration tests exercise the real stack.</summary>
internal sealed class TestClient : IAsyncDisposable
{
    private readonly TcpClient _socket;
    private readonly NetworkStream _stream;
    private readonly List<byte> _buffered = [];

    private TestClient(TcpClient socket)
    {
        _socket = socket;
        _stream = socket.GetStream();
    }

    public static async Task<TestClient> ConnectAsync(IPEndPoint endpoint)
    {
        var socket = new TcpClient();
        await socket.ConnectAsync(endpoint);
        return new TestClient(socket);
    }

    public async Task SendAsync(ProtocolMessage message)
    {
        var buffer = new ArrayBufferWriter<byte>();
        ControlCodec.WriteFrame(buffer, message);
        await _stream.WriteAsync(buffer.WrittenMemory);
    }

    /// <summary>Reads until a frame satisfying the predicate arrives, or the timeout expires.</summary>
    public async Task<(FrameType Type, byte[] Payload)> ReadUntilAsync(
        Func<FrameType, byte[], bool> predicate, TimeSpan? timeout = null)
    {
        using var deadline = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        byte[] scratch = new byte[8192];

        while (true)
        {
            // Anything already buffered may already contain the answer.
            while (TryTakeFrame(out FrameType type, out byte[] payload))
            {
                if (predicate(type, payload)) return (type, payload);
            }

            int read = await _stream.ReadAsync(scratch, deadline.Token);
            if (read == 0) throw new IOException("Server closed the connection.");

            _buffered.AddRange(scratch.AsSpan(0, read).ToArray());
        }
    }

    public async Task<T> ReadMessageAsync<T>(TimeSpan? timeout = null) where T : ProtocolMessage
    {
        (_, byte[] payload) = await ReadUntilAsync(
            (type, bytes) => type == FrameType.Control && ControlCodec.Read(bytes) is T, timeout);

        return (T)ControlCodec.Read(payload);
    }

    /// <summary>Waits for a viewport frame in which the given cell is alive.</summary>
    public async Task<ulong> ReadUntilCellAliveAsync(Cell cell, TimeSpan? timeout = null)
    {
        (_, byte[] payload) = await ReadUntilAsync((type, bytes) =>
        {
            if (type != FrameType.Viewport) return false;

            (Viewport viewport, _, byte[] bitmap) = ViewportFrame.Read(bytes);

            return viewport.TryLocate(cell, out int x, out int y)
                && Viewport.IsSet(bitmap, viewport.BitIndex(x, y));
        }, timeout);

        (_, ulong generation, _) = ViewportFrame.Read(payload);
        return generation;
    }

    public async Task<(Viewport Viewport, ulong Generation, byte[] Bitmap)> ReadFrameAsync(
        TimeSpan? timeout = null)
    {
        (_, byte[] payload) = await ReadUntilAsync((type, _) => type == FrameType.Viewport, timeout);
        return ViewportFrame.Read(payload);
    }

    /// <summary>Waits for a viewport frame whose window satisfies the predicate.</summary>
    public async Task<(Viewport Viewport, ulong Generation, byte[] Bitmap)> ReadUntilViewportAsync(
        Func<Viewport, bool> predicate, TimeSpan? timeout = null)
    {
        (_, byte[] payload) = await ReadUntilAsync((type, bytes) =>
            type == FrameType.Viewport && predicate(ViewportFrame.Read(bytes).Viewport), timeout);

        return ViewportFrame.Read(payload);
    }

    /// <summary>Writes bytes straight to the socket, bypassing the codec.</summary>
    public Task SendRawAsync(byte[] bytes) => _stream.WriteAsync(bytes).AsTask();

    private bool TryTakeFrame(out FrameType type, out byte[] payload)
    {
        var sequence = new ReadOnlySequence<byte>([.. _buffered]);

        if (!FrameCodec.TryRead(ref sequence, out type, out payload)) return false;

        _buffered.RemoveRange(0, _buffered.Count - (int)sequence.Length);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        _stream.Dispose();
        _socket.Dispose();
        await ValueTask.CompletedTask;
    }
}
