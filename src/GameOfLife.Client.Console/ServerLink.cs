using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading.Channels;
using GameOfLife.Protocol;

namespace GameOfLife.Client.Console;

/// <summary>Something the server sent us.</summary>
internal readonly record struct Inbound(FrameType Type, byte[] Payload);

/// <summary>
/// The connection to the server, with reconnection.
/// </summary>
/// <remarks>
/// Reconnecting matters more than it looks: an observer left running overnight
/// should survive a server restart without the user noticing anything but a
/// pause. On reconnect the viewport is re-subscribed, because the server keeps
/// no memory of a client that went away.
/// </remarks>
internal sealed class ServerLink(string host, int port) : IAsyncDisposable
{
    private static readonly TimeSpan MinRetry = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxRetry = TimeSpan.FromSeconds(5);

    private readonly Channel<Inbound> _inbound = Channel.CreateUnbounded<Inbound>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    private TcpClient? _socket;
    private NetworkStream? _stream;

    public ChannelReader<Inbound> Inbound => _inbound.Reader;

    public bool IsConnected { get; private set; }

    /// <summary>Raised after every successful (re)connection, so state can be restored.</summary>
    public event Func<Task>? Connected;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        TimeSpan retry = MinRetry;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _socket = new TcpClient { NoDelay = true };
                await _socket.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
                _stream = _socket.GetStream();

                IsConnected = true;
                retry = MinRetry;

                if (Connected is not null) await Connected().ConfigureAwait(false);

                await ReadLoopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception error) when (error is SocketException or IOException or ObjectDisposedException)
            {
                // Expected whenever the server is down or goes away.
            }
            finally
            {
                IsConnected = false;
                _stream?.Dispose();
                _socket?.Dispose();
            }

            try
            {
                await Task.Delay(retry, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // Back off so a server that stays down is not hammered, but stay
            // responsive enough that a quick restart is picked up promptly.
            retry = TimeSpan.FromMilliseconds(Math.Min(retry.TotalMilliseconds * 2, MaxRetry.TotalMilliseconds));
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        // leaveOpen so completing the reader does not dispose a stream the
        // sender still holds -- the same trap the server hit in Bolt 4.
        PipeReader reader = PipeReader.Create(_stream!, new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ReadResult result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                ReadOnlySequence<byte> buffer = result.Buffer;

                while (FrameCodec.TryRead(ref buffer, out FrameType type, out byte[] payload))
                    await _inbound.Writer.WriteAsync(new Inbound(type, payload), cancellationToken).ConfigureAwait(false);

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted) break;
            }
        }
        finally
        {
            await reader.CompleteAsync().ConfigureAwait(false);
        }
    }

    public async Task SendAsync(ProtocolMessage message, CancellationToken cancellationToken = default)
    {
        NetworkStream? stream = _stream;
        if (stream is null || !IsConnected) return;

        var buffer = new ArrayBufferWriter<byte>(256);
        ControlCodec.WriteFrame(buffer, message);

        try
        {
            await stream.WriteAsync(buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or SocketException or ObjectDisposedException)
        {
            // The read loop will notice and reconnect; a dropped keystroke is
            // not worth surfacing to the user.
        }
    }

    public ValueTask DisposeAsync()
    {
        _inbound.Writer.TryComplete();
        _stream?.Dispose();
        _socket?.Dispose();
        return ValueTask.CompletedTask;
    }
}
