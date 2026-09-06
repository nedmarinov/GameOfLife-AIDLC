using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading.Channels;
using GameOfLife.Core;
using GameOfLife.Protocol;

namespace GameOfLife.Server;

/// <summary>
/// One connected client: a frame reader, a bounded egress queue, and a writer.
/// </summary>
/// <remarks>
/// <para>
/// The egress queue holds <b>two</b> frames and drops the oldest when full.
/// That is the load-bearing decision of ADR 0003: a slow client must never
/// stall the simulation or any other client. Because each frame is a complete
/// snapshot rather than a delta, discarding a stale one is not a compromise —
/// it is correct. A client on a congested link renders the newest state and
/// silently skips intermediate generations, which is exactly what an observer
/// should do.
/// </para>
/// <para>
/// The alternative, an unbounded queue, would let a peer that simply stops
/// reading consume server memory without limit. That is a remote party
/// dictating the server's footprint, and it is the failure mode a device
/// gateway must not have.
/// </para>
/// </remarks>
internal sealed class ClientConnection : IAsyncDisposable
{
    /// <summary>
    /// Frames buffered per client before the oldest is discarded.
    /// </summary>
    /// <remarks>
    /// Two, not one: one in flight to the socket plus one queued behind it
    /// absorbs ordinary scheduling jitter without ever letting a backlog build.
    /// </remarks>
    private const int EgressCapacity = 2;

    private readonly Stream _stream;
    private readonly IDisposable? _owner;
    private readonly Channel<ReadOnlyMemory<byte>> _egress;
    private readonly CancellationTokenSource _closing = new();

    private int _droppedFrames;

    /// <summary>
    /// Wraps any duplex byte stream carrying framed messages.
    /// </summary>
    /// <remarks>
    /// Deliberately a <see cref="Stream"/> rather than a socket: the same
    /// connection logic serves a raw TCP client and a browser over WebSocket,
    /// because the frame format is one specification with two transports. Only
    /// the bytes' delivery differs.
    /// </remarks>
    public ClientConnection(Stream stream, int id, string description, IDisposable? owner = null)
    {
        _stream = stream;
        _owner = owner;
        Id = id;
        Description = description;

        _egress = Channel.CreateBounded<ReadOnlyMemory<byte>>(
            new BoundedChannelOptions(EgressCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            });
    }

    public int Id { get; }

    /// <summary>Where this client came from, for the log.</summary>
    public string Description { get; }

    /// <summary>
    /// The window this client is watching.
    /// </summary>
    /// <remarks>
    /// Read and written only by the simulation thread, so it needs no
    /// synchronisation despite living on a per-connection object.
    /// </remarks>
    public Viewport Viewport { get; set; } =
        new(1UL << 63, 1UL << 63, ProtocolConstants.DefaultViewportSize, ProtocolConstants.DefaultViewportSize);

    /// <summary>Frames discarded because this client could not keep up.</summary>
    public int DroppedFrames => _droppedFrames;

    public override string ToString() => $"client {Id}";

    /// <summary>
    /// Queues a frame, discarding this client's oldest if it is still behind.
    /// </summary>
    /// <remarks>
    /// Never blocks and never fails. Called from the simulation thread, which
    /// must not be delayed by any client's link speed.
    /// </remarks>
    public void Publish(ReadOnlyMemory<byte> frame)
    {
        // DropOldest makes TryWrite always succeed; a false return means the
        // channel is completed, i.e. the client is already going away.
        if (!_egress.Writer.TryWrite(frame)) return;

        if (_egress.Reader.Count >= EgressCapacity) Interlocked.Increment(ref _droppedFrames);
    }

    /// <summary>
    /// Reads frames from the socket and turns them into commands.
    /// </summary>
    /// <remarks>
    /// Runs until the peer disconnects or sends something that breaks the
    /// framing rules. A protocol violation ends the connection rather than
    /// being skipped: once the stream is out of sync there is no way to know
    /// where the next frame begins.
    /// </remarks>
    public async Task ReadLoopAsync(ChannelWriter<Command> commands, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closing.Token);

