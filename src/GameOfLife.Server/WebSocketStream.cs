using System.Net.WebSockets;

namespace GameOfLife.Server;

/// <summary>
/// Presents a <see cref="WebSocket"/> as a duplex byte stream.
/// </summary>
/// <remarks>
/// <para>
/// WebSocket is message-oriented and our protocol is a self-delimiting byte
/// stream, so the adaptation is one-directional in each sense: reads
/// concatenate incoming message payloads into a stream, and each write sends
/// exactly one binary message. Because every write here is a whole frame, the
/// message boundaries happen to coincide with frame boundaries — but nothing
/// depends on that, since the length prefix delimits regardless.
/// </para>
/// <para>
/// Keeping the same frame format over both transports costs four redundant
/// bytes per message and buys one protocol specification instead of two. The
/// browser client parses exactly what a native client parses.
/// </para>
/// </remarks>
internal sealed class WebSocketStream(WebSocket socket) : Stream
{
    public override bool CanRead => true;

    public override bool CanWrite => true;

    public override bool CanSeek => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        try
        {
            ValueWebSocketReceiveResult result =
                await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);

            // A close message is end-of-stream, which is what a PipeReader
            // expects to see rather than an exception.
            return result.MessageType == WebSocketMessageType.Close ? 0 : result.Count;
        }
        catch (WebSocketException)
        {
            return 0;
        }
    }

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (socket.State != WebSocketState.Open) return;

        try
        {
            await socket.SendAsync(buffer, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception error) when (error is WebSocketException or ObjectDisposedException)
        {
            // The read side will observe the close and unwind the connection.
        }
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Write(byte[] buffer, int offset, int count) =>
        WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) socket.Dispose();
        base.Dispose(disposing);
    }
}
