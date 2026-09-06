using GameOfLife.Core;
using GameOfLife.Protocol;

namespace GameOfLife.Server;

/// <summary>
/// Work for the simulation thread.
/// </summary>
/// <remarks>
/// Every mutation of simulation state — including a client connecting or
/// disconnecting — arrives as one of these, through a single channel, and is
/// applied by the one thread that owns the universe. The client list is
/// therefore ordinary and unsynchronised too: nothing else ever touches it. See
/// ADR 0003.
/// </remarks>
internal abstract record Command;

internal sealed record ClientConnected(ClientConnection Client) : Command;

internal sealed record ClientDisconnected(ClientConnection Client) : Command;

internal sealed record SetViewport(ClientConnection Client, Viewport Viewport) : Command;

internal sealed record PanViewport(ClientConnection Client, long Dx, long Dy) : Command;

internal sealed record ZoomViewport(ClientConnection Client, int Delta) : Command;

internal sealed record ToggleCell(ClientConnection Client, Cell Cell) : Command;

internal sealed record Control(ClientConnection Client, ControlAction Action, int Value) : Command;

internal sealed record LoadPattern(ClientConnection Client, string File) : Command;

internal sealed record SavePattern(ClientConnection Client, string File) : Command;