        // leaveOpen is mandatory here, not a preference. PipeReader.Create
        // disposes the stream on CompleteAsync by default, so the read loop's
        // own cleanup would close the socket out from under the write loop --
        // discarding whatever it had left to flush, including the ErrorMessage
        // that explains why the connection is ending.
        PipeReader reader = PipeReader.Create(
            _stream, new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            while (!linked.Token.IsCancellationRequested)
            {
                ReadResult result = await reader.ReadAsync(linked.Token).ConfigureAwait(false);
                ReadOnlySequence<byte> buffer = result.Buffer;

                try
                {
                    while (FrameCodec.TryRead(ref buffer, out FrameType type, out byte[] payload))
                    {
                        if (type != FrameType.Control)
                        {
                            await SendErrorAsync(ErrorCode.MalformedMessage,
                                $"Frame type {(byte)type} is not accepted from a client.").ConfigureAwait(false);
                            continue;
                        }

                        Command? command = Translate(ControlCodec.Read(payload));
                        if (command is not null)
                            await commands.WriteAsync(command, linked.Token).ConfigureAwait(false);
                    }
                }
                catch (ProtocolException error)
                {
                    await SendErrorAsync(ErrorCode.MalformedMessage, error.Message).ConfigureAwait(false);
                    return;
                }

                // Tell the pipe what was consumed and how far we inspected, so
                // it knows to wait for more data rather than spinning.
                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted) break;
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a fault.
        }
        catch (Exception error) when (error is IOException or SocketException or ObjectDisposedException)
        {
            // A peer vanishing is ordinary.
        }
        finally
        {
            await reader.CompleteAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Drains the egress queue to the socket.
    /// </summary>
    /// <remarks>
    /// Exits when the queue is <em>completed</em> rather than when the
    /// connection is cancelled, so anything already queued still reaches the
    /// peer. That matters for the last message on a doomed connection: the
    /// server promises an explicit error before hanging up, and a hard cancel
    /// here would break that promise. Only the outer token, meaning process
    /// shutdown, aborts mid-flush.
    /// </remarks>
    public async Task WriteLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (ReadOnlyMemory<byte> frame in
                _egress.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await _stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
                await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error) when (error is IOException or SocketException or ObjectDisposedException)
        {
        }
    }

    public async Task SendAsync(ProtocolMessage message)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        ControlCodec.WriteFrame(buffer, message);

        Publish(buffer.WrittenMemory);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public Task SendErrorAsync(ErrorCode code, string message) =>
        SendAsync(new ErrorMessage { Code = code, Message = message });

    /// <summary>Maps a wire message onto a simulation command.</summary>
    private Command? Translate(ProtocolMessage message) => message switch
    {
        SubscribeMessage m => new SetViewport(this, BuildViewport(m)),

        ToggleMessage m => new ToggleCell(this, new Cell(m.X, m.Y)),
        PanMessage m => new PanViewport(this, m.Dx, m.Dy),
        ZoomMessage m => new ZoomViewport(this, m.Delta),
        ControlMessage m => new Control(this, m.Action, m.Value),
        ListMessage => new ListPatterns(this),
        LoadMessage m => new LoadPattern(this, m.File),
        SaveMessage m => new SavePattern(this, m.File),

        // Server-to-client messages arriving from a client are ignored rather
        // than treated as an error; they are harmless and may just be an echo.
        _ => null,
    };

    /// <summary>
    /// Stops accepting new frames and lets the writer drain what is queued.
    /// </summary>
    /// <remarks>
    /// The graceful half of shutdown. <see cref="Close"/> is the abrupt half.
    /// </remarks>
    /// <summary>
    /// Builds a viewport from a subscribe request, clamped to what is legal.
    /// </summary>
    /// <remarks>
    /// Dimensions and zoom both arrive from a network peer. Clamping rather
    /// than rejecting keeps an over-eager client working instead of
    /// disconnecting it, while the <see cref="Viewport"/> constructor's own
    /// guard means an out-of-range zoom can never reach the renderer.
    /// </remarks>
    private static Viewport BuildViewport(SubscribeMessage message)
    {
        int width = Math.Clamp(message.Width, 1, ProtocolConstants.DefaultViewportSize);
        int height = Math.Clamp(message.Height, 1, ProtocolConstants.DefaultViewportSize);
        int zoom = Math.Clamp(message.Zoom, 0, Viewport.MaxZoomFor(width, height));

        return new Viewport(message.OriginX, message.OriginY, width, height, zoom);
    }

    public void CompleteOutbound() => _egress.Writer.TryComplete();

    /// <summary>Abandons the connection without waiting for queued frames.</summary>
    public void Close() => _closing.Cancel();

    public async ValueTask DisposeAsync()
    {
        await _closing.CancelAsync().ConfigureAwait(false);
        _egress.Writer.TryComplete();

        await _stream.DisposeAsync().ConfigureAwait(false);
        _owner?.Dispose();
        _closing.Dispose();
    }
}
