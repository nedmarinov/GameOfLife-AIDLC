using System.Net;
using System.Net.Sockets;
using GameOfLife.Core;

namespace GameOfLife.Server;

/// <summary>Accepts TCP connections and runs the simulation they share.</summary>
internal sealed class GameServer(PatternStore patterns, int port, int tickMilliseconds)
{
    /// <summary>How long a closing connection may spend flushing queued frames.</summary>
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(2);

    private readonly SimulationHost _simulation = new(patterns, tickMilliseconds);
    private int _nextClientId;

    public IPEndPoint? Endpoint { get; private set; }

    public void Seed(IEnumerable<Cell> cells, ulong generation = 0) =>
        _simulation.Seed(cells, generation);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        Endpoint = (IPEndPoint)listener.LocalEndpoint;

        Console.WriteLine($"Game of Life server listening on {Endpoint}");
        Console.WriteLine($"Patterns directory: {patterns.Root}");

        // The simulation runs independently of accepting: a burst of
        // connections must not perturb the tick rate.
        Task simulation = _simulation.RunAsync(cancellationToken);
        Task accepting = AcceptLoopAsync(listener, cancellationToken);

        try
        {
            await Task.WhenAll(simulation, accepting).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient socket = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);

                // Each connection is served on its own task and is never awaited
                // here: one client's lifetime must not gate anyone else's.
                _ = ServeAsync(socket, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException)
        {
        }
    }

    private async Task ServeAsync(TcpClient socket, CancellationToken cancellationToken)
    {
        var client = new ClientConnection(socket, Interlocked.Increment(ref _nextClientId));

        Console.WriteLine($"{client} connected from {socket.Client.RemoteEndPoint}");

        await _simulation.Commands.WriteAsync(new ClientConnected(client), cancellationToken)
            .ConfigureAwait(false);

        try
        {
            // The connection ends when either direction does: a dead socket
            // stops both loops rather than leaving one spinning.
            Task reading = client.ReadLoopAsync(_simulation.Commands, cancellationToken);
            Task writing = client.WriteLoopAsync(cancellationToken);

            await Task.WhenAny(reading, writing).ConfigureAwait(false);

            // Stop accepting new frames, then let the writer flush what is
            // already queued. A connection dropped for a protocol violation has
            // an ErrorMessage waiting in that queue, and closing without
            // flushing would leave the client guessing why it was hung up on.
            client.CompleteOutbound();

            try
            {
                await writing.WaitAsync(FlushTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is TimeoutException or OperationCanceledException)
            {
                // A peer that will not read cannot hold the connection open.
            }
        }
        finally
        {
            client.Close();

            await _simulation.Commands.WriteAsync(new ClientDisconnected(client), CancellationToken.None)
                .ConfigureAwait(false);

            Console.WriteLine(
                $"{client} disconnected" +
                (client.DroppedFrames > 0 ? $" (dropped {client.DroppedFrames} frames)" : string.Empty));

            await client.DisposeAsync().ConfigureAwait(false);
        }
    }
}
