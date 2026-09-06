using System.Text;
using System.Threading.Channels;
using GameOfLife.Client.Console;
using GameOfLife.Core;
using GameOfLife.Protocol;

string host = "127.0.0.1";
int port = ProtocolConstants.DefaultPort;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--host" when i + 1 < args.Length:
            host = args[++i];
            break;

        case "--port" when i + 1 < args.Length:
            port = int.Parse(args[++i]);
            break;

        case "--help" or "-h":
            System.Console.WriteLine("""
                Conway's Game of Life -- console client

                  --host <name>   server host (default 127.0.0.1)
                  --port <n>      server port (default 5150)

                Keys
                  arrows      move the cursor (the window follows at the edge)
                  shift+arrow pan the window by half a screen
                  space       toggle the cell under the cursor (paused only)
                  s           start / pause
                  n           single step (paused only)
                  + / -       faster / slower
                  c           clear the universe
                  o / w       load / save a pattern file
                  z / x       zoom out / in
                  Z           zoom all the way out (whole universe)
                  g           jump back to the centre of the universe, 1:1
                  q           quit
                """);
            return 0;
    }
}

const int Size = ProtocolConstants.DefaultViewportSize;

// Start centred on 2^63 so the shipped patterns, which sit near the middle of
// the universe, are visible immediately.
var viewport = new Viewport(1UL << 63, 1UL << 63, Size, Size);
Cell cursor = viewport.CellAt(Size / 2, Size / 2);

byte[] bitmap = new byte[viewport.BitmapByteCount];
ulong generation = 0;
int population = 0;
bool running = false;
int tickMilliseconds = ProtocolConstants.DefaultTickMilliseconds;
string? notice = Screen.SizeWarning(viewport);
string? prompt = null;
StringBuilder promptText = new();

using var shutdown = new CancellationTokenSource();

await using var link = new ServerLink(host, port);
var screen = new Screen();

// Re-subscribe on every connection: the server keeps no memory of a client
// that went away, so a reconnect must restate the window it wants.
link.Connected += () => link.SendAsync(new SubscribeMessage
{
    OriginX = viewport.OriginX,
    OriginY = viewport.OriginY,
    Width = viewport.Width,
    Height = viewport.Height,
    Zoom = viewport.Zoom,
}, shutdown.Token);

Screen.Enter();

// Restore the terminal even if something throws or the process is interrupted.
AppDomain.CurrentDomain.ProcessExit += (_, _) => Screen.Leave();
System.Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdown.Cancel();
};

