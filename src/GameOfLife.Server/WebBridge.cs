using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace GameOfLife.Server;

/// <summary>
/// Serves the browser client and bridges it onto the same simulation.
/// </summary>
/// <remarks>
/// <para>
/// A deliberately small HTTP surface: one page and one upgrade. Bringing in a
/// web framework to serve a single static file and perform one handshake would
/// cost more than it saves, and the handshake itself is four lines of
/// specification — hash the client's key with a fixed GUID and echo it back.
/// </para>
/// <para>
/// Frame handling is <em>not</em> hand-rolled: after the handshake the stream is
/// handed to <see cref="WebSocket.CreateFromStream"/>, so masking, fragmentation,
/// control frames and close handshakes come from the runtime. Owning the parts
/// worth owning is the point; reimplementing RFC 6455 framing is not.
/// </para>
/// </remarks>
internal sealed class WebBridge(GameServer server, int port, string? pagePath)
{
    /// <summary>The fixed GUID every RFC 6455 handshake concatenates with the client key.</summary>
    private const string HandshakeGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    private const int MaxRequestBytes = 16 * 1024;

    public IPEndPoint? Endpoint { get; private set; }

    /// <summary>Computes the <c>Sec-WebSocket-Accept</c> response for a client key.</summary>
    internal static string AcceptFor(string clientKey) =>
        Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(clientKey + HandshakeGuid)));

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        Endpoint = (IPEndPoint)listener.LocalEndpoint;

        Console.WriteLine($"Browser client at http://{Endpoint}/");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient socket = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                socket.NoDelay = true;

                _ = HandleAsync(socket, cancellationToken);
            }
        }
        catch (Exception error) when (error is OperationCanceledException or SocketException)
        {
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleAsync(TcpClient socket, CancellationToken cancellationToken)
    {
        NetworkStream stream = socket.GetStream();

        try
        {
            (string? requestLine, Dictionary<string, string> headers) =
                await ReadRequestAsync(stream, cancellationToken).ConfigureAwait(false);

            if (requestLine is null)
            {
                socket.Dispose();
                return;
            }

            bool upgrading =
                headers.TryGetValue("upgrade", out string? upgrade)
                && upgrade.Contains("websocket", StringComparison.OrdinalIgnoreCase);

            if (upgrading)
            {
                await UpgradeAsync(socket, stream, headers, cancellationToken).ConfigureAwait(false);
                return;
            }

            await ServePageAsync(stream, cancellationToken).ConfigureAwait(false);
            socket.Dispose();
        }
        catch (Exception error) when (error is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            socket.Dispose();
        }
    }

    private async Task UpgradeAsync(
        TcpClient socket,
        NetworkStream stream,
        Dictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        if (!headers.TryGetValue("sec-websocket-key", out string? key))
        {
            await WriteAsync(stream, "HTTP/1.1 400 Bad Request\r\n\r\n", cancellationToken).ConfigureAwait(false);
            socket.Dispose();
            return;
        }

        string response =
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {AcceptFor(key)}\r\n\r\n";

        await WriteAsync(stream, response, cancellationToken).ConfigureAwait(false);

        WebSocket webSocket = WebSocket.CreateFromStream(
            stream,
            new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = TimeSpan.FromSeconds(30) });

        // From here the browser is an ordinary client: same frames, same
        // commands, same simulation.
        await server.ServeAsync(
            new WebSocketStream(webSocket),
            $"{socket.Client.RemoteEndPoint} (browser)",
            socket,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ServePageAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        if (pagePath is null || !File.Exists(pagePath))
        {
            await WriteAsync(stream,
                "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        byte[] body = await File.ReadAllBytesAsync(pagePath, cancellationToken).ConfigureAwait(false);

        string header =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Cache-Control: no-store\r\n" +
            "Connection: close\r\n\r\n";

        await WriteAsync(stream, header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the request line and headers, stopping at the blank line.</summary>
    private static async Task<(string? RequestLine, Dictionary<string, string> Headers)> ReadRequestAsync(
        NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new ArrayBufferWriter<byte>(1024);
        byte[] scratch = new byte[512];

        while (buffer.WrittenCount < MaxRequestBytes)
        {
            int read = await stream.ReadAsync(scratch, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;

            buffer.Write(scratch.AsSpan(0, read));

            string text = Encoding.ASCII.GetString(buffer.WrittenSpan);
            int end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (end < 0) continue;

            string[] lines = text[..end].Split("\r\n");
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string line in lines.Skip(1))
            {
                int colon = line.IndexOf(':');
                if (colon > 0) headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }

            return (lines[0], headers);
        }

        // A request that never terminates, or is absurdly long, is not one we
        // are going to answer.
        return (null, []);
    }

    private static Task WriteAsync(Stream stream, string text, CancellationToken cancellationToken) =>
        stream.WriteAsync(Encoding.ASCII.GetBytes(text), cancellationToken).AsTask();
}