var keys = Channel.CreateUnbounded<ConsoleKeyInfo>(
    new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

// Both run for the life of the client; neither is awaited here, because the
// render loop below is what decides when the client exits.
_ = link.RunAsync(shutdown.Token);
_ = ReadKeysAsync(shutdown.Token);

try
{
    await RunAsync();
}
finally
{
    await shutdown.CancelAsync();
    Screen.Leave();
}

return 0;

async Task RunAsync()
{
    var redraw = new PeriodicTimer(TimeSpan.FromMilliseconds(16));

    while (!shutdown.IsCancellationRequested)
    {
        while (keys.Reader.TryRead(out ConsoleKeyInfo key))
        {
            if (prompt is not null) await HandlePromptKeyAsync(key);
            else if (!await HandleKeyAsync(key)) return;
        }

        while (link.Inbound.TryRead(out Inbound frame)) Consume(frame);

        Draw();

        try
        {
            await redraw.WaitForNextTickAsync(shutdown.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
    }
}

void Consume(Inbound frame)
{
    switch (frame.Type)
    {
        case FrameType.Viewport:
            (Viewport window, ulong gen, byte[] bits) = ViewportFrame.Read(frame.Payload);

            // If the zoom or origin changed under us -- a zoom recentres the
            // window -- re-derive the cursor from its displayed position, so it
            // stays where the user is looking and keeps addressing a cell the
            // server would actually edit.
            if (window.Zoom != viewport.Zoom || !window.TryLocate(cursor, out _, out _))
                cursor = window.CellAt(window.Width / 2, window.Height / 2);
            else if (window.TryLocate(cursor, out int cx, out int cy))
                cursor = window.CellAt(cx, cy);

            // The server is authoritative about the window: if a pan has not
            // round-tripped yet, its answer wins over our optimistic guess.
            viewport = window;
            generation = gen;
            bitmap = bits;
            break;

        case FrameType.Control:
            switch (ControlCodec.Read(frame.Payload))
            {
                case HelloMessage hello:
                    running = hello.Running;
                    generation = hello.Generation;
                    population = hello.Population;
                    tickMilliseconds = hello.TickMilliseconds;
                    notice = null;
                    break;

                case StatusMessage status:
                    running = status.Running;
                    generation = status.Generation;
                    population = status.Population;
                    tickMilliseconds = status.TickMilliseconds;
                    break;

                case ErrorMessage error:
                    notice = error.Message;
                    break;
            }

            break;
    }
}

async Task<bool> HandleKeyAsync(ConsoleKeyInfo key)
{
    bool shift = (key.Modifiers & ConsoleModifiers.Shift) != 0;
    int stride = shift ? Size / 2 : 1;

    switch (key.Key)
    {
        case ConsoleKey.LeftArrow: await MoveAsync(-stride, 0, shift); break;
        case ConsoleKey.RightArrow: await MoveAsync(stride, 0, shift); break;
        case ConsoleKey.UpArrow: await MoveAsync(0, -stride, shift); break;
        case ConsoleKey.DownArrow: await MoveAsync(0, stride, shift); break;

        case ConsoleKey.Spacebar:
            await link.SendAsync(new ToggleMessage { X = cursor.X, Y = cursor.Y }, shutdown.Token);
            break;

        case ConsoleKey.S:
            await link.SendAsync(new ControlMessage
            {
                Action = running ? ControlAction.Pause : ControlAction.Start,
            }, shutdown.Token);
            break;

        case ConsoleKey.N:
            await link.SendAsync(new ControlMessage { Action = ControlAction.Step }, shutdown.Token);
            break;

        case ConsoleKey.C:
            await link.SendAsync(new ControlMessage { Action = ControlAction.Clear }, shutdown.Token);
            break;

        case ConsoleKey.Z when shift:
            await link.SendAsync(new ZoomMessage { Delta = 99 }, shutdown.Token);
            break;

        case ConsoleKey.Z:
            await link.SendAsync(new ZoomMessage { Delta = 1 }, shutdown.Token);
            break;

        case ConsoleKey.X:
            await link.SendAsync(new ZoomMessage { Delta = -1 }, shutdown.Token);
            break;

        case ConsoleKey.G:
            viewport = new Viewport(1UL << 63, 1UL << 63, Size, Size);
            cursor = viewport.CellAt(Size / 2, Size / 2);
            await SubscribeAsync();
            break;

        case ConsoleKey.O:
            prompt = "load: ";
            promptText.Clear();
            break;

        case ConsoleKey.W:
            prompt = "save as: ";
            promptText.Clear();
            break;

        case ConsoleKey.Q:
            return false;

        default:
            // '+' and '-' arrive as OemPlus/OemMinus or Add/Subtract depending
            // on keyboard and platform, so match the character instead.
            if (key.KeyChar is '+' or '=')
            {
                await link.SendAsync(new ControlMessage
                {
                    Action = ControlAction.Speed,
                    Value = Math.Max(10, tickMilliseconds / 2),
                }, shutdown.Token);
            }
            else if (key.KeyChar is '-' or '_')
            {
                await link.SendAsync(new ControlMessage
                {
                    Action = ControlAction.Speed,
                    Value = Math.Min(5_000, tickMilliseconds * 2),
                }, shutdown.Token);
            }

            break;
    }

    return true;
}

async Task MoveAsync(long dx, long dy, bool panning)
{
    if (panning)
    {
        viewport = viewport.Pan(dx, dy);
        cursor = cursor.Offset(dx, dy);
        await link.SendAsync(new PanMessage { Dx = dx, Dy = dy }, shutdown.Token);
        return;
    }

    Cell moved = cursor.Offset(dx, dy);

    // Push the window when the cursor would leave it, so the edge is a soft
    // boundary rather than a wall.
    if (!viewport.TryLocate(moved, out _, out _))
    {
        viewport = viewport.Pan(dx, dy);
        await link.SendAsync(new PanMessage { Dx = dx, Dy = dy }, shutdown.Token);
    }

    cursor = moved;
}

Task SubscribeAsync() => link.SendAsync(new SubscribeMessage
{
    OriginX = viewport.OriginX,
    OriginY = viewport.OriginY,
    Width = viewport.Width,
    Height = viewport.Height,
    Zoom = viewport.Zoom,
}, shutdown.Token);

async Task HandlePromptKeyAsync(ConsoleKeyInfo key)
{
    switch (key.Key)
    {
        case ConsoleKey.Enter:
            string file = promptText.ToString().Trim();

            if (file.Length > 0)
            {
                if (prompt!.StartsWith("load", StringComparison.Ordinal))
                    await link.SendAsync(new LoadMessage { File = file }, shutdown.Token);
                else
                    await link.SendAsync(new SaveMessage { File = file }, shutdown.Token);
            }

            prompt = null;
            break;

        case ConsoleKey.Escape:
            prompt = null;
            break;

        case ConsoleKey.Backspace:
            if (promptText.Length > 0) promptText.Length--;
            break;

        default:
            if (!char.IsControl(key.KeyChar)) promptText.Append(key.KeyChar);
            break;
    }
}

void Draw()
{
    string connection = link.IsConnected ? "connected" : "reconnecting...";

    string status =
        $"\e[1mgen\e[0m {generation}   " +
        $"\e[1mpop\e[0m {population}   " +
        $"\e[1m{(running ? "RUNNING" : "PAUSED ")}\e[0m   " +
        $"\e[1mtick\e[0m {tickMilliseconds}ms   " +
        $"\e[1mcursor\e[0m ({cursor.X}, {cursor.Y})   " +
        $"\e[1morigin\e[0m ({viewport.OriginX}, {viewport.OriginY})   " +
        $"\e[1mzoom\e[0m {viewport.Zoom}" +
        (viewport.Zoom > 0 ? $" ({viewport.Scale} cells/px)" : " (1:1)") + "   " +
        connection;

    string help = notice
        ?? "arrows move  shift+arrows pan  z/x zoom out/in  Z whole universe  space toggle  "
         + "s start/pause  n step  +/- speed  c clear  o load  w save  g centre  q quit";

    screen.Draw(viewport, bitmap, cursor, status, help, prompt is null ? null : prompt + promptText);
}

async Task ReadKeysAsync(CancellationToken cancellationToken)
{
    // With stdin redirected there is no keyboard to poll and KeyAvailable
    // throws. That happens whenever the client is piped or run from a script,
    // so it is a supported way to run as a pure observer, not an error.
    if (System.Console.IsInputRedirected) return;

    while (!cancellationToken.IsCancellationRequested)
    {
        // KeyAvailable keeps this from blocking on a key that never comes,
        // which would otherwise hold the process open at shutdown.
        if (!System.Console.KeyAvailable)
        {
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            continue;
        }

        await keys.Writer.WriteAsync(System.Console.ReadKey(intercept: true), cancellationToken)
            .ConfigureAwait(false);
    }
}
